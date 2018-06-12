
#r "bin/Debug/Newtonsoft.Json.dll"
#r "bin/Debug/CHSearch.exe"
#r "bin/Debug/PocoGeneration.exe"
#r "bin/Debug/LiteDB.dll"
#r @"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.6.1\System.Net.Http.dll"

open System
open Onnea
open Newtonsoft.Json
open Onnea.Domain
open System.IO

Directory.SetCurrentDirectory(@"C:\Projects\CHSearch\CHSearch\bin\Debug")

//let db = Commands.GetDatabase( @"C:\temp\CHSearch\db\big.db" )
let db = Commands.GetDatabase( @"C:\temp\CHSearch\db\temp.db" )

db.GetCollection("companies").FindAll() |> Seq.take 100 |> Seq.iter (fun c -> System.Console.WriteLine( c.ToString().Substring( 0, 80) ))

let adf = Commands.Fetch( db, from = 314330, count = 15 );
let fetched = adf |> Seq.toList //|> Seq.length
let fromWeb = fetched |> Seq.where (fun r -> r.WasFetchedFromWeb) |> Seq.toList
//Commands.Index( db, "CompanyName", false )

let allCompanies = Commands.GetCompanies( db, fun _ -> true ) |> Seq.filter (fun c -> not c.DoesNotExist) |> List.ofSeq
allCompanies |> Seq.map (fun c -> c.CompanyName)  |> Seq.take 10

let companiesB = Commands.GetCompaniesWhere( db, "CompanyName",
                  fun c -> c.AsString <> null && c.AsString.StartsWith( "B" ) )
let companiesC = Commands.GetCompaniesWhere( db, "_id", // aka "CompanyInfoId"
                  fun c -> c.AsInt32 = 314128 ) |> List.ofSeq
let activeB = companiesB 
              //|> Seq.filter (fun c -> c.CompanyStatus = "active") 
              |> Seq.toList
let fh = 
  allCompanies 
  |> Seq.map (fun c -> c.CompanyName, Commands.GetFilingHistory( db, c, DateTime.Parse( "2018-05-22" )))
 
fh |> Seq.length

let fhsWithFullAccounts 
  = fh |> Seq.where (fun h -> (h |> snd).Items 
                             |> Seq.exists (fun i -> i.Description.ToLower().Contains("full")))
       |> List.ofSeq

fhsWithFullAccounts 
   |> Seq.last
   |> fun fhi -> (fhi |> snd).Items |> Seq.filter (fun i -> i.Links <> null) |> Seq.last 
   |> fun i -> Commands.GetDocument(i)
   |> fun doco -> printfn "%s" doco.TextFile

//|> fun (name, bytes) -> File.WriteAllBytes( @"C:\temp\CHSearch\documents\" + name + ".pdf", bytes );

let docos = 
    fhsWithFullAccounts 
    |> Seq.map (fun fhi -> (fhi |> fst), 
                           (fhi |> snd).Items |> Seq.map (fun i -> Commands.GetDocument(i) ) )
    |> List.ofSeq

let lines = docos.Item 3 |> snd |> Seq.map( fun fhd -> fhd.TextFile |> File.ReadAllLines )

lines |> Seq.map (Seq.item 1) |> Seq.take 6 |> List.ofSeq


companiesB 
|> Seq.map (fun c -> JsonConvert.SerializeObject(c, Newtonsoft.Json.Formatting.None )) 
|> Seq.filter (fun t -> t.Contains( "company_name\":null" ) |> not)
|> Seq.toList |> Seq.iter (printfn "%s")

let indices = db.GetCollectionNames() |> Seq.head |> fun c -> db.GetCollection( c ).GetIndexes()

let ranges = db.GetCollection( "ranges" )

let companiesNull = Commands.GetCompanies( db, fun c -> c.CompanyName = null );

let n1 = Commands.GetCompanies( db, fun _ -> true ) |> Seq.where (fun c -> c.DoesNotExist) |> Seq.length

let five = 5
printf "%A" n1
