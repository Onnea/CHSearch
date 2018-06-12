using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Onnea
{
    class FlakyJsonStringListConverter : JsonConverter
    {
        public static FlakyJsonStringListConverter INSTANCE = new FlakyJsonStringListConverter();

        public override bool CanConvert(Type objectType) 
            => objectType.Equals(typeof(IList<string>));

        public override object ReadJson(
            JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            List<string> strings = new List<string>();
            if (reader.TokenType == JsonToken.StartArray)
            {
                while (reader.TokenType != JsonToken.EndArray)
                {
                    var readerVal = reader.Value;
                    var strVal = reader.ReadAsString();
                    if (strVal != null)
                    {
                        strings.Add(strVal);
                    }
                    else
                    {
                        int i = 0;
                    }
                }
                //reader.Read();
            }
            else
            {
                var readerVal = reader.Value;
                //var strVal = reader.ReadAsString();
                if (readerVal != null) //strVal != null)
                {
                    strings.Add(readerVal.ToString());// strVal);
                }
                else
                {
                    int i = 0;
                }
            }

            return strings;
        }

        public override void WriteJson(
            JsonWriter writer, object value, JsonSerializer serializer)
        => serializer.Serialize(writer, value);
    }
}
