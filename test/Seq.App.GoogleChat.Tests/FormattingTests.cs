using System;
using System.Collections.Generic;
using Seq.App.GoogleChat.Formatting;
using Seq.App.GoogleChat.Suppression;
using Xunit;

namespace Seq.App.GoogleChat.Tests
{
    public class FormattingTests
    {
        [Fact]
        public void Escape_does_not_double_escape()
        {
            Assert.Equal("a &lt;b&gt; &amp; c", GoogleChatSyntax.Escape("a <b> & c"));
        }

        [Fact]
        public void Hyperlink_quotes_and_escapes_the_url()
        {
            Assert.Equal("<a href=\"https://x/?a=1&amp;b=2\">go</a>", GoogleChatSyntax.Hyperlink("https://x/?a=1&b=2", "go"));
        }

        [Fact]
        public void Formats_structures_as_json_and_scalars_invariantly()
        {
            var formatter = new PropertyValueFormatter(null);

            Assert.Equal("{\"A\":\"б\"}", formatter.ConvertPropertyValueToString(new Dictionary<string, object> { ["A"] = "б" }));
            Assert.Equal("[1,2]", formatter.ConvertPropertyValueToString(new object[] { 1, 2 }));
            Assert.Equal("1.5", formatter.ConvertPropertyValueToString(1.5m));
            Assert.Equal("null", formatter.ConvertPropertyValueToString(null));
        }

        [Fact]
        public void Truncates_to_the_configured_length()
        {
            Assert.Equal("abc…", new PropertyValueFormatter(3).ConvertPropertyValueToString("abcdef"));
        }

        [Fact]
        public void Suppresses_an_event_type_within_the_window()
        {
            var suppressions = new EventTypeSuppressions(5);
            var t0 = new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);

            Assert.False(suppressions.ShouldSuppressAt(1, t0));
            Assert.True(suppressions.ShouldSuppressAt(1, t0.AddMinutes(4)));
            Assert.False(suppressions.ShouldSuppressAt(2, t0.AddMinutes(4)));
            Assert.False(suppressions.ShouldSuppressAt(1, t0.AddMinutes(6)));
        }

        [Fact]
        public void Zero_or_negative_window_never_suppresses()
        {
            var t0 = DateTime.UtcNow;
            Assert.False(new EventTypeSuppressions(0).ShouldSuppressAt(1, t0));
            var negative = new EventTypeSuppressions(-1);
            Assert.False(negative.ShouldSuppressAt(1, t0));
            Assert.False(negative.ShouldSuppressAt(1, t0));
        }
    }
}
