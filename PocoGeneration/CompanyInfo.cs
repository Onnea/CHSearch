using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Onnea.DTO
{
    public class CompanyInfo : CompanyInfoGenerated
    {
        public override string ToString()
        => $"{nameof( CompanyInfo )}[{CompanyNumber}, {CompanyName}]";
    }
}
