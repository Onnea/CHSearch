using System.IO;

namespace Onnea
{
    public class Definitions
    {
        public static string BaseDirPath => @"C:\temp\CHSearch";
        public static string DbFilePath  => Path.Combine(BaseDirPath, "db", "main.db" );
        public static string ApiKeyPath  => Path.Combine(BaseDirPath, "etc", "apikey.txt" );
        public static string ApiKey      => File.ReadAllText(ApiKeyPath);

        private static string DocumentsDirPath          => Path.Combine(BaseDirPath,      "documents" );
        public  static string DocumentsContentDirPath   => Path.Combine(DocumentsDirPath, "content"   );
        public  static string DocumentsMetadataDirPath  => Path.Combine(DocumentsDirPath, "metadata"  );
    }
}
