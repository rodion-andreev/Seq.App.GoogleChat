using Seq.App.GoogleChat.Api;
using Seq.App.GoogleChat.Formatting;
using Seq.Apps;
using Seq.Apps.LogEvents;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Seq.App.GoogleChat.Messages
{
    public class DefaultMessageBuilder
    {
        // Google Chat refuses a message over 32,000 bytes. Text is budgeted in UTF-8 bytes, with headroom for
        // the JSON around each widget, so a large exception or property set is cut short instead of rejected.
        internal const int MaxTextBytes = 24_000;
        internal const int WidgetOverheadBytes = 80;
        internal const int MaxHeaderChars = 1_000;
        internal const int MaxWidgetChars = 4_000;

        private const string StackTraceProperty = "StackTrace";
        private static readonly string[] SpecialProperties = ["Id", "Host"];

        private readonly Host _host;
        private readonly PropertyValueFormatter _propertyValueFormatter;
        private readonly bool _excludeOptionalAttachments;
        private readonly HashSet<string> _includedProperties;

        public DefaultMessageBuilder(Host host, PropertyValueFormatter propertyValueFormatter, bool excludeOptionalAttachments, IEnumerable<string> includedProperties)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _propertyValueFormatter = propertyValueFormatter ?? throw new ArgumentNullException(nameof(propertyValueFormatter));
            _includedProperties = [.. includedProperties ?? []];
            _excludeOptionalAttachments = excludeOptionalAttachments;
        }

        public GoogleChatMessage BuildMessage(Event<LogEventData> evt)
        {
            var data = evt.Data;
            var renderedMessage = string.IsNullOrWhiteSpace(data.RenderedMessage) ? "(no message)" : data.RenderedMessage;

            var section = new Section
            {
                Header = GoogleChatSyntax.Escape(GoogleChatSyntax.Truncate(renderedMessage, MaxHeaderChars))
            };
            section.Widgets.Add(new Widget(GoogleChatSyntax.Hyperlink(EventFormatting.LinkToId(_host, evt.Id), "View this event in Seq")));

            if (!_excludeOptionalAttachments)
                AddOptionalWidgets(section, evt, MaxTextBytes - Utf8(section.Header) - Utf8(section.Widgets[0].TextParagraph.Text));

            if (section.Widgets.Count > 1)
            {
                section.Collapsible = true;
                section.UncollapsibleWidgetsCount = 1;
            }

            var card = new Card
            {
                Header = new Header { Title = Title(data.Level), Subtitle = Subtitle(data.LocalTimestamp) },
                Sections = [section]
            };
            return new GoogleChatMessage(card);
        }

        private void AddOptionalWidgets(Section section, Event<LogEventData> evt, int budget)
        {
            var properties = evt.Data.Properties ?? new Dictionary<string, object>();
            var candidates = new List<Widget>();

            foreach (var key in SpecialProperties)
                if (properties.TryGetValue(key, out var value))
                    candidates.Add(PropertyWidget(key, value));

            if (!string.IsNullOrEmpty(evt.Data.Exception))
                candidates.Add(BlockWidget("Exception", evt.Data.Exception));

            if (properties.TryGetValue(StackTraceProperty, out var st) && st is string stackTrace)
                candidates.Add(BlockWidget(StackTraceProperty, stackTrace));

            foreach (var property in properties)
            {
                if (SpecialProperties.Contains(property.Key) || property.Key == StackTraceProperty) continue;
                if (_includedProperties.Count != 0 && !_includedProperties.Contains(property.Key)) continue;
                candidates.Add(PropertyWidget(property.Key, property.Value));
            }

            var omitted = 0;
            foreach (var widget in candidates)
            {
                var size = Utf8(widget.TextParagraph.Text) + WidgetOverheadBytes;
                if (omitted > 0 || size > budget)
                {
                    omitted++;
                    continue;
                }
                section.Widgets.Add(widget);
                budget -= size;
            }

            if (omitted > 0)
                section.Widgets.Add(new Widget($"<i>{omitted} more item(s) not shown: the message would exceed the Google Chat size limit.</i>"));
        }

        private Widget PropertyWidget(string key, object value)
        {
            var text = GoogleChatSyntax.Truncate(_propertyValueFormatter.ConvertPropertyValueToString(value), MaxWidgetChars);
            return new Widget($"{GoogleChatSyntax.Bold(key + ":")} {GoogleChatSyntax.Multiline(text)}", maxLines: 2);
        }

        private static Widget BlockWidget(string caption, string text) =>
            new($"{GoogleChatSyntax.Bold(caption + ":")}<br>{GoogleChatSyntax.Multiline(GoogleChatSyntax.Truncate(text, MaxWidgetChars))}", maxLines: 3);

        private static string Title(LogEventLevel level)
        {
            var marker = level switch
            {
                LogEventLevel.Fatal or LogEventLevel.Error => "🔴",
                LogEventLevel.Warning => "🟠",
                LogEventLevel.Information => "🟢",
                _ => "⚪"
            };
            return $"{marker} {level}";
        }

        private string Subtitle(DateTimeOffset timestamp)
        {
            var time = timestamp.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(_host.InstanceName) ? time : $"{_host.InstanceName} · {time}";
        }

        private static int Utf8(string s) => Encoding.UTF8.GetByteCount(s);
    }
}
