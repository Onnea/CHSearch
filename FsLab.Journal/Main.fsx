
#r "bin/Debug/Newtonsoft.Json.dll"
#r "bin/Debug/CHSearch.exe"
#r "bin/Debug/PocoGeneration.exe"
#r "bin/Debug/LiteDB.dll"

open Onnea
open Newtonsoft.Json
open Onnea.DTO

//let db = Commands.GetDatabase( @"C:\temp\CHSearch\db\big.db" )
let db = Commands.GetDatabase( @"C:\temp\CHSearch\db\temp.db" )

let adf = Commands.Fetch( db, from = 314330, count = 15 );

let fetched = adf |> Seq.toList
fetched |> Seq.length

let fromWeb = fetched |> Seq.where (fun r -> r.WasFetchedFromWeb) |> Seq.toList

//Commands.Index( db, "CompanyName", true )

let companiesB = Commands.GetCompaniesWhere( db, "CompanyName",
                  fun c -> c.AsString <> null && c.AsString.StartsWith( "P" ) )

companiesB |> Seq.toList

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
