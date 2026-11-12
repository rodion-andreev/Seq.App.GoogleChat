using System;

namespace Seq.App.GoogleChat.Formatting
{
    /// <summary>
    /// The HTML subset Google Chat renders in card text: &lt;b&gt;, &lt;br&gt;, &lt;a href&gt; and a few more.
    /// Everything taken from an event goes through <see cref="Escape"/> or <see cref="Multiline"/>.
    /// </summary>
    static class GoogleChatSyntax
    {
        public static string Escape(string s)
        {
            ArgumentNullException.ThrowIfNull(s);
            return s
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }

        public static string Multiline(string s) =>
            Escape(s).Replace("\r\n", "<br>").Replace("\n", "<br>").Replace("\r", "<br>");

        public static string Bold(string s) => $"<b>{Escape(s)}</b>";

        public static string Hyperlink(string url, string caption)
        {
            ArgumentNullException.ThrowIfNull(url);
            ArgumentNullException.ThrowIfNull(caption);
            return $"<a href=\"{Escape(url).Replace("\"", "&quot;")}\">{Escape(caption)}</a>";
        }

        public static string Truncate(string s, int maxLength)
        {
            if (s == null || s.Length <= maxLength)
                return s;
            return s[..maxLength] + "…";
        }
    }
}
