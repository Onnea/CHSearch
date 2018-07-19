using LiteDB;
using Newtonsoft.Json;
using Onnea.DbInterfaces;
using Onnea.Domain;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Onnea
{
    public class Commands
    {
        static T TFromJson<T>(string json)
            => JsonConvert.DeserializeObject<T>(
                json, new JsonConverter[] { FlakyJsonDateTimeConverter.INSTANCE, 
                                            FlakyJsonStringListConverter.INSTANCE });

        static CompanyInfo UpsertCompany( string companyInfoJson,
                                          LiteCollection<CompanyInfo> companies)
        {
            var companyInfo = TFromJson<CompanyInfo>(companyInfoJson);
            var cosmosDbResult = _cosmosDb.UpsertCompanyInfo( companyInfo ).Result;
            companyInfo.CompanyInfoId = int.Parse(companyInfo.CompanyNumber);
            // Insert new customer document (Id will be auto-incremented)
            companies.Upsert(companyInfo);
            return companyInfo;
        }

        static FilingHistory UpsertFilingHistory(
                                    CompanyInfo companyInfo,
                                    string filingHistoryJson,
                                    DateTime asOf,
                                    LiteCollection<FilingHistory> filingHistories)
        {
            var filingHistory = TFromJson<FilingHistory>(filingHistoryJson);
            filingHistory.FilingHistoryId = int.Parse(companyInfo.CompanyNumber);
            filingHistory.AsOf = asOf;
            filingHistories.Upsert(filingHistory);
            return filingHistory;
        }

        public static void BackUpExistingDatabase(string dbFilePath)
        {
            if (File.Exists(dbFilePath))
            {
                var backupOutputPath = Path.Combine(Path.GetDirectoryName(dbFilePath), $"main.db.{DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss")}.zip");
                Console.WriteLine($"Backing up database {dbFilePath} to {backupOutputPath}");
                using (ZipArchive zip = ZipFile.Open(backupOutputPath, ZipArchiveMode.Create))
                {
                    zip.CreateEntryFromFile(dbFilePath, "main.db");
                }
            }
        }

        private static LiteDB.BsonMapper          _bsonMapper = new BsonMapper();
        private static readonly CosmosDbInterface _cosmosDb   = new DbInterfaces.CosmosDbInterface();

        static Commands()
        {
            _bsonMapper.RegisterType<IList<string>>(
                sl => new BsonArray(sl.Select(item => new BsonValue(item))),
                value => value.IsString
                  ? new string[] { value }
                  : value.AsArray.Select(item => item.AsString).ToArray());

            _cosmosDb.Init();
        }

        public static LiteDatabase GetDatabase( string dbFilePath = null )
            => new LiteDatabase( dbFilePath ?? Definitions.DbFilePath,
                                 _bsonMapper );

        public struct FetchResult
        {
            public int CompanyNumber;
            public bool WasFetchedFromWeb;
        }

        public class FetchedRange
        {
            [JsonProperty( "id" )]
            public int FetchedRangeId { get; set; }

            [JsonProperty( "start" )]
            public int Start { get; set; }

            [JsonProperty( "end" )]
            public int End { get; set; }

            public override string ToString()
            => $"{nameof( FetchedRangeId )}={FetchedRangeId}, {nameof( Start )}={Start}, {nameof( End )}={End}";
        }

        /// <returns>A sequence of company numbers together with whether they were actually fetched from the web.</returns>
        public static IEnumerable<FetchResult> FetchCompanyInfos( LiteDatabase db, int from, int count )
        {
            Console.WriteLine( $"Fetching company info from {from} to {from + count - 1}" );

            Console.WriteLine( $"Getting collection \"companies\"" );
            var companies     = db.GetCollection<CompanyInfo>( "companies" );
            Console.WriteLine( $"Ensuring index on {nameof( CompanyInfo.DoesNotExist )}" );
            companies.EnsureIndex( nameof( CompanyInfo.DoesNotExist ), unique: false );

            var companyNumberList = Enumerable.Range( from, count ).ToList();
            var companyNumbersToFetch = new HashSet<int>( companyNumberList );//06052617, 600 * 12 * 24 * 7);//08264572, 1000 );

            var publishingQueue = new BlockingCollection<FetchCompanyJson.Result>();

            // Populate the list of known company id ranges that do not need to be fetched.
            Console.WriteLine( $"Getting already fetched ranges" );
            var fetchedRanges      = db.GetCollection<FetchedRange>( "fetchedRanges" );
            var fetchedRangesList  = fetchedRanges.FindAll().ToList();
            var fetchedRangesMaxId = fetchedRangesList.Any() ? fetchedRangesList.Max( fr => fr.FetchedRangeId ) : 0;

            // If we have not yet ever recorded any fetched ranges, use the very slow method of 
            // populate the list of known fetched ranges from the existing database.
            if ( !fetchedRangesList.Any() )
            {
                Console.WriteLine( $"No fetched ranges found, creating fetched ranges" );

                var allExistingCompanyNumbers = companies.FindAll().Select( c => c.CompanyInfoId );

                if ( allExistingCompanyNumbers.Any() )
                {
                    var first = allExistingCompanyNumbers.Min();
                    var last = allExistingCompanyNumbers.Max();
                    var currentRangeStart = -1;
                    var prevCompanyNumber = -1;
                    var completedRangesCount = 0;

                    for ( var currCompanyNumber = first; currCompanyNumber < last; ++currCompanyNumber )
                    {
                        if ( currentRangeStart == -1 )
                        {
                            currentRangeStart = currCompanyNumber;
                        }

                        if ( prevCompanyNumber == -1 )
                        {
                            prevCompanyNumber = currCompanyNumber;
                            continue;
                        }

                        if ( currCompanyNumber - prevCompanyNumber > 1 )
                        {
                            var completedRange = new FetchedRange()
                            {
                                FetchedRangeId = completedRangesCount++,
                                Start = currentRangeStart,
                                End = prevCompanyNumber
                            };
                            fetchedRangesList.Add( completedRange );
                            currentRangeStart = currCompanyNumber;
                        }
                        prevCompanyNumber = currCompanyNumber;
                    }

                    if ( currentRangeStart != -1 )
                    {
                        var completedRange = new FetchedRange()
                        {
                            FetchedRangeId = completedRangesCount++,
                            Start = currentRangeStart,
                            End = last
                        };

                        fetchedRangesList.Add( completedRange );
                    }

                    fetchedRangesList.ForEach( completedRange => fetchedRanges.Upsert( completedRange ) );
                }
            }

            Task fetcher = Task.Run( async () =>
            {
                try
                {
                    await FetchCompanyJson.Run(
                        Definitions.ApiKey,
                        companyNumbersToFetch,
                        companyNumber =>
                            fetchedRangesList.Any(
                                r => r.Start <= companyNumber && companyNumber <= r.End ),
                        publishingQueue );
                }
                catch ( Exception e )
                {
                    Console.Error.WriteLine( $"{e.Message}:\n{e.StackTrace}" );
                }
            } );

            int resultCount = 0;
            foreach ( FetchCompanyJson.Result result in publishingQueue.GetConsumingEnumerable() )
            {
                if ( result.Success )
                {
                    if ( result.Json != null )
                    {
                        // This company JSON was fetched from the web.
                        CompanyInfo ci = UpsertCompany( result.Json, companies );
                        //Console.WriteLine( JsonConvert.SerializeObject(ci, Newtonsoft.Json.Formatting.Indented ) );
                        Console.WriteLine( $"Fetched {companyNumberList.First()}-{ci.CompanyNumber}, {ci.CompanyStatus.PadLeft(9)}, {ci.CompanyName}, {ci.RegisteredOfficeAddress.Locality}, {ci.RegisteredOfficeAddress.PostalCode}" );
                    }
                    //Console.WriteLine($"\n-------Fetching {result.CompanyNumber} OK   : {( result.Json != null ? "JSON upserted" : result.Message )}-------" );
                }
                else
                {
                    UpsertCompany( JsonConvert.SerializeObject(
                        new CompanyInfo()
                        {
                            CompanyNumber = $"{result.CompanyNumber}",
                            DoesNotExist = true
                        } ), companies );
                    //Console.WriteLine( $"-------Fetching {result.CompanyNumber} ERROR: {result.Message?.Substring( 0, Math.Min( result.Message.Length, 60 ) )}-------" );
                }
                
                yield return ( new FetchResult
                {
                    CompanyNumber = result.CompanyNumber,
                    WasFetchedFromWeb = result.Json != null || !result.Success
                } );
                
                if ( !fetchedRangesList.Any( fr => fr.Start <= companyNumberList.First() &&
                                                   result.CompanyNumber <= fr.End ) )
                {
                    var nfr = new FetchedRange()
                    {
                        FetchedRangeId = fetchedRangesMaxId + 1,
                        Start          = companyNumberList.First(),
                        End            = result.CompanyNumber
                    };
                    
                    fetchedRanges.Upsert( nfr );
                }
            }

            Console.WriteLine( "\nWaiting for the fetcher to finish..." );
            fetcher.Wait();

            Console.WriteLine( $"Done fetching company info from {from} to {from + count - 1}" );
        }

        /// <returns>true if the field exists and was indexed</returns>
        public static bool Index( LiteDatabase db, string fieldName, bool indexIsUnique )
        {
            if ( fieldName != null &&
                 typeof( CompanyInfo ).GetProperties().Any( pi => pi.Name.Equals( fieldName ) ) )
            {
                LiteCollection<CompanyInfo> companies = db.GetCollection<CompanyInfo>( "companies" );
                companies.EnsureIndex( fieldName, indexIsUnique );
                //Console.WriteLine( $"Done indexing {fieldName} ({( indexIsUnique ? "" : "not " )}unique)" );
                return true;
            }
            return false;
        }

        public static void Delete( LiteDatabase db, int companyInfoId )
        {
            LiteCollection<CompanyInfo> companies = db.GetCollection<CompanyInfo>( "companies" );
            companies.Delete( c => c.CompanyInfoId == companyInfoId );
        }

        public static IEnumerable<CompanyInfo> GetCompanies(
            LiteDatabase db, Func<CompanyInfo, bool> predicate )
        {
            LiteCollection<CompanyInfo> companies = db.GetCollection<CompanyInfo>( "companies" );
            return companies.FindAll().Where( c => predicate( c ) );
        }

        public static IEnumerable<CompanyInfo> GetCompaniesWhere(
            LiteDatabase db, string field, Func<BsonValue, bool> predicate )
        {
            var companies = db.GetCollection<CompanyInfo>( "companies" );
            return companies.Find( Query.Where( field, predicate ) );
        }

        private static HttpClient CreateHttpClientForFilingHistoryFetching()
        {
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Authorization
                       = new AuthenticationHeaderValue( Definitions.ApiKey.Trim() );
            return client;
        }

        public static IEnumerable<FilingHistory> GetFilingHistories( LiteDatabase db,
                                                                     IEnumerable<CompanyInfo> companyInfos,
                                                                     DateTime asOf )
        {
            var ciList = companyInfos.OrderBy( ci => ci.CompanyInfoId ).ToList();
            Console.WriteLine( $@"Fetching filing histories from {
                ciList.First().CompanyNumber} to {ciList.Last().CompanyNumber}, as of {asOf:yyyyMMdd}" );

            int resultCount = 0;
            HttpClient httpClient = CreateHttpClientForFilingHistoryFetching();

            foreach ( var companyInfo in ciList )
            {
                yield return GetFilingHistory( db, companyInfo, asOf, httpClient );
                Console.Write( $".{( ++resultCount % 60 == 0 ? ( $"fetched {resultCount}\n" ) : "" )}" );
            }

            Console.WriteLine( $@"\nDone fetching filing histories from {
                ciList.First().CompanyNumber} to {ciList.Last().CompanyNumber}, as of {asOf:yyyyMMdd}" );
        }

        public static FilingHistory GetFilingHistory( LiteDatabase db,
                                                      CompanyInfo companyInfo,
                                                      DateTime asOf,
                                                      HttpClient externalHttpClient = null )
        {
            var filingHistories = db.GetCollection<FilingHistory>( "filingHistories" );

            var existingFH = filingHistories.FindOne(
                Query.EQ( "_id", //aka "FilingHistoryId" 
                          new BsonValue( companyInfo.CompanyInfoId ) ) );
            if ( existingFH != null &&
                 existingFH.AsOf.Date >= asOf.Date )
            {
                return existingFH;
            }

            var httpClient = externalHttpClient ?? CreateHttpClientForFilingHistoryFetching();
            var apiurl = $"https://api.companieshouse.gov.uk/company/{companyInfo.CompanyNumber}/filing-history";
            HttpResponseMessage response = httpClient.GetAsync( apiurl ).Result;
            if ( response.IsSuccessStatusCode )
            {
                string json = response.Content.ReadAsStringAsync().Result;
                try
                {
                    FilingHistory fh = UpsertFilingHistory( companyInfo, json,
                        asOf: DateTime.Now.Date, filingHistories: filingHistories );
                    return fh;
                }
                catch ( Exception e )
                {
                    var tempFilename = Path.GetTempFileName();
                    File.WriteAllText( tempFilename, json );
                    Console.Error.WriteLine( $@"Failed to upsert filing history for {
                        companyInfo.CompanyNumber}/{asOf:yyyyMMdd}. JSON dumped to {
                        tempFilename}. Exception was:\n" + e.Message );
                }
            }

            FetchCompanyJson.SnoozeIfRequestQuotaRunningLow( response );

            return null;
        }

        public static IEnumerable<FileHistoryDocument> GetDocuments( 
                                                        FilingHistory filingHistory )
        {
            Console.WriteLine( $@"Fetching documents for filing history {
                                            filingHistory.FilingHistoryId}" );

            int resultCount = 0;
            foreach ( var item in filingHistory.Items )
            {
                yield return GetDocument( item );
                Console.Write( $".{( ++resultCount % 60 == 0 ? ( $"fetched {resultCount}\n" ) : "" )}" );
            }

            Console.WriteLine( $@"\nDone fetching documents for filing history {
                                            filingHistory.FilingHistoryId}" );
        }

        public class FileHistoryDocument
        {
            public FileHistoryDocument(string text, IReadOnlyList<string> images)
            {
                TextFile   = text;
                ImageFiles = images;
            }

            public string                TextFile      { get; }
            public IReadOnlyList<string> ImageFiles    { get; }
        }

        public static FileHistoryDocument GetDocument( 
            DTO.Generated.FilingHistoryList.Item filingHistoryListItem,
            int desiredXDpi = 200, int desiredYDpi = 200)
        {
            if (filingHistoryListItem?.Links?.DocumentMetadata == null)
            {
                return null;
            }

            var docName = filingHistoryListItem.Links.DocumentMetadata.Split('/').Last();

            // For some reason the URL in DocumentMetadata is slightly wrong.
            var correctedUrl = $@"{filingHistoryListItem.Links.DocumentMetadata.Replace( 
                                                    "frontend-doc-api", "document-api" ) }";
            
            // The filepath will reflect the URL
            var filepath = correctedUrl;
            Path.GetInvalidFileNameChars().Concat(Path.GetInvalidPathChars()).ToList()
                .ForEach( c => filepath = filepath.Replace( c.ToString(), "_" ) );

            var textFilename = Path.Combine( Definitions.DocumentsTextDirPath, $"{filepath}.txt" );

            if (File.Exists(textFilename))
            {
                return new FileHistoryDocument( textFilename,
                    Directory.GetFiles( Definitions.DocumentsImagesDirPath, $"{filepath}.p*.tif" ) );
            }
            else
            {
                Directory.CreateDirectory( Definitions.DocumentsMetadataDirPath );
                Directory.CreateDirectory( Definitions.DocumentsContentDirPath  );

                var metadataFilePath = Path.Combine(Definitions.DocumentsMetadataDirPath, $"{filepath}.metadata.txt");
                var metadata = File.Exists( metadataFilePath ) ? File.ReadAllText ( metadataFilePath ) : null;
            
                var contentFilePath = Path.Combine(Definitions.DocumentsContentDirPath,  $"{filepath}.pdf");
                var content = File.Exists( contentFilePath  ) ? File.ReadAllBytes( contentFilePath  ) : null; 

                var metadataWasCached = metadata != null;
                var contentWasCached  = content  != null;

                HttpClient httpClient = null;

                if ( !metadataWasCached || !contentWasCached )
                {
                    // TODO: use doc type from metadata instead of assuming it's always pdf.
                    httpClient = new HttpClient();
                    //client.BaseAddress = new Uri("https://document-api.companieshouse.gov.uk/");
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                        "Basic", System.Convert.ToBase64String(
                            System.Text.Encoding.UTF8.GetBytes(Definitions.ApiKey)));
                    httpClient.DefaultRequestHeaders.Add( "Accept", "application/pdf" );
                }

                Console.WriteLine($@"Fetching document from {correctedUrl}");
                metadata = metadata ?? httpClient.GetStringAsync   ( $"{correctedUrl}"         ).Result;
                content  = content  ?? httpClient.GetByteArrayAsync( $"{correctedUrl}/content" ).Result;

                if ( !metadataWasCached ) File.WriteAllText ( metadataFilePath, metadata );
                if ( !contentWasCached  ) File.WriteAllBytes( contentFilePath,  content  );
                
                Console.Write( $@"Converting pages to images.." );
                var pagesAsBytes = new List<byte[]>();
                foreach ( var pageImage in PdfToImageConverter.CovertPdfToImage( content, desiredXDpi, desiredYDpi ) )
                {
                    Console.Write( "." );
                    pagesAsBytes.Add( pageImage );
                }
                Console.WriteLine( $@"done" );

                Console.Write( $@"OCRing page images.." );
                StringBuilder sb = new StringBuilder();
                int counter = 0;
                List<string> imageFilenames = new List<string>();
                foreach ( var pageImageBytes in pagesAsBytes )
                {
                    Console.Write( $"." );
                    var textAndConfidence = ImageOCR.OCR( pageImageBytes );
                    var text       = textAndConfidence.Item1;
                    var confidence = textAndConfidence.Item2;

                    var pageImageFilename =
                        Path.Combine( Definitions.DocumentsImagesDirPath, $"{filepath}.p{(++counter)}.{confidence}.tif" );
                    imageFilenames.Add( pageImageFilename );
                    File.WriteAllBytes( pageImageFilename, pageImageBytes );

                    sb.Append( String.Join( "\n", text.Split( new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries )
                                                      .Where( s => s.Trim() != "" ) ) );
                }
                File.WriteAllText( textFilename, sb.ToString() );
            
                Console.WriteLine( "done" );

                return new FileHistoryDocument( textFilename, imageFilenames );
            }
        }
    }
}
