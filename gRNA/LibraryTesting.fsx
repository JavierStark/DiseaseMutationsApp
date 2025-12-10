#r "nuget: FSharp.Data"
#r "./bin/Debug/net9.0/gRNA.dll"

open gRNA.Omim

rsFromOmim 261600
|> printfn "%A"
