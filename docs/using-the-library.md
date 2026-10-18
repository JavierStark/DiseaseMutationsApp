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

Before any NCBI or Bowtie work it validates (all failures are `GrnaInputException`): `validateSpacerParameters` (spacer 18-60, seed inside it, constants `MinSpacerSize`/`MaxSpacerSize`), the parsed HGVS, and, once the sequence is fetched, `validateAgainstSequence` (supported coordinates `g.`/`m.`/`n.` - an unconverted `c.` is refused; `getBestgRNAFromHGVS` first converts `c.` on a transcript record with `HGVS.convertCodingToTranscript` and the CDS range from `SequenceRepository.GetCodingRange`, supported mutation type, position inside the sequence, adjacent positions for an insertion, stated reference equal to the real one). `complement = true` designs against the opposite strand: `orientForStrand` replaces both sequences by their reverse complement and mirrors the mutation span (`mirrorSpan`).

The function is **curried**; from C# the compiler exposes it as a normal method taking all arguments in order, as
`GrnaService.GetBestgRNAFromHgvs` shows: `Main.getBestgRNAFromHGVS(hgvs, window, seedStart, seedEnd, bowtie, ct, complement)`.
`ResultFromHGVS` (note: a different type from the C# record of the same name in `DiseaseMutationsApp.Services`) carries
`gRNA`, `originalGRNA` (ranked candidate lists), `mutatedSequence`, `originalSequence`, `extraNucleotids` and `windowStart`/`windowEnd` (the 1-based inclusive accession window the sequences were cut from). For
substitutions the special rule may replace a list with a single engineered candidate (the offtarget and fold values are carried over from the
unadjusted candidate). If no candidate carries the mutation at index `N-3` (`SpacerFinder.specialRuleApplies` is false: the mutation is too
close to an end of the sequence) the ranked list is kept and `notices` holds a warning (`Main.specialRuleNotice`).

### `HGVS.HGVS(code)`
Parsing happens in the **constructor**, so construction is what throws `GrnaInputException`. Members: `Code`, `Accession`,
`Type`, `Position` (start, end; 1-based), `Mutation` (`MutationType`), `Reference` (the stated reference base, or the stated `del`/`dup`/`inv` sequence), `Alternate`, `Count`, `GetMutationLength()`.
The constructor is strict: no colon or no change, position 0 or beyond Int32, a reversed range, lower-case or IUPAC alleles, a substitution that changes nothing, and a non-numeric repeat count all raise `GrnaInputException`. `Repeat`, `Conversion`, `Translocation` and `LargeUnknown` parse but are refused later by `Main.validateAgainstSequence`.

### `Sequence`
`Sequence.complementary s` returns the complement without reversing; `Sequence.reverseComplement s` is the other strand read 5'->3' (used for inversions and for the `(C)` tabs: since 2026-10-13 the complement tab is a real reverse-complement design, see `BIOLOGICAL_REPORT.md` section 4.1.1). `Sequence(id, data).GetMutatedSubsequence(hgvs, left, right)` returns `(mutated, original)`
with padding. `Repeat` parses but `GetMutatedSubsequence` throws `NotImplementedException`.

### `SequenceRepository.SequenceRepository.GetSequence(accession[, ct])`
Fetches FASTA from NCBI E-utilities. Cached (bounded, insertion-ordered), concurrency-safe, one in-flight fetch per
accession, failures not cached. The body goes through `parseFasta`, which accepts exactly one FASTA record of letters (upper-cased) and raises `GrnaUpstreamException` for anything else (an HTML/JSON error page, an empty body, a second record).

### `SNP.getHgvsNotationsAsync rsNumber ct`, `getAllHgvsNotationsAsync`, `getVariantsAsync`, `parseVariants`
Resolves an rsID (digits only, no `rs`; anything else raises `GrnaInputException` before a request is made). Returns `[]` for "no `NG_` mapping"; raises `GrnaUpstreamException` for HTTP failure.
`NM_`/`NC_` notations are not returned.
`getHgvsNotationsAsync` returns the **default selection**: the disease-relevant allele(s) on the main RefSeqGene record (everything if that would be empty); `getAllHgvsNotationsAsync` returns every allele on every `NG_` record. `getVariantsAsync` returns `SnpVariant` records (`Hgvs`, `Accession`, `Gene`, `GeneOnPlusStrand`, `Significance`, `Preferred`); `parseVariants json` is the pure parser behind them.

### Substitution-rule positions
`Main.getBestgRNAFromHGVSWithRule (rule: SpecialRuleOptions)` takes the variant and mismatch positions (both from the spacer's 3' end, 1 = last letter; `Main.defaultSpecialRule` is 3 and 4, `Main.validateSpecialRule` checks them); `getBestgRNAFromHGVS` is the same call with the defaults. Lower level: `SpacerFinder.applySubstitutionSpecialRuleAt snpPos mismatchPos ...`, `specialRuleAppliesAt`, `adjustPositionFromEndToAorU`. `ReportSchema.provenanceWithRule` records the positions and `tryParseRule` reads them back.

### Per-guide fields added 2026-10-16
`SpacerFinder.gRNAResult` has `WtMismatches` (fewest letters between the guide's DNA window and the closest window of the other allele; -1 when not computed; filled by `annotateDiscrimination`, helpers `windowOfSpacer`, `minMismatchesAgainst`) and `Engineered` (true for the spacer `applySubstitutionSpecialRule` builds, which is now returned first, followed by the unchanged ordinary list).

### `DiseaseVariants`
Finds candidate rsIDs for a disease (see `docs/runtime-contract.md` for the services and limits). Pure functions over JSON text:
`parseQuery` (name, OMIM number or MONDO id; bad input raises `GrnaInputException`), `parseMonarchSearch`, `parseMonarchEntity`,
`parseElinkHistory`, `parseEsearch`, `parseEsummary`, `spdiToHgvs` (ClinVar canonical SPDI to a genomic HGVS that `HGVS.HGVS`
accepts; `None` when it cannot be represented), `reviewStars`, `meetsSignificance`, and the exports `formatRsList`, `formatCsv`,
`formatTsv`, `formatJson`. The network is `DiseaseVariantClient(http, options, gate)` (`DiseaseVariantClient.Shared` is the
process-wide one): `FindDiseasesAsync text ct`, `ResolveOmimNumbersAsync disease includeSubtypes ct` and
`GetVariantsAsync omimNumbers minSignificance progress ct`, which returns a `DiseaseVariantResult` (`Variants`, `TotalMatching`,
`Truncated`). The `HttpClient` and the clock are injectable, so tests stub the services. A record's `RsId` is `"rs334"` with the prefix.

```fsharp
let client = gRNA.DiseaseVariants.DiseaseVariantClient.Shared
let! matches = client.FindDiseasesAsync("sickle cell disease", ct)
let! resolved = client.ResolveOmimNumbersAsync(matches.Head, false, ct)
let! result = client.GetVariantsAsync(resolved.OmimNumbers, gRNA.DiseaseVariants.PathogenicOrLikely, null, ct)
printf "%s" (gRNA.DiseaseVariants.formatRsList result.Variants)   // one rsID per line
```

### `SpacerFinder`
`scaffold`, `slidingWindow`, `calculateGCContent`, `calculateGCScore gc (lower, upper)` (inclusive bounds),
`countHomopolymers`, `getgRNAResult`, `getOrderedgRna` (the whole ranking, async), `applySubstitutionSpecialRule`,
`sortByResult`. Ranking key: `(Offtargets, -Energy, -GCScore, Homopolymers)`.

### `RNAFoldWrapper`
`fold seq ct`, `foldMany seqs ct`, `foldManyWith env seqs ct` (result: `{ Structure; Energy }`). Bounded to 2 concurrent
interpreters per process; sequences go as `ArgumentList` items, never through string interpolation.

### `BowtieWrapper` / `Services.BowtieService`
`BowtieService.ProcessMultipleSequencesAsync(sequences, mismatches, threads, ct)` returns one offtarget count per read.
It implements `IBowtieRunner`, the seam for substituting a stub. `FindOffTargetsAsync(window, mismatches, maxHits, ct)` returns the
alignments with loci (`Parsing.BowtieAlignment`: reference, 0-based offset, strand, mismatch count and detail) for one DNA window. Counts saturate at `-k 6`: **6 means "6 or more"**.

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

- `Repeat` parses; `Main` refuses it with a message (`GrnaInputException`) and `Sequence.GetMutatedSubsequence` itself still throws `NotImplementedException`.
- The stated reference base is checked against the fetched sequence by `Main.validateAgainstSequence`, but a wrong accession *version* that happens to carry the same base still yields a plausible but different sequence: pin the version.
- NCBI sequences are upper-cased by `parseFasta`, but IUPAC letters other than ACGT inside a window are not handled by the scoring or the folding step, which will fail for that window.
- `extraNucleotids` is `max 0 (spacer - mutation length)`. A deletion longer than the spacer leaves no window and is refused with a message; this is a limit of the design, not a solved case.
- Offtarget counts saturate at 6 (`-k 6`).
- The substitution special rule carries the pre-adjustment offtarget and fold values onto the adjusted sequence (re-running Bowtie and the folding for one changed base was judged redundant).
- Bowtie is serialised per process.
- `DiseaseVariants` reads at most 3,000 ClinVar records per search (`Truncated` says so), uses only germline classifications, expands subtypes one level (40 at most), and derives an HGVS only for simple A/C/G/T variants (not 3'-shifted: the same sequence results either way).
