using System;

namespace Seq.App.GoogleChat.Formatting
{
    static class GoogleChatSyntax
    {
        public static string Escape(string s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            return s
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("&", "&amp;");
        }

        public static string Hyperlink(string url, string caption)
        {
            if (url == null) throw new ArgumentNullException(nameof(url));
            if (caption == null) throw new ArgumentNullException(nameof(caption));
            return $"<a href={url}>{caption}</a>";
        }

        public static string Preformatted(string s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            return $"`{s.Replace("\r", "<br>")}`";
        }

        public static string Code(string s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            return $"`{s}`";
        }
    }
}
