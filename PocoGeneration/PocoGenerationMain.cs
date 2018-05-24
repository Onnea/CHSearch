using System;
using System.IO;
using Xamasoft.JsonClassGenerator;

namespace Onnea
{
    class PocoGenerationMain
    {
        static void Main( string[] args )
        {
            var schemas = new[]
            {
                "CompanyInfo",
                "FilingHistoryList"
            };

            if ( args.Length != 1 )
            {
                throw new ArgumentException( 
                    $"Usage: {nameof( PocoGenerationMain )}.exe TARGET_FOLDER" );
            }

            var targetFolder = args[0];

            foreach ( var schemaName in schemas )
            {
                var schemaText = 
                    File.ReadAllText( $"schemas/{schemaName}.yml" );
                
                var gen = new JsonClassGenerator
                {
                    Namespace = $"{nameof( Onnea )}.DTO.Generated.{schemaName}",
                    TargetFolder = targetFolder,
                    MainClass = $"{schemaName}Generated",
                    UsePascalCase = true,
                    SingleFile = true,
                    Example = schemaText,
                    UseProperties = true
                };

                using ( var sw = new StringWriter() )
                {
                    gen.OutputStream = sw;
                    gen.GenerateClasses();
                    sw.Flush();
                }
            }
        }
    }
}
