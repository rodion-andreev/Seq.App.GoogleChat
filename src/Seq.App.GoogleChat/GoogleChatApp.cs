using Seq.App.GoogleChat.Api;
using Seq.App.GoogleChat.Formatting;
using Seq.App.GoogleChat.Messages;
using Seq.App.GoogleChat.Suppression;
using Seq.Apps;
using Seq.Apps.LogEvents;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Seq.App.GoogleChat
{
    [SeqApp("Google Chat Notifier", Description = "Sends events to a Google chat webhook.")]
    public class GoogleChatApp : SeqApp, ISubscribeToAsync<LogEventData>
    {
        private DefaultMessageBuilder _defaultMessageBuilder;

        [SeqAppSetting(
            DisplayName = "Webhook URL",
            HelpText = "Add the Incoming WebHooks app to your GoogleChat to get this URL.")]
        public string WebhookUrl { get; set; }

        [SeqAppSetting(
            DisplayName = "Suppression time (minutes)",
            IsOptional = true,
            HelpText = "Once an event type has been sent to GoogleChat, the time to wait before sending again. The default is zero.")]
        public int SuppressionMinutes { get; set; } = 0;

        [SeqAppSetting(
            DisplayName = "Exclude optional attachments",
            IsOptional = true,
            HelpText = "Should event property information and other optional attachments be excluded from the message? The default is to attach all properties.")]
        public bool ExcludePropertyInformation { get; set; }

        [SeqAppSetting(
            DisplayName = "Proxy Server",
            HelpText = "Proxy server to be used when making HTTPS requests to the GoogleChat API. Uses default credentials.",
            IsOptional = true)]
        public string ProxyServer { get; set; }

        [SeqAppSetting(
            DisplayName = "Maximum property length",
            IsOptional = true,
            HelpText = "If a property when converted to a string is longer than this number it will be truncated.")]
        public int? MaxPropertyLength { get; set; } = null;

        [SeqAppSetting(
            DisplayName = "Included properties",
            IsOptional = true,
            HelpText = "Comma separated list of properties to include as attachments. The default is to include all properties.")]
        public string IncludedProperties { get; set; }

        private EventTypeSuppressions _suppressions;
        private IGoogleChatApi _GoogleChatApi;

        // Used reflectively by the app host.
        // ReSharper disable once UnusedMember.Global
        public GoogleChatApp()
        {
        }

        internal GoogleChatApp(IGoogleChatApi GoogleChatApi)
        {
            _GoogleChatApi = GoogleChatApi;
        }

        protected override void OnAttached()
        {
            if (_GoogleChatApi == null)
            {
                _GoogleChatApi = new GoogleChatApi(ProxyServer);
            }

            var propertyValueFormatter = new PropertyValueFormatter(MaxPropertyLength);

            var includedProperties = string.IsNullOrWhiteSpace(IncludedProperties) ? Array.Empty<string>() : IncludedProperties.Split(',').Select(x => x.Trim());

            _defaultMessageBuilder = new DefaultMessageBuilder(Host, App, propertyValueFormatter, ExcludePropertyInformation, includedProperties);
        }

        public async Task OnAsync(Event<LogEventData> evt)
        {
            _suppressions = _suppressions ?? new EventTypeSuppressions(SuppressionMinutes);
            if (_suppressions.ShouldSuppressAt(evt.EventType, DateTime.UtcNow))
                return;

            var message = _defaultMessageBuilder.BuildMessage(evt);

            await _GoogleChatApi.SendMessageAsync(WebhookUrl, message);
        }
    }
}
