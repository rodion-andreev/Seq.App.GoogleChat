using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Seq.App.GoogleChat.Api
{
    /// <summary>
    /// Posts card messages to a Google Chat incoming webhook.
    /// </summary>
    /// <remarks>
    /// Google Chat accepts one request per second per space, shared by every webhook in it, so posts are
    /// spaced at least <see cref="MinInterval"/> apart and a 429 or 5xx answer is retried with backoff.
    /// Nothing this class throws contains the webhook URL: its query string carries the space's key and token.
    /// </remarks>
    public sealed class GoogleChatApi : IGoogleChatApi, IDisposable
    {
        internal const int MaxAttempts = 4;
        internal static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(1100);
        internal static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);
        internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
        private const int MaxResponseBodyInError = 500;

        // Relaxed escaping keeps non-ASCII text (Cyrillic, etc.) as UTF-8 instead of \uXXXX, which matters
        // for the 32,000-byte message limit; the payload is never embedded in HTML, so it is safe here.
        internal static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private readonly Func<TimeSpan, Task> _delay;
        private readonly TimeProvider _timeProvider;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private DateTimeOffset _lastPostAt = DateTimeOffset.MinValue;

        /// <param name="proxyServer">Proxy address, or null/empty to connect directly.</param>
        public GoogleChatApi(string proxyServer)
            : this(CreateHttpClient(proxyServer), ownsHttpClient: true, delay: null, timeProvider: null)
        {
        }

        /// <param name="httpClient">A client owned by the caller; it is not disposed with this instance.</param>
        public GoogleChatApi(HttpClient httpClient)
            : this(httpClient, ownsHttpClient: false, delay: null, timeProvider: null)
        {
        }

        internal GoogleChatApi(HttpClient httpClient, bool ownsHttpClient, Func<TimeSpan, Task> delay, TimeProvider timeProvider)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _ownsHttpClient = ownsHttpClient;
            _delay = delay ?? (d => Task.Delay(d));
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public async Task SendMessageAsync(string webhookUrl, GoogleChatMessage message)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(webhookUrl);
            ArgumentNullException.ThrowIfNull(message);

            var payload = Serialize(message);

            await _gate.WaitAsync();
            try
            {
                for (var attempt = 1; ; attempt++)
                {
                    await WaitForSlotAsync();

                    var outcome = await PostOnceAsync(webhookUrl, payload);
                    if (outcome.Delivered)
                        return;

                    if (!outcome.Retryable || attempt == MaxAttempts)
                        throw outcome.ToException(attempt);

                    await _delay(outcome.RetryAfter ?? Backoff(attempt));
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        internal static string Serialize(GoogleChatMessage message) =>
            JsonSerializer.Serialize(message, SerializerOptions);

        private async Task WaitForSlotAsync()
        {
            var wait = _lastPostAt + MinInterval - _timeProvider.GetUtcNow();
            if (wait > TimeSpan.Zero)
                await _delay(wait);
        }

        private async Task<PostOutcome> PostOnceAsync(string webhookUrl, string payload)
        {
            _lastPostAt = _timeProvider.GetUtcNow();
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var timeout = new CancellationTokenSource(RequestTimeout);
            try
            {
                using var response = await _httpClient.PostAsync(webhookUrl, content, timeout.Token);
                if (response.IsSuccessStatusCode)
                    return PostOutcome.Success;

                var body = await response.Content.ReadAsStringAsync();
                return PostOutcome.Rejected(response.StatusCode, body, RetryAfter(response.Headers.RetryAfter));
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                return PostOutcome.Failed(new TimeoutException($"Google Chat did not answer within {RequestTimeout.TotalSeconds:0} s."));
            }
            catch (HttpRequestException ex)
            {
                return PostOutcome.Failed(ex);
            }
        }

        private TimeSpan? RetryAfter(RetryConditionHeaderValue header)
        {
            var delay = header?.Delta ?? (header?.Date - _timeProvider.GetUtcNow());
            if (delay == null)
                return null;
            if (delay < TimeSpan.Zero)
                return TimeSpan.Zero;
            return delay > MaxRetryDelay ? MaxRetryDelay : delay;
        }

        private static TimeSpan Backoff(int attempt) => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));

        private static HttpClient CreateHttpClient(string proxyServer)
        {
            if (string.IsNullOrWhiteSpace(proxyServer))
                return new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

            var handler = new HttpClientHandler
            {
                Proxy = new WebProxy(proxyServer, false) { UseDefaultCredentials = true },
                UseProxy = true,
                PreAuthenticate = true,
                UseDefaultCredentials = true
            };
            return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        }

        public void Dispose()
        {
            if (_ownsHttpClient)
                _httpClient.Dispose();
            _gate.Dispose();
        }

        private sealed class PostOutcome
        {
            public static readonly PostOutcome Success = new() { Delivered = true };

            public bool Delivered { get; private init; }
            public bool Retryable { get; private init; }
            public TimeSpan? RetryAfter { get; private init; }
            private HttpStatusCode? Status { get; init; }
            private string Body { get; init; }
            private Exception Error { get; init; }

            public static PostOutcome Rejected(HttpStatusCode status, string body, TimeSpan? retryAfter) => new()
            {
                Status = status,
                Body = body,
                RetryAfter = retryAfter,
                Retryable = status == HttpStatusCode.TooManyRequests || (int)status >= 500
            };

            public static PostOutcome Failed(Exception error) => new() { Error = error, Retryable = true };

            public Exception ToException(int attempts)
            {
                var tries = attempts == 1 ? "1 attempt" : $"{attempts} attempts";
                if (Error != null)
                    return new GoogleChatDeliveryException($"Could not reach Google Chat ({tries}): {Error.Message}", null, Error);

                var body = Body ?? "";
                if (body.Length > MaxResponseBodyInError)
                    body = body[..MaxResponseBodyInError] + "...";
                return new GoogleChatDeliveryException(
                    $"Google Chat rejected the message with {(int)Status} {Status} ({tries}). Response: {body}", Status, null);
            }
        }
    }
}
