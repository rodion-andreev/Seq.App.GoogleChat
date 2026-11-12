using System;
using System.Net;

namespace Seq.App.GoogleChat.Api
{
    /// <summary>
    /// A message could not be delivered to Google Chat. The message never contains the webhook URL.
    /// </summary>
    public sealed class GoogleChatDeliveryException : Exception
    {
        public GoogleChatDeliveryException(string message, HttpStatusCode? statusCode, Exception innerException)
            : base(message, innerException)
        {
            StatusCode = statusCode;
        }

        /// <summary>The status Google Chat answered with, or null when it was not reached.</summary>
        public HttpStatusCode? StatusCode { get; }
    }
}
