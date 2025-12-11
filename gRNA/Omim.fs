module gRNA.Omim

open FSharp.Data
open gRNA.LevenshteinDistance
open System.Threading.Tasks

type Phenotype = HtmlProvider<"https://omim.org/entry/261600">
type AllelicVariant = HtmlProvider<"https://omim.org/allelicVariants/612349">

let similarityThreshold = 75.0

 
 
let rsFromOmim (omim: int) =
    task{
        use client = new System.Net.Http.HttpClient()
        let! phenotypeResponse = client.GetStringAsync($"https://omim.org/entry/%d{omim}")
        
        
        let phenotypeHtml = Phenotype.Parse(phenotypeResponse);
        let phenotypeRows =
            phenotypeHtml.Tables.``Phenotype-Gene Relationships``.Rows

        let genes =
            phenotypeRows
            |> Seq.map _.``Gene/Locus MIM number``
            |> Seq.distinct
            |> Seq.toList
            
        let phenotypesDistances =
            phenotypeRows
            |> Seq.map _.Phenotype
            |> Seq.distinct
            |> Seq.toList
            |> List.map levenshteinSimilarityPercentage

        let! allelicVariantsHtml =
            genes
            |> List.map (fun mimNumber -> 
                task {
                    let! avResponse = client.GetStringAsync($"https://omim.org/allelicVariants/%d{mimNumber}")
                    return AllelicVariant.Parse(avResponse)
                })
            |> Task.WhenAll
            
        let isRelevantPhenotype (phenotype: string) =
            phenotypesDistances
            |> List.exists (fun distanceFunc ->
                distanceFunc phenotype > similarityThreshold)
            
        let allelicVariants =
            allelicVariantsHtml
            |> Array.toList
            |> List.collect (fun avHtml -> avHtml.Tables.Table1.Rows |> Seq.toList)
            |> List.filter (fun row -> isRelevantPhenotype row.Phenotype)
            |> List.map _.SNP
            |> List.distinct
            |> List.filter _.StartsWith("rs")

        return allelicVariants
    }
