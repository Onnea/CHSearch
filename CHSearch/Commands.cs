using LiteDB;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Onnea
{
    public class Commands
    {
        static DTO.CompanyInfo CompanyFromJson( string json ) 
            => JsonConvert.DeserializeObject<DTO.CompanyInfo>( json, new[] { FlakyJsonDateTimeConverter.INSTANCE } );

        static DTO.CompanyInfo UpsertCompany( string companyInfoJson, LiteCollection<DTO.CompanyInfo> companies )
        {            
            var companyInfo = CompanyFromJson( companyInfoJson );
            companyInfo.CompanyInfoId = int.Parse( companyInfo.CompanyNumber );
            // Insert new customer document (Id will be auto-incremented)
            companies.Upsert( companyInfo );
            return companyInfo;
        }

        public static void BackUpExistingDatabase( string dbFilePath )
        {
            if ( File.Exists( dbFilePath ) )
            {
                var backupOutputPath = Path.Combine( Path.GetDirectoryName( dbFilePath ), $"main.db.{DateTime.Now.ToString( "yyyy-MM-dd-HH-mm-ss" )}.zip" );
                Console.WriteLine( $"Backing up database {dbFilePath} to {backupOutputPath}" );
                using ( ZipArchive zip = ZipFile.Open( backupOutputPath, ZipArchiveMode.Create ) )
                {
                    zip.CreateEntryFromFile( dbFilePath, "main.db" );
                }
            }
        }

        public static LiteDatabase GetDatabase( string dbFilePath = null )
            => new LiteDatabase( dbFilePath ?? Definitions.DbFilePath, LiteDB.BsonMapper.Global );

        public struct FetchResult
        {
            public int  CompanyNumber;
            public bool WasFetchedFromWeb;
        }

        public class FetchedRange
        {
            [JsonProperty("id")]
            public int FetchedRangeId { get; set; }

            [JsonProperty( "start" )]
            public int Start { get; set; }

            [JsonProperty( "end" )]
            public int End { get; set; }

            public override string ToString()
            => $"{nameof(FetchedRangeId)}={FetchedRangeId}, {nameof(Start)}={Start}, {nameof(End)}={End}";
        }

        /// <returns>A sequence of company numbers together with whether they were actually fetched from the web.</returns>
        public static IEnumerable<FetchResult> Fetch( LiteDatabase db, int from, int count )
        {
            var companies     = db.GetCollection<DTO.CompanyInfo>( "companies"     );
            var fetchedRanges = db.GetCollection<FetchedRange>   ( "fetchedRanges" );
            
            companies.EnsureIndex( nameof( DTO.CompanyInfo.DoesNotExist ), unique: false );

            var companyNumberList = Enumerable.Range( from, count ).ToList();
            var companyNumbersToFetch = new HashSet<int>( companyNumberList );//06052617, 600 * 12 * 24 * 7);//08264572, 1000 );
            
            var publishingQueue = new BlockingCollection<FetchCompanyJson.Result>();

            // Populate the list of known company id ranges that do not need to be fetched.
            var fetchedRangesList = fetchedRanges.FindAll().ToList();

            // If we have not yet ever recorded any fetched ranges, use the very slow method of 
            // populate the list of known fetched ranges from the existing database.
            if ( !fetchedRangesList.Any() )
            { 
                var allExistingCompanyNumbers = companies.FindAll().Select( c => c.CompanyInfoId );
                var first = allExistingCompanyNumbers.Min();
                var last  = allExistingCompanyNumbers.Max();
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
                            Start          = currentRangeStart,
                            End            = prevCompanyNumber
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
                        Start          = currentRangeStart,
                        End            = last
                    };
                    
                    fetchedRangesList.Add( completedRange );
                }
                
                fetchedRangesList.ForEach( completedRange => fetchedRanges.Upsert( completedRange ) );
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
                                r => r.Start <= companyNumber &&  companyNumber <= r.End ),
                        publishingQueue );
                }
                catch ( Exception e )
                {
                    Console.Error.WriteLine( $"{e.Message}:\n{e.StackTrace}" );
                }
            } );

            int resultCount = 0;
            foreach ( var result in publishingQueue.GetConsumingEnumerable() )
            {
                if ( result.Success )
                {
                    if ( result.Json != null )
                    {
                        // This company JSON was fetched from the web.
                        DTO.CompanyInfo ci = UpsertCompany( result.Json, companies );
                        //Console.WriteLine( JsonConvert.SerializeObject(ci, Newtonsoft.Json.Formatting.Indented ) );
                    }                    
                    //Console.WriteLine($"\n-------Fetching {result.CompanyNumber} OK   : {( result.Json != null ? "JSON upserted" : result.Message )}-------" );
                    Console.Write( $".{( ++resultCount % 100 == 0 ? "\n" : "")}" );
                }
                else
                {
                    UpsertCompany( JsonConvert.SerializeObject( 
                        new DTO.CompanyInfo() 
                        { 
                            CompanyNumber = $"{result.CompanyNumber}", 
                            DoesNotExist = true 
                        } ), companies );
                    //Console.WriteLine( $"-------Fetching {result.CompanyNumber} ERROR: {result.Message?.Substring( 0, Math.Min( result.Message.Length, 60 ) )}-------" );
                    Console.Write( $"{( ++resultCount % 1000 == 0 ? "." : "")}{(resultCount % 100000 == 0 ? "\n" : "")}" );
                }
                
                yield return (new FetchResult
                {
                    CompanyNumber     = result.CompanyNumber,
                    WasFetchedFromWeb = result.Json != null || !result.Success 
                });

            }

            Console.WriteLine( "\nWaiting for the fetcher to finish..." );
            fetcher.Wait();
            
            var fetchStart = companyNumberList.First();
            var fetchEnd   = companyNumberList.Last();

            if ( !fetchedRangesList.Any( fr => fr.Start <= fetchStart && fetchEnd <= fr.End ) )
            {
                var nfr = new FetchedRange()
                {
                    FetchedRangeId = fetchedRangesList.Max( fr => fr.FetchedRangeId ) + 1,
                    Start = fetchStart,
                    End = fetchEnd
                };
                fetchedRanges.Upsert( nfr );
            }
            
            Console.WriteLine( "Waiting for the fetcher to finish..." );
        }

        /// <returns>true if the field exists and was indexed</returns>
        public static bool Index( LiteDatabase db, string fieldName, bool indexIsUnique )
        {
            if ( fieldName != null && 
                 typeof( DTO.CompanyInfo ).GetProperties().Any( pi => pi.Name.Equals( fieldName ) ) )
            {
                LiteCollection<DTO.CompanyInfo> companies = db.GetCollection<DTO.CompanyInfo>( "companies" );
                companies.EnsureIndex( fieldName, indexIsUnique );
                //Console.WriteLine( $"Done indexing {fieldName} ({( indexIsUnique ? "" : "not " )}unique)" );
                return true;
            }
            return false;
        }

        public static void Delete( LiteDatabase db, int companyInfoId )
        {
            LiteCollection<DTO.CompanyInfo> companies = db.GetCollection<DTO.CompanyInfo>( "companies" );
            companies.Delete( c => c.CompanyInfoId == companyInfoId );
        }

        public static IEnumerable<DTO.CompanyInfo> GetCompanies( 
            LiteDatabase db, Func<DTO.CompanyInfo, bool> predicate )
        {
            LiteCollection<DTO.CompanyInfo> companies = db.GetCollection<DTO.CompanyInfo>( "companies" );
            return companies.FindAll().Where( c => predicate(c) );
        }

        public static IEnumerable<DTO.CompanyInfo> GetCompaniesWhere( 
            LiteDatabase db, string field, Func<BsonValue, bool> predicate )
        {
            var companies = db.GetCollection<DTO.CompanyInfo>( "companies" );
            return companies.Find( Query.Where( field, predicate ) );
        }
    }
}
