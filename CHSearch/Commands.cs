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

        /// <returns>A sequence of company numbers together with whether they were actually fetched from the web.</returns>
        public static IEnumerable<FetchResult> Fetch( LiteDatabase db, int from, int count )
        {
            // Get a collection (or create, if doesn't exist)
            LiteCollection<Onnea.DTO.CompanyInfo> companies = db.GetCollection<DTO.CompanyInfo>( "companies" );

            companies.EnsureIndex( nameof( DTO.CompanyInfo.DoesNotExist ), unique: false );

            var companyNumberList = Enumerable.Range( from, count ).ToList();
            var companyNumbersToFetch = new HashSet<int>( companyNumberList );//06052617, 600 * 12 * 24 * 7);//08264572, 1000 );

            var publishingQueue = new BlockingCollection<FetchCompanyJson.Result>();

            // Populate the list of known companies that do not need to be fetched.
            var allKnownCompanyNumbers = new HashSet<int>( companies.FindAll().Select( c => c.CompanyInfoId ) );

            Task fetcher = Task.Run( () =>
                FetchCompanyJson.Run( Definitions.ApiKey,
                                      companyNumbersToFetch,
                                      companyNumber => allKnownCompanyNumbers.Contains( companyNumber ),
                                      publishingQueue ) );

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
                    //Console.WriteLine(
                    //    $"\n-------Fetching {result.CompanyNumber} OK   : {( result.Json != null ? "JSON upserted" : result.Message )}-------" );
                }
                else
                {
                    UpsertCompany( JsonConvert.SerializeObject( new DTO.CompanyInfo() 
                                                                { 
                                                                    CompanyNumber = $"{result.CompanyNumber}", 
                                                                    DoesNotExist = true 
                                                                } ), companies );
                    //Console.WriteLine( $"-------Fetching {result.CompanyNumber} ERROR: {result.Message?.Substring( 0, Math.Min( result.Message.Length, 60 ) )}-------" );
                }
                yield return (new FetchResult
                {
                    CompanyNumber     = result.CompanyNumber,
                    WasFetchedFromWeb = result.Json != null || !result.Success 
                });
            }

            //Console.WriteLine( "Waiting for the fetcher to finish..." );
            fetcher.Wait();   
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

        public static IEnumerable<DTO.CompanyInfo> GetCompanies( LiteDatabase db, Func<DTO.CompanyInfo, bool> predicate )
        {
            LiteCollection<DTO.CompanyInfo> companies = db.GetCollection<DTO.CompanyInfo>( "companies" );
            return companies.FindAll().Where( c => predicate(c) );
        }
    }
}
