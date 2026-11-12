using System;
using System.Collections.Generic;
using System.Linq;

namespace Seq.App.GoogleChat.Suppression
{
    class EventTypeSuppressions
    {
        private readonly Dictionary<uint, DateTime> _suppressions = new();
        private readonly TimeSpan _window;

        public EventTypeSuppressions(int suppressionMinutes)
        {
            _window = TimeSpan.FromMinutes(Math.Max(0, suppressionMinutes));
        }

        public bool ShouldSuppressAt(uint eventType, DateTime utcNow)
        {
            if (_window == TimeSpan.Zero)
                return false;

            if (_suppressions.TryGetValue(eventType, out var since) && since + _window >= utcNow)
                return true;

            foreach (var expired in _suppressions.Where(kvp => kvp.Value + _window < utcNow).Select(kvp => kvp.Key).ToList())
                _suppressions.Remove(expired);

            _suppressions[eventType] = utcNow;
            return false;
        }
    }
}
