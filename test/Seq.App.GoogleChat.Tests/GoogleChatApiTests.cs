using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
using Xunit;
using Seq.App.GoogleChat.Api;
using System.Threading;

namespace Seq.App.GoogleChat.Tests
{
    public sealed class GoogleChatApiTests
    {
        [Fact]
        public async Task SendMessageAsync_ValidInputs_Success()
        {
            // Arrange
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK
                });

            var httpClient = new HttpClient(handlerMock.Object);
            var api = new GoogleChatApi(httpClient);
            var message = new GoogleChatMessage(new Card());

            // Act
            await api.SendMessageAsync("https://example.com/webhook", message);

            // Assert
            handlerMock.Protected().Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Post),
                ItExpr.IsAny<CancellationToken>()
            );
        }

        [Fact]
        public async Task SendMessageAsync_InvalidWebhookUrl_ThrowsArgumentException()
        {
            // Arrange
            var httpClient = new HttpClient();
            var api = new GoogleChatApi(httpClient);
            var message = new GoogleChatMessage(new Card());

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => api.SendMessageAsync("", message));
        }

        [Fact]
        public async Task SendMessageAsync_NullMessage_ThrowsArgumentNullException()
        {
            // Arrange
            var httpClient = new HttpClient();
            var api = new GoogleChatApi(httpClient);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => api.SendMessageAsync("https://example.com/webhook", null));
        }

        [Fact]
        public async Task SendMessageAsync_UnsuccessfulResponse_ThrowsHttpRequestException()
        {
            // Arrange
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.BadRequest,
                    Content = new StringContent("Bad Request")
                });

            var httpClient = new HttpClient(handlerMock.Object);
            var api = new GoogleChatApi(httpClient);
            var message = new GoogleChatMessage(new Card());

            // Act & Assert
            var exception = await Assert.ThrowsAsync<HttpRequestException>(() => api.SendMessageAsync("https://example.com/webhook", message));
            Assert.Contains("Failed to send message to Google Chat", exception.Message);
        }
    }
}