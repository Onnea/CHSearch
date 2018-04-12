#r "bin/Debug/CHSearch.exe"
#r "bin/Debug/PocoGeneration.exe"
#r "bin/Debug/LiteDB.dll"

open Onnea
open LiteDB

let db = Commands.Get new LiteDatabase( @"C:\temp\CHSearch\db\temp.db", LiteDB.BsonMapper.Global )

let adf = Commands.Fetch( db, from = 314100, count = 350 );

let fetched = adf |> Seq.where (fun r -> r.WasFetchedFromWeb)|> Seq.toList

fetched |> Seq.length
let companiesA = Commands.GetCompanies( db, fun c -> c.CompanyName <> null && c.CompanyName.StartsWith( "B" ) );

companiesA 
|> Seq.toList
|> Seq.length

let companiesNull = Commands.GetCompanies( db, fun c -> c.CompanyName = null );

let n1 = Commands.GetCompanies( db, fun _ -> true ) |> Seq.where (fun c -> c.DoesNotExist) |> Seq.length

let five = 5
printf "%A" n1
