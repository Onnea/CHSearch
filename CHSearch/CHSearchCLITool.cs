using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;

namespace Onnea
{
    class CHSearchCLITool
    {
        static string PaddedCompanyNumber(int companyNumber) => companyNumber.ToString().PadLeft(8, '0');

        static void Main( string[] args ) => Console.WriteLine( Process( args ) );

        static string Process(string[] args)
        {
            if ( !args.Any() ) return "Missing any arguments"; 
            var strCmd = args.First();
            
            string ret = "Invalid command";

            if ( strCmd.StartsWith( "backup" ) )
            {
                Commands.BackUpExistingDatabase(Definitions.DbFilePath);
            }
            else if ( strCmd.StartsWith( "fetch" ) )
            {
                int count = 1;
                int from = int.MaxValue;

                #region Parse "fetch" args
                if ( args.Length == 2 )
                {
                    from = Int32.Parse( args[ 1 ] );
                }
                else if ( args.Length == 3 )
                {
                    from = Int32.Parse( args[ 1 ] );
                    count = Int32.Parse( args[ 2 ] );
                }
                else
                {
                    ret += ". Correct usage is: >fetch starting_company_number [number_of_steps]";
                }
                #endregion

                if ( from != int.MaxValue )
                {   
                    Directory.CreateDirectory( Path.GetDirectoryName( Definitions.DbFilePath ) );
                    
                    using ( var db = Commands.GetDatabase() )
                    {
                        Commands.Fetch( db, from, count );
                    }
                    ret = $"Done fetching at {DateTime.Now:yyyy-MM-dd HH:mm:ss.ffffff}";
                }
            }
            else if ( strCmd.StartsWith( "print" ) )
            {
                int count = 1;
                int from = int.MaxValue;

                #region Parse "print" args
                if ( args.Length == 2 )
                {
                    from = Int32.Parse( args[ 1 ] );
                }
                else if ( args.Length == 3 )
                {
                    from = Int32.Parse( args[ 1 ] );
                    count = Int32.Parse( args[ 2 ] );
                }
                else
                {
                    ret += ". Correct usage is: >print starting_company_number [number_of_steps]";
                }
                #endregion

                if ( from != int.MaxValue )
                {
                    using ( var db = Commands.GetDatabase() )
                    {
                        var matching = Commands.GetCompanies( db, c => c.CompanyInfoId >= from && 
                                                                       c.CompanyInfoId < from + count &&
                                                                      !c.DoesNotExist );
                        foreach ( var comp in matching )
                        {
                            Console.WriteLine( $"{JsonConvert.SerializeObject( comp, Formatting.Indented )}" );
                            Console.WriteLine( $"----------------------------------------" );
                        }
                    }
                }

                ret = "Printing done";
            }
            else if ( strCmd.StartsWith( "sic" ) )
            {
                int sic = int.MaxValue;

                #region Parse args
                if ( args.Length == 2 )
                {
                    sic = Int32.Parse( args[ 1 ] );
                }
                else
                {
                    ret += ". Correct usage is: >sic desired_sic_code";
                }
                #endregion

                if ( sic != int.MaxValue )
                {
                    using ( var db = Commands.GetDatabase() )
                    {
                        var matching = Commands.GetCompanies( db, c => c.CompanyStatus != null && c.CompanyStatus.ToLower() == "active" &&
                                                                       c.SicCodes != null && c.SicCodes.Contains( sic.ToString() ) );
                        foreach ( var comp in matching )
                        {
                            Console.WriteLine( $"{comp.CompanyNumber}: {comp.CompanyName}" );
                        }
                    }
                }

                ret = "Done SICcing";
            }
            else if ( strCmd.StartsWith( "index" ) )
            {
                #region Parse args
                string fieldName = null;
                bool indexIsUnique = true;

                if ( args.Length >= 2 && args[ 1 ].Trim() != "" )
                {
                    fieldName = args[ 1 ].Trim();
                    if ( args.Length == 3 && bool.TryParse( args[ 2 ].Trim(), out bool uniqueParsed ) )
                    {
                        indexIsUnique = uniqueParsed;
                    }
                }
                else
                {
                    ret += ". Correct usage is: >index fieldName isUnique([true]/false).\nAvailable names are:\n" + 
                    string.Join( ", ", typeof( DTO.CompanyInfo ).GetProperties().Select( pi => pi.Name ) );
                }
                #endregion

                using ( var db = Commands.GetDatabase() )
                {
                    ret += Commands.Index( db, fieldName, indexIsUnique );
                }
            }
            else if ( strCmd.StartsWith( "nullnumber" ) )
            {
                #region Parse args
                if ( args.Length > 2 )
                {
                    ret += ". Correct usage is: >nullnumber [delete]";
                }

                var deleteNulls = args.Length == 2 && args[ 1 ].ToLower().Equals( "delete" );
                #endregion

                using ( var db = Commands.GetDatabase() )
                {
                    var matching = Commands.GetCompanies( db, c => c.CompanyNumber == null );

                    foreach ( var comp in matching.ToList() )
                    {
                        if ( deleteNulls )
                        {
                            Commands.Delete( db, comp.CompanyInfoId );
                        }
                        Console.WriteLine( $"NULL: {comp.CompanyInfoId}: {comp.CompanyName} ({( deleteNulls ? "deleted" : "" )})" );
                    }
                }

                ret = $"Done {( deleteNulls ? "deleting" : "listing" )} NULLs";
            }
            else if ( strCmd.Equals( "exit" ) )
            {
                throw new Exception( "Exiting" );
            }

            return ret + Environment.NewLine;
        }
    }
}
