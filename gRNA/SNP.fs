module gRNA.SNP

open FSharp.Data.JsonProvider
open System.Net.Http
open System.Threading.Tasks

type SnpData = JsonProvider<"snp_sample.json">


let private httpClient = new HttpClient()

let loadJsonFromUrlAsync (url: string) : Task<string> =
    task {
        let! response = httpClient.GetStringAsync(url)
        return response
    }

let getHgvsNotationsAsync (rsNumber: string) : Task<string list> =
    task {
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
