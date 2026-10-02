# Using the gRNA library

`gRNA/` is an F# library (target `net9.0`). The web app and the `grna` CLI are two consumers of it; you can be a third.
The library does the biology; it cannot ship its native dependencies, so using it is always a **two-step install**:
reference/pack the library, then provide Bowtie, a Bowtie index and Python with ViennaRNA
(see [runtime-contract.md](runtime-contract.md)).

## Consumption recipes

**F# Interactive** (no web app): [`examples/standalone.fsx`](../examples/standalone.fsx), runnable from any directory:
`dotnet build gRNA -c Release && dotnet fsi examples/standalone.fsx`.

**ProjectReference** from C#/F#:

```xml
<ProjectReference Include="path/to/gRNA/gRNA.fsproj" />
```

**Local NuGet feed**: `dotnet pack gRNA -c Release -o ./nupkg` produces `gRNA.1.0.0.nupkg` carrying the MIT expression;
add `./nupkg` as a source. The package never contains Bowtie, the index or ViennaRNA.

**Dependency injection** (what the web app does): one singleton `BowtieService` per process (it owns the single-slot
Bowtie semaphore), everything else stateless:

```csharp
builder.Services.AddSingleton<gRNA.Services.BowtieService>();
gRNA.GrnaLog.setSink(m => logger.LogDebug("{Message}", m));          // the library is silent by default
gRNA.GrnaEnvironmentModule.setCurrent(customEnvironment);            // optional: paths, timeouts, NCBI key
```

**CLI**: `dotnet tool install` the packed `gRNA.Cli` or run it from the image: `docker run --rm --entrypoint grna <image> design --hgvs ...`.

## Configuration seam

`GrnaEnvironment` (record) holds `BowtieBinary`, `BowtieIndexBase` (option), `BowtieIndexDirectory`, `PythonExecutable`,
`ProcessTimeout`, `NcbiApiKey`, `NcbiContact`, `SequenceCacheEntries`. `GrnaEnvironment.defaults` is the baseline;
`GrnaEnvironment.fromEnvironmentVariables()` applies the `GRNA_*` variables; relative paths resolve against
`AppContext.BaseDirectory` first, then the current directory, so a published app, a test runner and `dotnet fsi` all work.
`GrnaEnvironment.setCurrent` sets the process-wide value used by the wrappers (from C# it is `GrnaEnvironmentModule`).

Errors are typed: `GrnaInputException` (bad HGVS), `GrnaDependencyException` (missing/unusable Bowtie, index, Python,
ViennaRNA), `GrnaUpstreamException` (NCBI or a child process misbehaved). All child processes have timeouts and honour
cancellation tokens.

## API reference

### `Main` (global namespace)

```fsharp
Main.getBestgRNAFromHGVS : hgvs:string -> grnaSize:int -> seedStart:int -> seedEnd:int
                           -> BowtieService -> CancellationToken -> complement:bool -> Task<ResultFromHGVS>
```

The function is **curried**; from C# the compiler exposes it as a normal method taking all arguments in order, as
`GrnaService.GetBestgRNAFromHgvs` shows: `Main.getBestgRNAFromHGVS(hgvs, window, seedStart, seedEnd, bowtie, ct, complement)`.
`ResultFromHGVS` (note: a different type from the C# record of the same name in `DiseaseMutationsApp.Services`) carries
`gRNA`, `originalGRNA` (ranked candidate lists), `mutatedSequence`, `originalSequence` and `extraNucleotids`. For
substitutions the special rule may replace a list with a single engineered candidate (it can also yield **zero** candidates
while carrying the pre-adjustment alignment and fold values onto the adjusted sequence).

### `HGVS.HGVS(code)`
Parsing happens in the **constructor**, so construction is what throws `GrnaInputException`. Members: `Code`, `Accession`,
`Type`, `Position` (start, end; 1-based), `Mutation` (`MutationType`), `Reference`, `Alternate`, `Count`, `GetMutationLength()`.

### `Sequence`
`Sequence.complementary s` returns the **complement without reversing** (the `(C)` tabs are complement-only; the spacer
derivation later reverses). `Sequence(id, data).GetMutatedSubsequence(hgvs, left, right)` returns `(mutated, original)`
with padding. `Repeat` parses but `GetMutatedSubsequence` throws `NotImplementedException`.

### `SequenceRepository.SequenceRepository.GetSequence(accession[, ct])`
Fetches FASTA from NCBI E-utilities. Cached (bounded, insertion-ordered), concurrency-safe, one in-flight fetch per
accession, failures not cached.

### `SNP.getHgvsNotationsAsync rsNumber ct`
Resolves an rsID (digits, no `rs`). Returns `[]` for "no `NG_` mapping"; raises `GrnaUpstreamException` for HTTP failure.
`NM_`/`NC_` notations are not returned.

### `SpacerFinder`
`scaffold`, `slidingWindow`, `calculateGCContent`, `calculateGCScore gc (lower, upper)` (inclusive bounds),
`countHomopolymers`, `getgRNAResult`, `getOrderedgRna` (the whole ranking, async), `applySubstitutionSpecialRule`,
`sortByResult`. Ranking key: `(Alignments, -Energy, -GCScore, Homopolymers)`.

### `RNAFoldWrapper`
`fold seq ct`, `foldMany seqs ct`, `foldManyWith env seqs ct` (result: `{ Structure; Energy }`). Bounded to 2 concurrent
interpreters per process; sequences go as `ArgumentList` items, never through string interpolation.

### `BowtieWrapper` / `Services.BowtieService`
`BowtieService.ProcessMultipleSequencesAsync(sequences, mismatches, threads, ct)` returns one alignment count per read.
It implements `IBowtieRunner`, the seam for substituting a stub. Counts saturate at `-k 6`: **6 means "6 or more"**.

### `Parsing`
Pure, unit-tested parsers: `parseFoldLine`, `parseFoldBatch`, `tryParseBowtieLine`, `parseBowtieAlignments` (records with
read index, strand, reference, offset, sequence), `countAlignments`.

### `Diagnostics`
`runAll env includeNetwork` returns `CheckResult list`; `isHealthy`. Shared by `grna doctor` and `/healthz`.

### `ReportSchema`
`columns`, `header`, `row`, `provenance`: the CSV the web export, `grna design --format csv` and the Pooling parser share.

### `Pooling`
`buildPlan model format guideCount wellCapacity`, `compareModels`, `bestModel`, `wellsForModel`, `repetitions`,
`modelName`, `plate96`/`plate384`, and `decode plan positivePoolIds` which maps positive wells back to guides
(`Implicated`, `Definite`, `Ambiguous`, `PartiallySupported`, `UnexplainedPools`, `UnknownPools`, `IsAmbiguous`).
Every guide is in exactly `repetitions model` pools; a guide is implicated when all of them read positive. Two true hits
can implicate innocent guides at the intersections of their pools; those are reported as `Ambiguous`, not hidden.

## Known limitations

- `Repeat` parses but cannot be applied (`NotImplementedException`).
- There is **no reference-allele validation**: a wrong accession version silently yields a plausible but wrong mutated sequence.
- Lowercase/soft-masked and IUPAC bases are not normalised: `complementary` passes them through, and GC/homopolymer
  counting is uppercase-only.
- `GetMutationLength` ignores the inserted allele, so `extraNucleotids` can go negative for long deletions.
- Alignment counts saturate at 6 (`-k 6`).
- The substitution special rule can legitimately return zero candidates while carrying the pre-adjustment alignment and
  fold values onto the adjusted sequence.
- Bowtie is serialised per process.
