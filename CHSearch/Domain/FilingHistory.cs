using Newtonsoft.Json;
using System;

namespace Onnea.Domain
{
    public class FilingHistory : DTO.Generated.FilingHistoryList.FilingHistoryListGenerated
    {
        [JsonProperty( PropertyName = "id" )]
        public int FilingHistoryId { get; set; }

        public DateTime AsOf { get; set; }

        public override string ToString()
         => $"{nameof( FilingHistory )}[{FilingHistoryId}, as of: {AsOf:yyyy-MM-dd}]";

    }
}
