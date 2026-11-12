using System;
using System.Globalization;
using System.Text.Json;
using Seq.App.GoogleChat.Api;

namespace Seq.App.GoogleChat.Formatting
{
    public class PropertyValueFormatter
    {
        private readonly int? _maxPropertyLength;

        public PropertyValueFormatter(int? maxPropertyLength)
        {
            _maxPropertyLength = maxPropertyLength is > 0 ? maxPropertyLength : null;
        }

        /// <summary>
        /// Scalars as text, structures and sequences as JSON, truncated to the configured length.
        /// </summary>
        public string ConvertPropertyValueToString(object propertyValue)
        {
            var result = propertyValue switch
            {
                null => "null",
                string s => s,
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => ToJson(propertyValue)
            };

            return _maxPropertyLength.HasValue ? GoogleChatSyntax.Truncate(result, _maxPropertyLength.Value) : result;
        }

        private static string ToJson(object value)
        {
            try
            {
                return JsonSerializer.Serialize(value, GoogleChatApi.SerializerOptions);
            }
            catch (Exception)
            {
                return value.ToString();
            }
        }
    }
}
