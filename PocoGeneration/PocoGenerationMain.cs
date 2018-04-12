using System;
using System.IO;
using Xamasoft.JsonClassGenerator;

namespace Onnea
{
    class PocoGenerationMain
    {
        static void Main( string[] args )
        {
            var schema = File.ReadAllText( "schemas/companyProfile.yml" );

            if ( args.Length != 1 ) throw new ArgumentException( $"Usage: {nameof(PocoGenerationMain)}.exe TARGET_FOLDER" );
            var targetFolder = args[0];

			var gen = new JsonClassGenerator
				          {
					          Namespace = $"{nameof(Onnea)}.DTO",
					          TargetFolder = targetFolder,
					          MainClass = "CompanyInfo",
					          UsePascalCase = true,
					          SingleFile = true,
							  Example = schema,
                              UseProperties = true
				          };

			using (var sw = new StringWriter())
			{
				gen.OutputStream = sw;
				gen.GenerateClasses();
				sw.Flush();
			}
        }
    }
}
