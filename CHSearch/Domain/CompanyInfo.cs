using Newtonsoft.Json;
using System;

namespace Onnea.Domain
{
    public class CompanyInfo : DTO.Generated.CompanyInfo.CompanyInfoGenerated
    {
        [JsonProperty( PropertyName = "id" )]
        public string Id => CompanyNumber;

        public int CompanyInfoId { get; set; }

        public string ToShortString()
        => $"{nameof( CompanyInfo )}[{CompanyNumber}, {CompanyName}]";

        public override string ToString() => JsonConvert.SerializeObject( this );

        public Lazy<FilingHistory>
            FilingHistory = new Lazy<FilingHistory>(
                () =>
                {
                    // TODO: fetch as required and return. Print stuff if fetching.
                    return new FilingHistory();
                });
    }
}
