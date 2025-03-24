using Seq.App.GoogleChat.Api;
using Seq.App.GoogleChat.Formatting;
using Seq.Apps;
using Seq.Apps.LogEvents;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Seq.App.GoogleChat.Messages
{
    public class DefaultMessageBuilder
    {
        public const string DefaultIconUrl = "https://datalust.co/images/nuget/seq-apps.png";

        private readonly Host _host;
        private readonly PropertyValueFormatter _propertyValueFormatter;
        private static readonly IEnumerable<string> SpecialProperties = ["Id", "Host"];

        private readonly bool _excludeOptionalAttachments;
        private readonly HashSet<string> _includedProperties;

        public DefaultMessageBuilder(Host host, Apps.App app, PropertyValueFormatter propertyValueFormatter, bool excludeOptionalAttachments, IEnumerable<string> includedProperties)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _propertyValueFormatter = propertyValueFormatter ?? throw new ArgumentNullException(nameof(propertyValueFormatter));
            _includedProperties = [.. includedProperties ?? throw new ArgumentNullException(nameof(includedProperties))];
            _excludeOptionalAttachments = excludeOptionalAttachments;

        }

        public GoogleChatMessage BuildMessage(Event<LogEventData> evt)
        {
            var card = new Card()
            {

                Header = new Header { Title = $"<b>{evt.Data.Level}</b>", Subtitle = evt.Data.LocalTimestamp.ToString("f") },
                Sections = [
                    new Section {
                        Header = evt.Data.RenderedMessage,
                        Widgets =
                        [
                            new Widget(GenerateLinkAttachment(evt))
                        ]
                    }
                ]
            };

            if (!_excludeOptionalAttachments)
            {
                AddOptionalAttachments(card, evt);
            }

            return new GoogleChatMessage(card);
        }

        protected string GenerateLinkAttachment(Event<LogEventData> evt)
        {
            var viewUrl = EventFormatting.LinkToId(_host, evt.Id);
            return GoogleChatSyntax.Hyperlink(viewUrl, "View this event in Seq");
        }
       
        protected void AddOptionalAttachments(Card message, Event<LogEventData> evt)
        {
            foreach (var key in SpecialProperties)
            {
                if (evt.Data.Properties == null || !evt.Data.Properties.ContainsKey(key)) continue;

                var property = evt.Data.Properties[key];
                message.Sections.First().Widgets.Add(new Widget($"<b>{key}:</b> {property}"));

            }

            if (evt.Data.Exception != null)
            {
                message.Sections.First().Widgets.Add(new Widget($"<b>Exception:</b> {GoogleChatSyntax.Preformatted(evt.Data.Exception)}", 1));
            }

            if (evt.Data.Properties != null && evt.Data.Properties.TryGetValue("StackTrace", out var st) && st is string stackTrace)
            {
                message.Sections.First().Widgets.Add(new Widget($"<b>StackTrace:</b> {GoogleChatSyntax.Preformatted(stackTrace)}", 1));
            }

            if (evt.Data.Properties != null)
            {
                foreach (var property in evt.Data.Properties)
                {
                    if (SpecialProperties.Contains(property.Key)) continue;
                    if (property.Key == "StackTrace") continue;
                    if (_includedProperties.Count != 0 && !_includedProperties.Contains(property.Key)) continue;

                    var value = _propertyValueFormatter.ConvertPropertyValueToString(property.Value);

                    message.Sections.First().Widgets.Add(new Widget($"<b>{property.Key}:</b> {value}"));
                }
            }
        }
    }
}