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

        public static void SnoozeIfRequestQuotaRunningLow( HttpResponseMessage response )//, LinkedList<TimeSpan> snoozeTimes )
        {
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
                    var howLongToSnooze = TimeSpan.FromSeconds( (int) (5 * 60.0 / remaining) );
                    //snoozeTimes.AddFirst( howLongToSnooze );
                    //var snoozes = snoozeTimes.Take( Math.Min( 5, snoozeTimes.Count ) ).Skip( 1 )
                    //                            .Select( s => $"{s.Minutes}min {s.Seconds}s" );
                    //Console.WriteLine( $"Snoozing for {howLongToSnooze.Minutes} minutes {howLongToSnooze.Seconds} seconds as remaining attempts for time slot is {remaining}.\n" +
                    //                    $"Last snooze periods were: {string.Join( ", ", snoozes )}..." );
                    Console.WriteLine( $"Snoozing for {howLongToSnooze.Minutes} minutes {howLongToSnooze.Seconds} seconds as remaining attempts for time slot is {remaining}.");
                    Thread.Sleep( howLongToSnooze );
                }
                else if ( remaining % 10 == 0 )
                {
                    Console.WriteLine( $"Number of attempts remaing for time slot is {remaining}" );
                        
                }
            }
        }

        public static async Task Run( string                     apikey,
                                      IEnumerable<int>           companyNumbers,
                                      Func<int,bool>             alreadyExistsFn,
                                      BlockingCollection<Result> publishingQueue )
        {
            HttpClient client = new HttpClient(); 
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(apikey.Trim());
            //DateTime lastSnoozeFinishedAt = DateTime.MinValue;
            //LinkedList<TimeSpan> snoozeTimes = new LinkedList<TimeSpan>();
            
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
                HttpResponseMessage response = await client.GetAsync(apiurl);

                SnoozeIfRequestQuotaRunningLow( response );//, snoozeTimes );

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
