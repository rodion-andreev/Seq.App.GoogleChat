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
        internal const int MaxResultRows = 20;
        internal const int MaxExpandedExtras = 3;

        private const string StackTraceProperty = "StackTrace";
        private const string TimeFormat = "yyyy-MM-dd HH:mm:ss zzz";
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
            return AlertNotification.TryParse(evt.Data.Properties, out var alert)
                ? BuildAlertMessage(evt, alert)
                : BuildEventMessage(evt);
        }

        private GoogleChatMessage BuildEventMessage(Event<LogEventData> evt)
        {
            var data = evt.Data;
            var section = NewSection(data.RenderedMessage);
            section.Widgets.Add(new Widget(GoogleChatSyntax.Hyperlink(EventFormatting.LinkToId(_host, evt.Id), "View this event in Seq")));

            if (!_excludeOptionalAttachments)
                AddWithinBudget(section, EventAttachments(evt));

            return Message(Title(data.Level, data.Level.ToString()), data.LocalTimestamp, section, visibleWidgets: 1);
        }

        private GoogleChatMessage BuildAlertMessage(Event<LogEventData> evt, AlertNotification alert)
        {
            var data = evt.Data;
            var section = NewSection(data.RenderedMessage);

            var links = new List<string>();
            if (alert.ResultsUrl != null) links.Add(GoogleChatSyntax.Hyperlink(alert.ResultsUrl, "View the results"));
            if (alert.AlertUrl != null) links.Add(GoogleChatSyntax.Hyperlink(alert.AlertUrl, "Open the alert"));
            if (links.Count == 0) links.Add(GoogleChatSyntax.Hyperlink(_host.BaseUri, "Open Seq"));
            section.Widgets.Add(new Widget(string.Join(" · ", links)));

            var core = new List<Widget>();
            if (alert.RangeStart.HasValue && alert.RangeEnd.HasValue)
                core.Add(new Widget($"{GoogleChatSyntax.Bold("Period:")} {Local(alert.RangeStart.Value, data)} – {Local(alert.RangeEnd.Value, data)}"));
            if (ResultsText(alert, data) is { } results)
                core.Add(new Widget($"{GoogleChatSyntax.Bold("Results:")}<br>{results}", maxLines: 6));
            if (alert.Failures != null)
                core.Add(new Widget($"{GoogleChatSyntax.Bold("Failures:")} {GoogleChatSyntax.Multiline(Format(alert.Failures))}", maxLines: 3));

            var attached = _excludeOptionalAttachments
                ? []
                : (data.Properties ?? new Dictionary<string, object>())
                    .Where(p => !AlertNotification.IsOwnProperty(p.Key) && IsIncluded(p.Key))
                    .Select(p => PropertyWidget(p.Key, p.Value));

            AddWithinBudget(section, core.Concat(attached));

            var title = Title(data.Level, alert.Title ?? data.Level.ToString());
            return Message(title, data.LocalTimestamp, section, visibleWidgets: 1 + core.Count);
        }

        private IEnumerable<Widget> EventAttachments(Event<LogEventData> evt)
        {
            var properties = evt.Data.Properties ?? new Dictionary<string, object>();

            foreach (var key in SpecialProperties)
                if (IsIncluded(key) && properties.TryGetValue(key, out var value))
                    yield return PropertyWidget(key, value);

            if (!string.IsNullOrEmpty(evt.Data.Exception))
                yield return BlockWidget("Exception", evt.Data.Exception);

            if (properties.TryGetValue(StackTraceProperty, out var st) && st is string stackTrace)
                yield return BlockWidget(StackTraceProperty, stackTrace);

            foreach (var property in properties)
            {
                if (SpecialProperties.Contains(property.Key) || property.Key == StackTraceProperty) continue;
                if (!IsIncluded(property.Key)) continue;
                yield return PropertyWidget(property.Key, property.Value);
            }
        }

        private string ResultsText(AlertNotification alert, LogEventData data)
        {
            if (alert.Results.Count < 2)
                return null;

            var columns = alert.Results[0].Select(c => c?.ToString() ?? "").ToList();
            var rows = alert.Results.Skip(1).ToList();
            var lines = rows.Take(MaxResultRows).Select(row => string.Join(", ", row.Select((cell, i) =>
            {
                var column = i < columns.Count ? columns[i] : $"#{i}";
                var value = column == "time" && AlertNotification.Time(cell) is { } t ? Local(t, data) : Format(cell);
                return $"{column}: {value}";
            }))).ToList();

            if (rows.Count > MaxResultRows)
                lines.Add($"… {rows.Count - MaxResultRows} more row(s)");

            return GoogleChatSyntax.Multiline(GoogleChatSyntax.Truncate(string.Join("\n", lines), MaxWidgetChars));
        }

        private static Section NewSection(string renderedMessage)
        {
            var text = string.IsNullOrWhiteSpace(renderedMessage) ? "(no message)" : renderedMessage;
            return new Section { Header = GoogleChatSyntax.Escape(GoogleChatSyntax.Truncate(text, MaxHeaderChars)) };
        }

        private void AddWithinBudget(Section section, IEnumerable<Widget> candidates)
        {
            var budget = MaxTextBytes - Utf8(section.Header) - section.Widgets.Sum(w => Utf8(w.TextParagraph.Text) + WidgetOverheadBytes);
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

        private GoogleChatMessage Message(string title, DateTimeOffset timestamp, Section section, int visibleWidgets)
        {
            // A few extra lines read better in full than behind "Show more"; collapse only a longer list.
            if (section.Widgets.Count - visibleWidgets > MaxExpandedExtras)
            {
                section.Collapsible = true;
                section.UncollapsibleWidgetsCount = visibleWidgets;
            }

            var card = new Card
            {
                Header = new Header { Title = GoogleChatSyntax.Truncate(title, 200), Subtitle = Subtitle(timestamp) },
                Sections = [section]
            };
            return new GoogleChatMessage(card);
        }

        private bool IsIncluded(string key) => _includedProperties.Count == 0 || _includedProperties.Contains(key);

        private string Format(object value) => _propertyValueFormatter.ConvertPropertyValueToString(value);

        private Widget PropertyWidget(string key, object value)
        {
            var text = GoogleChatSyntax.Truncate(Format(value), MaxWidgetChars);
            return new Widget($"{GoogleChatSyntax.Bold(key + ":")} {GoogleChatSyntax.Multiline(text)}", maxLines: 2);
        }

        private static Widget BlockWidget(string caption, string text) =>
            new($"{GoogleChatSyntax.Bold(caption + ":")}<br>{GoogleChatSyntax.Multiline(GoogleChatSyntax.Truncate(text, MaxWidgetChars))}", maxLines: 3);

        private static string Title(LogEventLevel level, string text)
        {
            var marker = level switch
            {
                LogEventLevel.Fatal or LogEventLevel.Error => "🔴",
                LogEventLevel.Warning => "🟠",
                LogEventLevel.Information => "🟢",
                _ => "⚪"
            };
            return $"{marker} {text}";
        }

        // Alert periods come in UTC; show them in the offset the Seq server reports for the notification.
        private static string Local(DateTimeOffset time, LogEventData data) =>
            time.ToOffset(data.LocalTimestamp.Offset).ToString(TimeFormat, CultureInfo.InvariantCulture);

        private string Subtitle(DateTimeOffset timestamp)
        {
            var time = timestamp.ToString(TimeFormat, CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(_host.InstanceName) ? time : $"{_host.InstanceName} · {time}";
        }

        private static int Utf8(string s) => Encoding.UTF8.GetByteCount(s);
    }
}
