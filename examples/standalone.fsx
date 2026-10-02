// Worked example: use the gRNA library from F# Interactive, with no web app and no Docker.
//
//   dotnet build gRNA -c Release
//   dotnet fsi examples/standalone.fsx            (works from any directory)
//
// Part 1 uses only pure functions (no Bowtie, no Python). Part 2 runs the full pipeline and needs the native
// tools: set GRNA_BOWTIE_BINARY and GRNA_BOWTIE_INDEX (and have python3 with `import RNA`), or run it in the container.

#r "../gRNA/bin/Release/net9.0/gRNA.dll"

open gRNA

// ---- Part 1: pure functions -------------------------------------------------------------------------------
let hgvs = HGVS.HGVS "NG_008690.2:g.5000A>T"
printfn "accession=%s position=%A mutation=%A alt=%s" hgvs.Accession hgvs.Position hgvs.Mutation hgvs.Alternate

// A made-up 60 nt reference around position 30, mutated at that base.
let reference = "ACGTACGTACGTACGTACGTACGTACGTAAGTACGTACGTACGTACGTACGTACGTACGTACGT"
let seqObj = Sequence.Sequence("TEST.1", reference)
let variant = HGVS.HGVS "TEST.1:g.30A>T"
let mutated, original = seqObj.GetMutatedSubsequence(variant, 10, 10)
printfn "original: %s\nmutated : %s" original mutated

// GC scoring: the boundaries are inclusive, so exactly 40%% and 60%% GC score a perfect 1.0.
for gc in [ 39.99; 40.0; 50.0; 60.0; 60.01 ] do
    printfn "GC %.2f%% -> score %.3f" gc (SpacerFinder.calculateGCScore gc (40.0, 60.0))

// Pooling: 100 guides, at most 5 per tube, on 96-well plates; then decode a screening result.
let plan = Pooling.buildPlan (Pooling.bestModel 100 5) Pooling.plate96 100 5
printfn "%s: %d wells, %d to prepare" (Pooling.modelName plan.Model) plan.TotalWells plan.NonEmptyWells
let wellsOfGuide7 = plan.Pools |> List.filter (fun p -> List.contains 7 p.GuideIndices) |> List.map (fun p -> p.Id)
let decoded = Pooling.decode plan wellsOfGuide7
printfn "positive tubes %A implicate guides %A (ambiguous: %b)" wellsOfGuide7 decoded.Implicated decoded.IsAmbiguous

// ---- Part 2: the full pipeline (needs Bowtie, the index, python3 + ViennaRNA, and network access) ---------------------
if System.Environment.GetEnvironmentVariable "GRNA_RUN_PIPELINE" = "1" then
    GrnaLog.sink <- printfn "[gRNA] %s"
    let bowtie = Services.BowtieService()
    let result =
        Main.getBestgRNAFromHGVS "NG_008690.2:g.5000A>T" 28 10 17 bowtie System.Threading.CancellationToken.None false
        |> fun t -> t.GetAwaiter().GetResult()
    for g in List.truncate 3 result.gRNA do
        printfn "rank %d %s  GC=%.1f  alignments=%d  energy=%.2f" g.Rank g.Sequence g.GCContent g.Allignments g.RnaFoldResult.Energy
