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
        public void Shows_a_few_attachments_in_full()
        {
            var section = Builder().BuildMessage(Event()).CardsV2[0].Card.Sections[0];

            Assert.Equal(3, section.Widgets.Count);
            Assert.Null(section.Collapsible);
        }

        [Fact]
        public void Collapses_a_long_attachment_list_behind_the_link()
        {
            var properties = Enumerable.Range(0, 5).ToDictionary(i => $"P{i}", i => (object)i);

            var section = Builder().BuildMessage(Event(properties: properties)).CardsV2[0].Card.Sections[0];

            Assert.True(section.Collapsible);
            Assert.Equal(1, section.UncollapsibleWidgetsCount);
        }

        [Fact]
        public void Shows_only_the_included_key_fields()
        {
            var properties = new Dictionary<string, object>
            {
                ["Id"] = "x", ["Host"] = "h", ["ApplicationName"] = "Portal.Api", ["RequestPath"] = "/health", ["ConnectionId"] = "c"
            };

            var texts = Texts(Builder(false, "ApplicationName", "RequestPath").BuildMessage(Event(properties: properties))).Skip(1).ToList();

            Assert.Equal(["<b>ApplicationName:</b> Portal.Api", "<b>RequestPath:</b> /health"], texts);
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

        // The shape Seq 2025.1 sends to an app instance when an alert triggers (captured 2026-11-13).
        private static Event<LogEventData> AlertEvent(object[] results, Dictionary<string, object> extra = null)
        {
            var properties = new Dictionary<string, object>
            {
                ["@seqid"] = "event-0092bb64416b08df0000000000000000",
                ["NamespacedAlertTitle"] = "Errors by app",
                ["Alert"] = new Dictionary<string, object>
                {
                    ["Id"] = "alert-23",
                    ["Title"] = "Errors by app",
                    ["Url"] = "https://seq.example.com/#/alerts/alert-23",
                    ["Query"] = "select count(*) as count from stream where @Level = 'Error' group by time(1m), ApplicationName having count > 0 limit 100"
                },
                ["Source"] = new Dictionary<string, object>
                {
                    ["RangeStart"] = "2026-11-13T09:45:03.6822116Z",
                    ["RangeEnd"] = "2026-11-13T09:46:03.6822116Z",
                    ["ResultsUrl"] = "https://seq.example.com/#/events?q=select&from=2026-11-13T09:45:03Z",
                    ["Results"] = results,
                    ["ContributingEvents"] = null
                },
                ["SuppressedUntil"] = "2026-11-13T09:47:33.6822116Z",
                ["Failures"] = null
            };
            foreach (var kv in extra ?? []) properties[kv.Key] = kv.Value;

            return new("event-0092bb64", 1, DateTime.UtcNow, new LogEventData
            {
                Id = "event-0092bb64",
                Level = LogEventLevel.Error,
                RenderedMessage = "Alert condition triggered by Errors by app",
                LocalTimestamp = new DateTimeOffset(2026, 11, 13, 15, 46, 3, TimeSpan.FromHours(6)),
                Properties = properties
            });
        }

        private static readonly object[] TwoRows =
        [
            new object[] { "time", "ApplicationName", "count" },
            new object[] { "2026-11-13T09:45:03.6822116Z", "Voora.Ai.Portal.Api", 3L },
            new object[] { "2026-11-13T09:45:03.6822116Z", "Identity.Api", 1L }
        ];

        [Fact]
        public void Renders_an_alert_notification_with_links_period_and_results()
        {
            var message = Builder().BuildMessage(AlertEvent(TwoRows));
            var card = message.CardsV2[0].Card;
            var texts = Texts(message).ToList();

            Assert.Equal("🔴 Errors by app", card.Header.Title);
            Assert.Equal("Alert condition triggered by Errors by app", card.Sections[0].Header);
            Assert.Contains("View the results", texts[0]);
            Assert.Contains("https://seq.example.com/#/alerts/alert-23", texts[0]);
            Assert.DoesNotContain(texts, t => t.Contains("View this event in Seq"));
            Assert.Equal("<b>Period:</b> 2026-11-13 15:45:03 +06:00 – 2026-11-13 15:46:03 +06:00", texts[1]);
            Assert.Equal("<b>Results:</b><br>time: 2026-11-13 15:45:03 +06:00, ApplicationName: Voora.Ai.Portal.Api, count: 3"
                         + "<br>time: 2026-11-13 15:45:03 +06:00, ApplicationName: Identity.Api, count: 1", texts[2]);
            Assert.DoesNotContain(texts, t => t.Contains("Alert:") || t.Contains("Source:") || t.Contains("@seqid"));
        }

        [Fact]
        public void Keeps_alert_core_visible_and_collapses_attached_properties()
        {
            var section = Builder().BuildMessage(AlertEvent(TwoRows, new() { ["Environment"] = "Prod KG", ["A"] = "1", ["B"] = "2", ["C"] = "3" })).CardsV2[0].Card.Sections[0];

            Assert.Contains(section.Widgets, w => w.TextParagraph.Text == "<b>Environment:</b> Prod KG");
            Assert.True(section.Collapsible);
            Assert.Equal(3, section.UncollapsibleWidgetsCount);
        }

        [Fact]
        public void Shows_a_zero_count_for_a_no_logs_alert()
        {
            var results = new object[] { new object[] { "time", "count" }, new object[] { "2026-11-13T09:45:03Z", 0L } };

            var texts = Texts(Builder().BuildMessage(AlertEvent(results)));

            Assert.Contains("<b>Results:</b><br>time: 2026-11-13 15:45:03 +06:00, count: 0", texts);
        }

        [Fact]
        public void Caps_the_number_of_result_rows()
        {
            var rows = new List<object> { new object[] { "ApplicationName", "count" } };
            rows.AddRange(Enumerable.Range(0, 30).Select(i => (object)new object[] { $"app{i}", 1L }));

            var results = Texts(Builder().BuildMessage(AlertEvent(rows.ToArray()))).Single(t => t.StartsWith("<b>Results:"));

            Assert.Contains("app19", results);
            Assert.DoesNotContain("app20", results);
            Assert.EndsWith("… 10 more row(s)", results);
        }
    }
}
