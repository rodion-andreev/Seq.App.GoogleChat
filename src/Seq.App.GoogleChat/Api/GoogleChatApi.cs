using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Seq.App.GoogleChat.Api
{
    public class GoogleChatApi : IGoogleChatApi
    {
        static GoogleChatApi()
        {
            // Enable TLS 1.2 before any connection to the GoogleChat API is made.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        private readonly HttpClient _httpClient;

        public GoogleChatApi(string proxyServer)
        {
            if (!string.IsNullOrWhiteSpace(proxyServer))
            {
                var proxy = new WebProxy(proxyServer, false)
                {
                    UseDefaultCredentials = true
                };
                var httpClientHandler = new HttpClientHandler()
                {
                    Proxy = proxy,
                    PreAuthenticate = true,
                    UseDefaultCredentials = true,
                };
                _httpClient = new HttpClient(handler: httpClientHandler);
            }
            else
            {
                _httpClient = new HttpClient();
            }
        }
        
        public async Task SendMessageAsync(string webhookUrl, GoogleChatMessage message)
        {
            var serializeOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };
            var json = JsonSerializer.Serialize(message, serializeOptions);
            using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
            {
                var resp = await _httpClient.PostAsync(webhookUrl, content);
                if(resp.IsSuccessStatusCode != true)
                {
                    throw new HttpRequestException($"Failed to send message to GC: {resp.ReasonPhrase}. Contents sent {json}");
                }
            }
        }
    }
}
