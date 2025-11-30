#r "nuget: FSharp.Data.JsonProvider"
open FSharp.Data.JsonProvider

open System.Net.Http

let rs = "rs77101217"
let rsNum = rs[2..]

type SnpData = JsonProvider<"snp_sample.json">

let private httpClient = new HttpClient()

let loadJsonFromUrlAsync (url: string) : Async<string> =
    async {
        let! response = httpClient.GetStringAsync(url) |> Async.AwaitTask
        return response
    }

let getHgvsNotationsAsync (rsNumber: int) : Async<string list> =
    async {
        let url = $"https://api.ncbi.nlm.nih.gov/variation/v0/refsnp/{rsNumber}"
        
        try
            let! jsonString = loadJsonFromUrlAsync url
            
            let snpData = SnpData.Parse(jsonString)
            
            printfn $"snp object: %A{snpData}"
            
            let hgvsNotations =
                snpData.PrimarySnapshotData.PlacementsWithAllele
                |> Array.collect _.Alleles
                |> Array.map _.Hgvs
                |> Array.filter (_.StartsWith("NG_"))  // filter for genomic notations
                |> Array.filter (fun h -> not (h.Contains("=")))  // exclude no-change notations
                |> Array.toList
                
            printfn $"HGVS Notations: %A{hgvsNotations}"
            
            return hgvsNotations
            
        with ex -> 
            printfn "Error fetching SNP data: %s" ex.Message
            return []
    }


let hgvsNotations = getHgvsNotationsAsync rsNum |> Async.RunSynchronously