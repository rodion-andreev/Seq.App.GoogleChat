using System;
using Seq.Apps;

namespace Seq.App.GoogleChat.Formatting
{
    static class EventFormatting
    {
        public static string LinkToId(Host host, string eventId)
        {
            var filter = Uri.EscapeDataString($"@Id == \"{eventId}\"");
            return $"{host.BaseUri.TrimEnd('/')}/#/events?filter={filter}&show=expanded";
        }
    }
}
