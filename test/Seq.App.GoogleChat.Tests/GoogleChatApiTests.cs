using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Seq.App.GoogleChat.Api;
using Xunit;

namespace Seq.App.GoogleChat.Tests
{
    public sealed class GoogleChatApiTests
    {
        private const string Webhook = "https://chat.googleapis.com/v1/spaces/AAA/messages?key=SECRET-KEY&token=SECRET-TOKEN";

        private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-11-12T10:00:00Z"));
        private readonly List<TimeSpan> _delays = [];

        private GoogleChatApi CreateApi(StubHandler handler) =>
            new(new HttpClient(handler), ownsHttpClient: true, delay: d =>
            {
                _delays.Add(d);
                _time.Advance(d);
                return Task.CompletedTask;
            }, _time);

        private static GoogleChatMessage Message() => new(new Card { Header = new Header { Title = "t" } });

        [Fact]
        public async Task Delivers_on_first_success()
        {
            var handler = new StubHandler(HttpStatusCode.OK);

            await CreateApi(handler).SendMessageAsync(Webhook, Message());

            Assert.Equal(1, handler.Calls);
            Assert.Empty(_delays);
        }

        [Fact]
        public async Task Retries_429_honouring_Retry_After()
        {
            var tooMany = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            tooMany.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            var handler = new StubHandler(tooMany, new HttpResponseMessage(HttpStatusCode.OK));

            await CreateApi(handler).SendMessageAsync(Webhook, Message());

            Assert.Equal(2, handler.Calls);
            Assert.Contains(TimeSpan.FromSeconds(7), _delays);
        }

        [Fact]
        public async Task Caps_Retry_After()
        {
            var tooMany = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            tooMany.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromHours(1));
            var handler = new StubHandler(tooMany, new HttpResponseMessage(HttpStatusCode.OK));

            await CreateApi(handler).SendMessageAsync(Webhook, Message());

            Assert.Contains(GoogleChatApi.MaxRetryDelay, _delays);
        }

        [Fact]
        public async Task Retries_network_failure()
        {
            var handler = new StubHandler(new HttpRequestException("connection reset"), new HttpResponseMessage(HttpStatusCode.OK));

            await CreateApi(handler).SendMessageAsync(Webhook, Message());

            Assert.Equal(2, handler.Calls);
        }

        [Fact]
        public async Task Gives_up_after_max_attempts_on_5xx()
        {
            var handler = new StubHandler(HttpStatusCode.ServiceUnavailable);

            var ex = await Assert.ThrowsAsync<GoogleChatDeliveryException>(() => CreateApi(handler).SendMessageAsync(Webhook, Message()));

            Assert.Equal(GoogleChatApi.MaxAttempts, handler.Calls);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        }

        [Fact]
        public async Task Does_not_retry_400_and_never_reveals_the_webhook()
        {
            var handler = new StubHandler(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("Invalid JSON payload") });

            var ex = await Assert.ThrowsAsync<GoogleChatDeliveryException>(() => CreateApi(handler).SendMessageAsync(Webhook, Message()));

            Assert.Equal(1, handler.Calls);
            Assert.Contains("Invalid JSON payload", ex.Message);
            Assert.DoesNotContain("SECRET", ex.ToString());
        }

        [Fact]
        public async Task Spaces_consecutive_posts_by_the_minimum_interval()
        {
            var handler = new StubHandler(HttpStatusCode.OK);
            var api = CreateApi(handler);

            await api.SendMessageAsync(Webhook, Message());
            await api.SendMessageAsync(Webhook, Message());

            Assert.Equal([GoogleChatApi.MinInterval], _delays);
        }

        [Fact]
        public async Task Rejects_empty_arguments()
        {
            var api = CreateApi(new StubHandler(HttpStatusCode.OK));

            await Assert.ThrowsAsync<ArgumentException>(() => api.SendMessageAsync("", Message()));
            await Assert.ThrowsAsync<ArgumentNullException>(() => api.SendMessageAsync(Webhook, null));
        }

        [Fact]
        public void Serializes_camel_case_without_nulls_and_keeps_cyrillic_as_utf8()
        {
            var json = GoogleChatApi.Serialize(new GoogleChatMessage(new Card
            {
                Header = new Header { Title = "Ошибка <b>" },
                Sections = [new Section { Collapsible = true, UncollapsibleWidgetsCount = 1, Widgets = [new Widget("x")] }]
            }));

            Assert.Contains("\"cardsV2\"", json);
            Assert.Contains("\"collapsible\":true", json);
            Assert.Contains("\"uncollapsibleWidgetsCount\":1", json);
            Assert.Contains("Ошибка <b>", json);
            Assert.DoesNotContain("subtitle", json);
            Assert.DoesNotContain("maxLines", json);
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Queue<object> _responses;
            private readonly HttpStatusCode? _always;

            public StubHandler(HttpStatusCode always) => _always = always;

            public StubHandler(params object[] responses) => _responses = new Queue<object>(responses);

            public int Calls { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Calls++;
                Assert.Equal(HttpMethod.Post, request.Method);

                if (_always.HasValue)
                    return Task.FromResult(new HttpResponseMessage(_always.Value));

                return _responses.Dequeue() switch
                {
                    Exception e => Task.FromException<HttpResponseMessage>(e),
                    HttpResponseMessage r => Task.FromResult(r),
                    _ => throw new InvalidOperationException()
                };
            }
        }
    }
}
