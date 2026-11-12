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
    [SeqApp("Google Chat Notifier", Description = "Sends events and alert notifications to a Google Chat space through an incoming webhook.")]
    public sealed class GoogleChatApp : SeqApp, ISubscribeToAsync<LogEventData>, IDisposable
    {
        private DefaultMessageBuilder _messageBuilder;
        private EventTypeSuppressions _suppressions;
        private IGoogleChatApi _googleChatApi;
        private bool _ownsGoogleChatApi;

        [SeqAppSetting(
            DisplayName = "Webhook URL",
            InputType = SettingInputType.Password,
            HelpText = "The incoming webhook URL of the Google Chat space (space menu > Apps & integrations > Webhooks). It contains the space's key and token: treat it as a secret.")]
        public string WebhookUrl { get; set; }

        [SeqAppSetting(
            DisplayName = "Suppression time (minutes)",
            IsOptional = true,
            HelpText = "Once an event type has been sent to Google Chat, the time to wait before sending it again. The default is zero.")]
        public int SuppressionMinutes { get; set; }

        [SeqAppSetting(
            DisplayName = "Exclude optional attachments",
            IsOptional = true,
            HelpText = "Send only the level, message and a link to the event, without the exception and properties. The default is to attach them.")]
        public bool ExcludePropertyInformation { get; set; }

        [SeqAppSetting(
            DisplayName = "Proxy Server",
            HelpText = "Proxy server for the HTTPS requests to Google Chat, e.g. http://proxy:3128. Uses default credentials.",
            IsOptional = true)]
        public string ProxyServer { get; set; }

        [SeqAppSetting(
            DisplayName = "Maximum property length",
            IsOptional = true,
            HelpText = "A property longer than this, once converted to text, is truncated. Values are always capped at 4,000 characters and the whole message at the Google Chat size limit.")]
        public int? MaxPropertyLength { get; set; }

        [SeqAppSetting(
            DisplayName = "Included properties",
            IsOptional = true,
            HelpText = "Comma-separated list of properties to attach. The default is to attach all properties.")]
        public string IncludedProperties { get; set; }

        // Used reflectively by the app host.
        // ReSharper disable once UnusedMember.Global
        public GoogleChatApp()
        {
        }

        internal GoogleChatApp(IGoogleChatApi googleChatApi)
        {
            _googleChatApi = googleChatApi;
        }

        protected override void OnAttached()
        {
            if (!Uri.TryCreate(WebhookUrl?.Trim(), UriKind.Absolute, out var webhook) || webhook.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("The Webhook URL setting must be an absolute https:// URL.");
            WebhookUrl = webhook.AbsoluteUri;

            if (_googleChatApi == null)
            {
                _googleChatApi = new GoogleChatApi(ProxyServer);
                _ownsGoogleChatApi = true;
            }

            var includedProperties = string.IsNullOrWhiteSpace(IncludedProperties)
                ? []
                : IncludedProperties.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            _messageBuilder = new DefaultMessageBuilder(Host, new PropertyValueFormatter(MaxPropertyLength), ExcludePropertyInformation, includedProperties);
            _suppressions = new EventTypeSuppressions(SuppressionMinutes);
        }

        public async Task OnAsync(Event<LogEventData> evt)
        {
            if (_suppressions.ShouldSuppressAt(evt.EventType, DateTime.UtcNow))
                return;

            await _googleChatApi.SendMessageAsync(WebhookUrl, _messageBuilder.BuildMessage(evt));
        }

        public void Dispose()
        {
            if (_ownsGoogleChatApi && _googleChatApi is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
