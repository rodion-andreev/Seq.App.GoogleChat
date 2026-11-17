using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Seq.App.GoogleChat.Messages
{
    /// <summary>
    /// The notification Seq sends to an app instance when an alert triggers: the event carries <c>Alert</c>
    /// (title, link, query) and <c>Source</c> (period, result rows, link to the results) instead of log data.
    /// </summary>
    sealed class AlertNotification
    {
        private static readonly HashSet<string> OwnProperties =
            ["@seqid", "@i", "NamespacedAlertTitle", "Alert", "Source", "SuppressedUntil", "Failures"];

        public string Title { get; private init; }
        public string AlertUrl { get; private init; }
        public string ResultsUrl { get; private init; }
        public DateTimeOffset? RangeStart { get; private init; }
        public DateTimeOffset? RangeEnd { get; private init; }

        /// <summary>Result rows; the first row holds the column names.</summary>
        public IReadOnlyList<IReadOnlyList<object>> Results { get; private init; } = [];

        public object Failures { get; private init; }

        public static bool IsOwnProperty(string key) => OwnProperties.Contains(key);

        public static bool TryParse(IReadOnlyDictionary<string, object> properties, out AlertNotification notification)
        {
            notification = null;
            if (properties == null
                || !properties.TryGetValue("Alert", out var alertValue) || AsMap(alertValue) is not { } alert
                || !properties.TryGetValue("Source", out var sourceValue) || AsMap(sourceValue) is not { } source)
                return false;

            properties.TryGetValue("Failures", out var failures);
            notification = new AlertNotification
            {
                Title = Text(alert, "Title") ?? (properties.TryGetValue("NamespacedAlertTitle", out var t) ? t?.ToString() : null),
                AlertUrl = Text(alert, "Url"),
                ResultsUrl = Text(source, "ResultsUrl"),
                RangeStart = Time(source.GetValueOrDefault("RangeStart")),
                RangeEnd = Time(source.GetValueOrDefault("RangeEnd")),
                Results = Rows(source.GetValueOrDefault("Results")),
                Failures = IsEmpty(failures) ? null : failures
            };
            return true;
        }

        internal static DateTimeOffset? Time(object value) => value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt),
            string s when DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) => parsed,
            _ => null
        };

        private static IReadOnlyDictionary<string, object> AsMap(object value) => value switch
        {
            IReadOnlyDictionary<string, object> map => map,
            IDictionary<string, object> map => map.ToDictionary(kv => kv.Key, kv => kv.Value),
            IDictionary map => map.Keys.Cast<object>().ToDictionary(k => k.ToString(), k => map[k]),
            _ => null
        };

        private static string Text(IReadOnlyDictionary<string, object> map, string key) =>
            map.TryGetValue(key, out var value) && value != null && value.ToString() is { Length: > 0 } s ? s : null;

        private static IReadOnlyList<IReadOnlyList<object>> Rows(object value)
        {
            if (value is not IEnumerable rows || value is string)
                return [];
            return rows.Cast<object>()
                .Select(row => row is IEnumerable cells && row is not string ? cells.Cast<object>().ToList() : new List<object> { row })
                .ToList<IReadOnlyList<object>>();
        }

        private static bool IsEmpty(object value) =>
            value == null || (value is IEnumerable e && value is not string && !e.Cast<object>().Any());
    }
}
