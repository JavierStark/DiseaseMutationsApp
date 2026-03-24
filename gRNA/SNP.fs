module gRNA.SNP

open System.Net.Http
open System.Threading.Tasks

let private httpClient = new HttpClient()

let loadJsonFromUrlAsync (url: string) : Task<string> =
    task {
        let! response = httpClient.GetStringAsync(url)
        return response
    }

let getHgvsNotationsAsync (rsNumber: string) : Task<string list> =
    task {
        let url = $"https://api.ncbi.nlm.nih.gov/variation/v0/refsnp/{rsNumber}"
        let! jsonString = loadJsonFromUrlAsync url
        
        try
            let pattern = "NG_\\d+(?:\\.\\d+)?:[a-z]\\.[a-zA-Z0-9_>+*=\\-]+"
            let matches = System.Text.RegularExpressions.Regex.Matches(jsonString, pattern)
            let hgvsNotations =
                matches
                |> Seq.cast<System.Text.RegularExpressions.Match>
                |> Seq.map _.Value
                |> Seq.filter (fun x -> not (x.EndsWith("=")))
                |> Seq.distinct
                
                |> Seq.toList
            
            printfn $"HGVS Notations: %A{hgvsNotations}"
            
            return hgvsNotations
        with ex ->
            printfn "Error fetching SNP data: %s" ex.Message
            return []
    }
