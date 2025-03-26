using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Seq.App.GoogleChat.Api
{
    /// <summary>
    /// Provides methods to interact with the Google Chat API.
    /// </summary>
    public class GoogleChatApi : IGoogleChatApi, IDisposable
    {
        // Singleton instance of JsonSerializerOptions
        private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        private static readonly MediaTypeHeaderValue _mediaTypeHeaderValue = new("application/json");

        private readonly HttpClient _httpClient;
        private bool _disposed;
        private readonly bool _shouldDisposeHttpClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="GoogleChatApi"/> class,
        /// injecting an existing <see cref="HttpClient"/>. This can help with testing
        /// and advanced scenarios.
        /// </summary>
        /// <param name="httpClient">The <see cref="HttpClient"/> to use.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public GoogleChatApi(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _shouldDisposeHttpClient = false; // Caller owns HttpClient
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GoogleChatApi"/> class
        /// using an optional proxy server.
        /// </summary>
        /// <param name="proxyServer">The proxy server address, or null/empty if not needed.</param>
        public GoogleChatApi(string proxyServer)
        {
            _httpClient = CreateHttpClient(proxyServer);
            _shouldDisposeHttpClient = true; // We created it, so we are responsible for disposing
        }

        /// <summary>
        /// Sends a message to the specified Google Chat webhook URL.
        /// </summary>
        /// <param name="webhookUrl">The webhook URL.</param>
        /// <param name="message">The message to send.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <exception cref="ArgumentException">Thrown if <paramref name="webhookUrl"/> is null or empty.</exception>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="message"/> is null.</exception>
        /// <exception cref="HttpRequestException">Thrown when the request fails.</exception>
        public async Task SendMessageAsync(string webhookUrl, GoogleChatMessage message)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(webhookUrl, nameof(webhookUrl));
            ArgumentNullException.ThrowIfNull(message, nameof(message));

            var content = JsonContent.Create(message, _mediaTypeHeaderValue, _jsonSerializerOptions);
            var response = await _httpClient.PostAsync(webhookUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException(
                    $"Failed to send message to Google Chat. " +
                    $"StatusCode: {response.StatusCode}, ReasonPhrase: {response.ReasonPhrase}, " +
                    $"ResponseBody: {responseBody}. " +
                    $"ContentSent: {JsonSerializer.Serialize(message, _jsonSerializerOptions)}"
                );
            }
        }

        private static HttpClient CreateHttpClient(string proxyServer)
        {
            if (!string.IsNullOrWhiteSpace(proxyServer))
            {
                var proxy = new WebProxy(proxyServer, false)
                {
                    UseDefaultCredentials = true
                };
                var httpClientHandler = new HttpClientHandler
                {
                    Proxy = proxy,
                    PreAuthenticate = true,
                    UseDefaultCredentials = true
                };
                return new HttpClient(httpClientHandler);
            }
            else
            {
                return new HttpClient();
            }
        }

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="GoogleChatApi"/> and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing && _shouldDisposeHttpClient)
                {
                    _httpClient?.Dispose();
                }
                _disposed = true;
            }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
