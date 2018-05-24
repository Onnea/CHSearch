using System;

namespace Onnea.Domain
{
    public class CompanyInfo : DTO.Generated.CompanyInfo.CompanyInfoGenerated
    {
        public int CompanyInfoId { get; set; }

        public override string ToString()
        => $"{nameof( CompanyInfo )}[{CompanyNumber}, {CompanyName}]";

        public Lazy<FilingHistory>
            FilingHistory = new Lazy<FilingHistory>(
                () =>
                {
                    // TODO: fetch as required and return. Print stuff if fetching.
                    return new FilingHistory();
                });
    }
}
