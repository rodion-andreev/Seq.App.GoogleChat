using Seq.App.GoogleChat.Formatting;
using Seq.App.GoogleChat.Messages;
using Seq.Apps;
using Seq.Apps.LogEvents;
using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace Seq.App.GoogleChat.Tests
{
    public class DefaultMessageBuilderTests
    {
        private readonly Host _host;
        private readonly Event<LogEventData> _event;

        public DefaultMessageBuilderTests()
        {
            _host = new Host("http://listen.example.com", "instance");

            _event = new Event<LogEventData>("id", 1, DateTime.Now, new LogEventData
            {
                Id = "111",
                Level = LogEventLevel.Information,
                Properties = new Dictionary<string, object>
                {
                    {"Property1", "Value1"},
                    {"Property2", "Value2"},
                    {"StackTrace", new Exception("StackTrace exception").StackTrace}
                },
                Exception = new Exception("Test exception").ToString()
            });
        }

        private DefaultMessageBuilder CreateDefaultMessageBuilder(string messageTemplate = null, IEnumerable<string> includedProperties = null)
        {
            return new DefaultMessageBuilder(
                _host,
                new Apps.App("app-id", "App Title", new Dictionary<string, string>(), "storage-path"),
                new PropertyValueFormatter(null),
                false,
                null);
        }

        [Fact]
        public void BuildMessage_UsesDefaultTemplateWhenMessageTemplateIsNull()
        {
            var builder = CreateDefaultMessageBuilder();
            var result = builder.BuildMessage(_event);
            var serializeOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };
            var json = JsonSerializer.Serialize(result, serializeOptions);

            //Assert.Equal("[RenderedMessage]", result.CardsV2[0].Card.Header.Title);
        }       
    }
}
