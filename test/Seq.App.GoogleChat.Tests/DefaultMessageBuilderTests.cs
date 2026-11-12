using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Seq.App.GoogleChat.Api;
using Seq.App.GoogleChat.Formatting;
using Seq.App.GoogleChat.Messages;
using Seq.Apps;
using Seq.Apps.LogEvents;
using Xunit;

namespace Seq.App.GoogleChat.Tests
{
    public class DefaultMessageBuilderTests
    {
        private readonly Host _host = new("https://seq.example.com/", "Prod KG");

        private static Event<LogEventData> Event(string message = "Payment <failed> & retried", string exception = null,
            Dictionary<string, object> properties = null, LogEventLevel level = LogEventLevel.Error) =>
            new("event-1", 1, DateTime.UtcNow, new LogEventData
            {
                Id = "event-1",
                Level = level,
                RenderedMessage = message,
                Exception = exception,
                LocalTimestamp = new DateTimeOffset(2026, 11, 12, 16, 0, 0, TimeSpan.FromHours(6)),
                Properties = properties ?? new Dictionary<string, object> { ["MerchantId"] = "m-1", ["Count"] = 3 }
            });

        private DefaultMessageBuilder Builder(bool exclude = false, params string[] included) =>
            new(_host, new PropertyValueFormatter(null), exclude, included);

        private static IEnumerable<string> Texts(GoogleChatMessage m) =>
            m.CardsV2[0].Card.Sections[0].Widgets.Select(w => w.TextParagraph.Text);

        [Fact]
        public void Escapes_event_text_and_builds_header()
        {
            var message = Builder().BuildMessage(Event());
            var card = message.CardsV2[0].Card;

            Assert.Equal("🔴 Error", card.Header.Title);
            Assert.Equal("Prod KG · 2026-11-12 16:00:00 +06:00", card.Header.Subtitle);
            Assert.Equal("Payment &lt;failed&gt; &amp; retried", card.Sections[0].Header);
            Assert.StartsWith("<a href=\"https://seq.example.com/#/events?filter=", Texts(message).First());
            Assert.Contains("<b>MerchantId:</b> m-1", Texts(message));
            Assert.Contains("<b>Count:</b> 3", Texts(message));
        }

        [Fact]
        public void Collapses_attachments_behind_the_link()
        {
            var section = Builder().BuildMessage(Event()).CardsV2[0].Card.Sections[0];

            Assert.True(section.Collapsible);
            Assert.Equal(1, section.UncollapsibleWidgetsCount);
        }

        [Fact]
        public void Sends_only_the_link_when_attachments_are_excluded()
        {
            var section = Builder(exclude: true).BuildMessage(Event(exception: "boom")).CardsV2[0].Card.Sections[0];

            Assert.Single(section.Widgets);
            Assert.Null(section.Collapsible);
        }

        [Fact]
        public void Attaches_only_included_properties()
        {
            var texts = Texts(Builder(false, "Count").BuildMessage(Event()));

            Assert.Contains("<b>Count:</b> 3", texts);
            Assert.DoesNotContain(texts, t => t.Contains("MerchantId"));
        }

        [Fact]
        public void Renders_exception_lines_as_breaks()
        {
            var texts = Texts(Builder().BuildMessage(Event(exception: "System.Exception: <x>\n   at A.B()")));

            Assert.Contains("<b>Exception:</b><br>System.Exception: &lt;x&gt;<br>   at A.B()", texts);
        }

        [Fact]
        public void Keeps_a_huge_event_under_the_google_chat_limit()
        {
            var properties = Enumerable.Range(0, 40).ToDictionary(i => $"P{i}", _ => (object)new string('ж', 3_000));
            var message = Builder().BuildMessage(Event(message: new string('ы', 5_000), exception: new string('e', 50_000), properties: properties));

            var bytes = Encoding.UTF8.GetByteCount(GoogleChatApi.Serialize(message));

            Assert.True(bytes < 32_000, $"payload is {bytes} bytes");
            Assert.Contains(Texts(message), t => t.Contains("not shown"));
        }

        [Fact]
        public void Falls_back_when_the_message_is_empty()
        {
            var header = Builder().BuildMessage(Event(message: null)).CardsV2[0].Card.Sections[0].Header;

            Assert.Equal("(no message)", header);
        }
    }
}
