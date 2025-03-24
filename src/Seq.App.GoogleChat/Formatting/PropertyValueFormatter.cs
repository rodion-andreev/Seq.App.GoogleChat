using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Seq.App.GoogleChat.Formatting
{
    public class PropertyValueFormatter
    {
        private readonly int? _maxPropertyLength;

        public PropertyValueFormatter(int? maxPropertyLength)
        {
            _maxPropertyLength = maxPropertyLength;
        }

        public string ConvertPropertyValueToString(object propertyValue)
        {
            if (propertyValue == null)
                return string.Empty;

            string result;
            Type t = propertyValue.GetType();
            bool isDict = t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>);
            if (isDict)
            {
                result = JsonSerializer.Serialize(propertyValue);
            }
            else
            {
                result = propertyValue.ToString();
            }

            if (_maxPropertyLength.HasValue)
            {
                if (result.Length > _maxPropertyLength)
                {
                    result = result.Substring(0, _maxPropertyLength.Value) + "...";
                }
            }

            return result;
        }
    }
}