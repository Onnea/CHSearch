using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Onnea
{
    internal class FetchCompanyJson
    {
        public struct Result
        {
            public int CompanyNumber;
            public bool Success;
            public string Message;
            public string Json;
        }

        public static async Task Run( string                     apikey,
                                      IEnumerable<int>           companyNumbers,
                                      Func<int,bool>             alreadyExistsFn,
                                      BlockingCollection<Result> publishingQueue )
        {
            HttpClient client = new HttpClient(); 
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(apikey);
            DateTime lastSnoozeFinishedAt = DateTime.MinValue;
            LinkedList<TimeSpan> snoozeTimes = new LinkedList<TimeSpan>();
            
            foreach (var companyNumber in companyNumbers)
            {
                if ( alreadyExistsFn( companyNumber ) )
                {
                    publishingQueue.Add( new Result()
                    {
                        CompanyNumber = companyNumber,
                        Success = true,
                        Message = "Already fetched"
                    } );
                    continue;
                }
                
                var paddedCompanyNumber = companyNumber.ToString().PadLeft(8, '0');
                string apiurl = $"https://api.companieshouse.gov.uk/company/{paddedCompanyNumber}";
                //string apiurl = $"https://api.companieshouse.gov.uk/search?q=82990";
                HttpResponseMessage response = await client.GetAsync(apiurl);

                IEnumerable<string> rateLimitRemainValues;
                response.Headers.TryGetValues("X-Ratelimit-Remain", out rateLimitRemainValues);
                if ( rateLimitRemainValues != null && rateLimitRemainValues.Any() )
                {
                    // Example headers:
                    // X-Ratelimit-Remain: 599
                    // X-Ratelimit-Window: 5m
                    var remaining = Int32.Parse(rateLimitRemainValues.First());
                    if ( remaining <= 30 )
                    {
                        //var oneMin = TimeSpan.FromSeconds( 1 * 60 );
                        var howLongToSnooze = TimeSpan.FromSeconds( (int) (5 * 60.0 / remaining) );
                            //lastSnoozeFinishedAt.Equals( DateTime.MinValue ) ? oneMin
                            //: oneMin.Ticks - ( DateTime.Now - lastSnoozeFinishedAt ).Ticks > 0 ? oneMin - ( DateTime.Now - lastSnoozeFinishedAt )
                            //: oneMin; // the last option is a bit puzzling and should never occur
                        snoozeTimes.AddFirst( howLongToSnooze );
                        var snoozes = snoozeTimes.Take( Math.Min( 5, snoozeTimes.Count ) ).Skip( 1 )
                                                 .Select( s => $"{s.Minutes}min {s.Seconds}s" );
                        Console.WriteLine( $"Snoozing for {howLongToSnooze.Minutes} minutes {howLongToSnooze.Seconds} seconds as remaining attempts for time slot is {remaining}.\n" +
                                           $"Last snooze periods were: {string.Join( ", ", snoozes )}..." );
                        Thread.Sleep( howLongToSnooze );//> oneMin ? oneMin : howLongToSnooze );
                        lastSnoozeFinishedAt = DateTime.Now;
                    }
                    else if ( remaining % 10 == 0 )
                    {
                        Console.WriteLine( $"Number of attempts remaing for time slot is {remaining}" );
                        
                    }
                }

                if ( !response.IsSuccessStatusCode )
                {
                    publishingQueue.Add( new Result()
                    {
                        CompanyNumber = companyNumber,
                        Success = false,
                        Message = response.ToString()
                    } );
                }
                else
                {
                    string json = await response.Content.ReadAsStringAsync();
                    publishingQueue.Add( new Result()
                    {
                        CompanyNumber = companyNumber,
                        Success = true,
                        Json = json
                    } );
                }
            }
            publishingQueue.CompleteAdding();
        }
    }
}
