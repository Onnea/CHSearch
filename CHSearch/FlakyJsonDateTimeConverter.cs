using Newtonsoft.Json;
using System;

namespace Onnea
{
    class FlakyJsonDateTimeConverter  : JsonConverter
    {
        public static FlakyJsonDateTimeConverter INSTANCE = new FlakyJsonDateTimeConverter();

        public override bool CanConvert( Type objectType ) => objectType.Equals( typeof(DateTime) );

        public override object ReadJson( JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer )
        {
            if ( DateTime.TryParse( reader.Value.ToString(), out DateTime dt ) ) 
                return dt;
            return DateTime.MinValue;
        }

        public override void WriteJson( JsonWriter writer, object value, JsonSerializer serializer )
        {
            var dt = value as DateTime?;
            writer.WriteValue( dt.HasValue ? dt.Value : DateTime.MinValue );
        }
    }
}
