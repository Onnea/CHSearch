
#r "bin/Debug/Newtonsoft.Json.dll"
#r "bin/Debug/CHSearch.exe"
#r "bin/Debug/PocoGeneration.exe"
#r "bin/Debug/LiteDB.dll"

open System
open Onnea
open Newtonsoft.Json
open Onnea.Domain
open System.IO

//let db = Commands.GetDatabase( @"C:\temp\CHSearch\db\big.db" )
let db = Commands.GetDatabase( @"C:\temp\CHSearch\db\temp.db" )

let adf = Commands.Fetch( db, from = 314330, count = 15 );
let fetched = adf |> Seq.toList //|> Seq.length
let fromWeb = fetched |> Seq.where (fun r -> r.WasFetchedFromWeb) |> Seq.toList
//Commands.Index( db, "CompanyName", false )

let companiesB = Commands.GetCompaniesWhere( db, "CompanyName",
                  fun c -> c.AsString <> null && c.AsString.StartsWith( "B" ) )

let companiesC = Commands.GetCompaniesWhere( db, "_id", // aka "CompanyInfoId"
                  fun c -> c.AsInt32 = 314128 ) |> List.ofSeq

let activeB = companiesB 
              //|> Seq.filter (fun c -> c.CompanyStatus = "active") 
              |> Seq.toList
let fh = 
  activeB 
  |> List.map (
       fun c -> Commands.GetFilingHistory( db, c, DateTime.Parse( "2018-05-22" )))
       
fh.Head.Items |> Seq.last 
|> fun i -> i.Links.DocumentMetadata.Split('/') |> Seq.last,
            Commands.GetDocument(i) 
//|> fun (name, bytes) -> File.WriteAllBytes( @"C:\temp\CHSearch\documents\" + name + ".pdf", bytes );
|> fun (name, pagesAsBytes) -> 
    pagesAsBytes 
    |> Seq.iteri (fun i bytes -> 
                 File.WriteAllBytes( @"C:\temp\CHSearch\documents\images\" + name + "." + i.ToString() + ".jpg", bytes ) );

let docos = 
    fh.Head.Items 
    |> Seq.map (Commands.GetDocument)
    |> List.ofSeq


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
