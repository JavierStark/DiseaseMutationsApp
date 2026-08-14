## Biological Logic Report for Biologists: the gRNA Project

This document describes how a number of biological and bioinformatic concepts are implemented in the `gRNA` project. Its goal is to give a clear picture of how the science is translated into application logic, acting as a bridge between biology and software development.

### 1. HGVS Notation: the Language of Genetic Variants

**Scientific concept:**
HGVS notation (Human Genome Variation Society) is the gold standard for describing variations in DNA, RNA, and protein sequences. It allows precise, unambiguous communication about mutations such as substitutions, deletions, or insertions. For example, `NG_012345.1:c.76A>T` describes a substitution at position 76 of a gene's coding sequence, where an Adenine (A) has been replaced by a Thymine (T).

**Implementation:**
In `gRNA/HGVS.fs`, a parser interprets this notation. The code breaks an HGVS string down into its fundamental components: the sequence accession number, the mutation type, the affected positions, and the nucleotides involved.

```fsharp
// In gRNA/HGVS.fs

type MutationType =
    | Substitution
    | Deletion
    | Insertion
    | Duplication
    // ... other types

type HGVS(hgvs: string) =
    member this.Parse() =
        let parts = hgvs.Split(':')
        let accession = parts.[0]
        let mutation = parts.[1]
        // Logic to extract type, position and change
        // ...
```

This module is essential for the application to understand the user's initial request: a specific genetic variant.

### 2. SNP (Single Nucleotide Polymorphism): Common Variations

**Scientific concept:**
A SNP is a variation at a single nucleotide position in the genome. It is the most common type of genetic variation between individuals, often identified by an `rs` number (Reference SNP cluster ID). The application uses these `rs` identifiers to find the associated HGVS notations, connecting a common variation to its formal genetic description.

**Implementation:**
`gRNA/SNP.fs` talks to NCBI's dbSNP database to translate an `rs` ID into its corresponding HGVS notations. This lets the system work with a standardized format starting from a SNP identifier.

```fsharp
// In gRNA/SNP.fs

module SNP =
    let getHgvsNotationsAsync (rsNumber: string) =
        async {
            let url = $"https://api.ncbi.nlm.nih.gov/variation/v0/refsnp/{rsNumber}"
            // ... logic to make the web request and parse the JSON response
            // HGVS notations are extracted from the result.
        }
```

### 3. DNA/RNA Sequence Manipulation

**Scientific concept:**
The core of genomics is DNA sequence manipulation. Once a mutation is understood via HGVS, it needs to be applied to a reference sequence to obtain the mutated sequence. This process simulates the effect of the genetic variant on the DNA.

**Implementation:**
`gRNA/Sequence.fs` contains the logic that applies these mutations. `GetMutatedSubsequence` takes a reference sequence and an HGVS object and returns the altered sequence according to the described mutation (substitution, deletion, etc.), together with a context of flanking nucleotides.

```fsharp
// In gRNA/Sequence.fs

module Sequence =
    let complementary (sequence: string) =
        // Maps A<->T and C<->G; any other character (e.g. N) passes through unchanged.

    type Sequence(id: string, data: string) =
        member this.GetMutatedSubsequence(hgvs: HGVS, ?leftPadding: int, ?rightPadding: int) =
            // ... logic to apply each mutation type, clamping left/right padding at the
            // sequence boundaries so a mutation near the start or end never overruns the data.
            match hgvs.Mutation with
            | MutationType.Substitution ->
                // Replaces the reference nucleotide with the alternate one
            | MutationType.Deletion ->
                // Removes the nucleotides at the indicated positions
            // ... etc.
```

### 4. Search and Optimization of gRNA Spacers: the Heart of CRISPR

**Scientific concept:**
Designing an effective gRNA is the most critical step of a CRISPR experiment. The "spacer" — that ~20-28 nucleotide sequence that guides the Cas13 protein — must meet several criteria to ensure high cutting efficiency at the intended site while minimizing off-target effects. A suboptimal design can lead to low editing efficiency or unwanted mutations elsewhere in the genome.

Our strategy centers on a multi-factor analysis for every possible spacer, evaluating both its intrinsic properties and its predicted behavior in the genome.

**Implementation (`gRNA/SpacerFinder.fs`):**

The `SpacerFinder` module is the application's main engine. It orchestrates a multi-step process to identify the best gRNA candidates from a DNA sequence containing a mutation of interest.

#### 4.1. Candidate Generation: the Sliding Window

The first step is generating all possible spacers. This is done with a "sliding window" (`slidingWindow`) technique that scans the mutated DNA sequence, extracting subsequences of the configured gRNA length (the "spacer size", user-configurable, 20-28 nt in typical use).

> **IMPORTANT: Nature of the Generated Spacer Sequence**
>
> It is essential to understand the transformation the original DNA sequence undergoes to become a gRNA spacer:
>
> 1. **Original strand (sense DNA):** We start from a subsequence of the DNA strand that contains the mutation (e.g. `5'-AGCT...-3'`).
> 2. **Complementary strand (antisense DNA):** The code computes the strand complementary to this subsequence (e.g. `3'-TCGA...-5'`).
> 3. **Reversal and transcription to RNA:** The complementary DNA strand is then **reversed** (read 5'→3') and Thymine (T) is replaced with Uracil (U).
>
> **The final sequence returned by the application is an RNA sequence (with 'U') that is the reverse complement of the original target DNA strand.** This is required for the gRNA to correctly hybridize (bind) to the DNA sequence intended for cutting.

```fsharp
// In gRNA/SpacerFinder.fs

let slidingWindow (input: string) (windowSize: int) =
    [ for i in 0 .. input.Length - windowSize -> input.Substring(i, windowSize) ]

// Inside getOrderedgRna, each DNA window becomes a spacer via:
//   subsequence |> Sequence.complementary |> reverse |> _.Replace('T', 'U')
```

For every position in the sequence, a spacer candidate is generated. This exhaustive candidate set is the raw material for filtering and scoring.

#### 4.2. Scoring and Filtering: a Multi-Factor Approach

Each candidate spacer goes through a series of bioinformatic evaluations. The results are stored in a `gRNAResult` record, which encapsulates all information relevant to a candidate.

```fsharp
// In gRNA/SpacerFinder.fs

type gRNAResult =
    { Sequence: string              // The spacer sequence (RNA, with U instead of T)
      GCScore: float                // Score based on GC content
      GCContent: float              // Raw GC percentage
      HomopolymerCount: int         // Number of homopolymer runs (e.g. AAAA)
      SeedRegion: string            // Substring of Sequence covering the configured seed range
      Allignments: int              // Number of genomic alignments (off-targets)
      RnaFoldResult: RNAFoldResult  // RNA folding result (structure and energy)
      Rank: int                     // Group rank after sorting (ties share a rank)
      Score: float                  // Final normalized score (0 to 1)
      MutationHighlightStart: int   // Local index of the mutation within the spacer, or -1
      MutationHighlightLength: int }
```

The key evaluation criteria are:

**a) Guanine-Cytosine (GC) content:**
* **Concept:** The stability of the gRNA-target DNA duplex is influenced by GC content. Too low a content can result in unstable binding, while too high a content can hinder Cas13 dissociation after cutting. The generally accepted optimal range is **40-60%**.
* **Implementation:** `calculateGCScore` assigns a score of 1.0 when the GC content is strictly inside the ideal range (`lower < GC% < upper`). Outside that range — including exactly at the 40% or 60% boundary — the score falls back to a value proportional to the distance from the ideal.

```fsharp
// In gRNA/SpacerFinder.fs

let calculateGCScore (gcContent: float) (lowerThreshold: float, upperThreshold: float) =
    if gcContent < upperThreshold && gcContent > lowerThreshold then
        1.0
    else if gcContent < lowerThreshold then
        gcContent / lowerThreshold
    else
        (100.0 - gcContent) / (100.0 - upperThreshold)
```

**b) Presence of homopolymers:**
* **Concept:** Runs of four or more identical nucleotides (e.g., `AAAA` or `GGGG`) can cause premature termination of gRNA transcription by RNA polymerase III and have been shown to reduce CRISPR efficiency.
* **Implementation:** `countHomopolymers` uses a regular expression to count occurrences of these problematic runs in the RNA alphabet (`A`, `C`, `G`, `U`). A lower count is better.

**c) Off-target analysis with Bowtie:**
* **Concept:** For CRISPR therapy to be safe, the gRNA must be highly specific to the target sequence. Off-target analysis searches the whole genome for sequences similar to the candidate spacer that could be cut by mistake. Bowtie is an ultra-fast bioinformatic tool for aligning short sequences against a reference genome.
* **Implementation:** Bowtie is invoked on the raw DNA window (not the RNA spacer) allowing up to 2 mismatches, with up to 6 reported alignments per sequence (`-v 2 -k 6`). An alignment count (`Allignments`) of 1 is ideal, indicating a unique spacer; a higher count severely penalizes the candidate.

**d) gRNA secondary structure:**
* **Concept:** The complete gRNA (spacer + scaffold) must fold into a functional structure to bind Cas13. If the spacer itself folds into a stable secondary structure (hairpins, loops), it can interfere with its function. `RNAFold` (ViennaRNA) is used to predict the structure and its minimum free energy (MFE).
* **Implementation:** Folding is run on the **complete RNA gRNA** — the 36 nt scaffold followed by the RNA spacer (`scaffold + spacer`, both already in RNA form) — not on the raw DNA window. An energy value (`RnaFoldResult.Energy`) closer to zero (less negative) is preferable, since it indicates a less stable, more accessible structure.

#### 4.3. Sorting and Final Ranking

Once every candidate has been evaluated, they need to be sorted to present the most promising ones to the user. Sorting is a lexicographic process that prioritizes the most important factors first.

* **Implementation:** `sortByResult` defines the sort key. gRNAs are ordered from best to worst according to the following tuple:

```fsharp
// In gRNA/SpacerFinder.fs

let sortByResult (result: gRNAResult) =
    (result.Allignments, -result.RnaFoldResult.Energy, -result.GCScore, result.HomopolymerCount)
```

This translates into the following priority order:
1. **Fewest `Allignments` (off-targets):** specificity comes first. A gRNA with 1 alignment is always better than one with 2, regardless of the other factors.
2. **Highest `Energy` (least negative):** among gRNAs with the same off-target count, the one with the least stable secondary structure (energy closer to 0) is preferred. The negative of the energy is used so ascending sort places the least-negative values first.
3. **Highest `GCScore`:** all else equal, the candidate with GC content closest to ideal is preferred.
4. **Lowest `HomopolymerCount`:** finally, the presence of homopolymers is penalized.

After sorting, candidates with an identical sort key are grouped into the same `Rank` (ties share a rank; the next distinct group gets the next rank number). A final normalized `Score` from 0 to 1 is then assigned to every gRNA based on its rank relative to the total number of groups, where the best group scores 1.0. This gives the user a simple, quantitative metric to quickly compare the relative quality of the different gRNAs.

This rigorous selection process ensures the gRNAs recommended by the application are not only capable of targeting the mutation of interest, but do so with the highest possible efficiency and safety.

#### 4.4. Special Rule for Substitution SNPs

**Concept:**
CRISPR editing efficiency can be improved if the mutation being introduced (or corrected) itself helps prevent the CRISPR machinery from re-cutting the DNA once it has been repaired. This is especially relevant for substitution SNPs. If the gRNA is designed so that one of its key nucleotides overlaps the mutation position, a mismatch can be created against the original (wild-type) sequence while retaining a perfect match against the repaired (mutated) sequence.

One known strategy is to force a mismatch in the gRNA's "seed region". A more subtle, sometimes preferred technique is to introduce a mismatch near (but not inside) the seed, close to the spacer's 3' end. If the substitution SNP lands at the right spot near the 3' end, we can deliberately alter a nearby gRNA nucleotide to engineer a mismatch against the wild-type sequence.

**Implementation:**
The system implements a special rule to capitalize on this strategy — **and it is applied only when the HGVS mutation type is `Substitution`** (checked in `Main.fs` before calling `applySubstitutionSpecialRule`; other mutation types skip it entirely). Using 0-based indices into a spacer of length `N` (`windowSize`), the rule uses two distinct positions, three bases apart, both counted from the 3' end:

1. **Detection (index `N-3`):** the system looks, among the already-ranked candidates, for the one whose mutation highlight starts exactly at local index `N-3` — the 3rd base from the spacer's 3' end (index `N-1` is the last/3'-most base, `N-2` the 2nd from the end, `N-3` the 3rd).
2. **Modification (index `N-4`):** if such a spacer is found, a modified version is created. `adjustFourthFromEndToAorU` flips the nucleotide at index `N-4` — one position further from the 3' end than where the mutation itself sits — changing it to `A` or `U` (whichever it was not originally). Its GC content, GC score, homopolymer count, and seed region are then recomputed from the adjusted sequence, while the alignment count and RNA-fold result are carried over unchanged from the original candidate (re-running Bowtie/RNAFold on the single-base-adjusted sequence would be redundant work for a metric that will be overridden anyway).
3. **Prioritization — replaces the whole list:** this modified spacer is assigned the highest priority: `Rank = 1` and `Score = 1.0`. **This is a deliberate design choice, not an omission:** `applySubstitutionSpecialRule` returns **only** this single candidate (`Option.toList` around an `Option.map`/`List.tryFind` — an empty list if no matching candidate exists, otherwise a list of exactly one). The full ranked candidate table for that HGVS variant is discarded; the UI table for a substitution variant shows either one row or zero rows, never the full ranked list. The logic assumes the advantage of introducing this mismatch outweighs the other scoring criteria (GC, homopolymers, etc.) for this specific candidate.

```fsharp
// In gRNA/SpacerFinder.fs

let adjustFourthFromEndToAorU (sequence: string) =
    // ...
    let targetIndex = sequence.Length - 4
    let replacement = if sequence.[targetIndex] = 'A' then 'U' else 'A'
    // ...

let applySubstitutionSpecialRule (seedStart: int) (seedEnd: int) (windowSize: int) (results: gRNAResult list) =
    let targetMutationIndex = windowSize - 3
    results
    |> List.tryFind (fun result -> result.MutationHighlightStart = targetMutationIndex && result.MutationHighlightLength > 0)
    |> Option.map (fun selected ->
        let adjustedSequence = adjustFourthFromEndToAorU selected.Sequence
        // ... a new gRNAResult is built from the adjusted sequence, Rank = 1, Score = 1.0
        )
    |> Option.toList
```

This automatic rule offers the researcher a potentially more effective gRNA for editing substitution SNPs, using the mutation itself to improve the specificity of the process — at the cost of no longer presenting the full ranked alternative list for that variant.

### 5. Alignment with Bowtie: Off-Target Analysis

**Scientific concept:**
See section 4.2(c) above for the underlying concept.

**Implementation:**
`gRNA/BowtieWrapper.fs` is a wrapper around the Bowtie executable. It runs every candidate spacer (the raw DNA window) through Bowtie and counts the alignments found in the human genome. A low alignment count (ideally 1) indicates high specificity. The wrapper resolves the reference index automatically from the first `.bt2`/`.bt2l` file it finds under `bowtie/indexes/` (see the Prerequisites section of `README.md` for how those files are provisioned) and runs with 2 threads.

```fsharp
// In gRNA/BowtieWrapper.fs

module BowtieWrapper =
    let runBowtie (mismatches: int) (threads: int) (sequence: string) (cancellationToken: CancellationToken) =
        // Builds and executes the Bowtie command line, e.g.:
        // bowtie-align-s -x <genome_index> -c <sequences> -v <mismatches> -k 6 --threads <threads>
        // Parses the output to count alignments per sequence
```

### 6. Secondary Structure Prediction with RNAFold

**Scientific concept:**
The gRNA molecule can fold onto itself, forming secondary structures. A very stable structure can prevent the gRNA from correctly binding to the Cas13 protein or the target DNA sequence, reducing its efficacy. RNAFold (from the ViennaRNA package) predicts the most likely secondary structure of an RNA molecule and its minimum free energy (MFE). A very low (very negative) MFE indicates a very stable structure and therefore a potentially less effective gRNA.

**Implementation:**
`gRNA/RNAFoldWrapper.fs` interacts with RNAFold via a Python subprocess (`python3 -c "import RNA; print(RNA.fold(...))"`, falling back to `python`). For each candidate, it obtains the predicted structure in dot-bracket notation and its energy value, folding the **complete gRNA** (`scaffold + spacer`, both in RNA form). This value is one of the factors used to score and rank candidate gRNAs.

```fsharp
// In gRNA/RNAFoldWrapper.fs

module RNAFoldWrapper =
    type RNAFoldResult = { Structure: string; Energy: float }

    let fold (sequence: string) =
        // Calls a Python script that uses the ViennaRNA library
        // to compute the structure and energy of the RNA sequence.
        // Parses the result to extract structure and energy.
```

### 7. Levenshtein Distance: Measuring Similarity

> **Status: not compiled into the application.** `gRNA/LevenshteinDistance.fs` exists in the repository but is listed as `<None Include="LevenshteinDistance.fs" />` in `gRNA/gRNA.fsproj`, so it is not part of the build and is not used by any currently active feature. It only matters as a dependency of the disabled OMIM integration described in section 8.

**Scientific concept:**
Levenshtein distance is a metric measuring the difference between two strings, defined as the minimum number of single-character edits (insertions, deletions, or substitutions) required to change one string into the other. In this project it would be used to compare disease phenotype names and group relevant allelic variants.

**Implementation (inactive):**
`gRNA/LevenshteinDistance.fs` contains an implementation of the algorithm, intended to help decide whether an allelic variant found in the OMIM database is related to the disease phenotype under investigation.

```fsharp
// In gRNA/LevenshteinDistance.fs

module Levenshtein =
    let levenshteinDistance (s1: string) (s2: string) =
        // Dynamic-programming implementation of the edit-distance algorithm.
```

### 8. Disease Mapping with OMIM

> **Status: disabled.** OMIM integration is fully implemented in code but currently switched off across the stack: `gRNA/Omim.fs` is excluded from the F# build (`<None Include="Omim.fs" />` in `gRNA/gRNA.fsproj`), `GrnaService.GetRsFromOmim` is commented out, the `/omim-to-rs` page and its navigation menu entry are commented out, and `AppStateService`'s OMIM-related state fields are commented out. The reason is that OMIM's site now sits behind a Cloudflare CAPTCHA that blocks the scraper used here (see git history for the change that disabled it). The description below documents the intended design for when/if the integration is re-enabled; it does not describe current, working behavior.

**Scientific concept:**
OMIM (Online Mendelian Inheritance in Man) is a comprehensive database of human genes and genetic phenotypes. The application would use it to find genetic variants (SNPs with an `rs` ID) associated with a specific disease, identified by its MIM number.

**Implementation (inactive):**
`gRNA/Omim.fs` performs web scraping on the OMIM site. Starting from a MIM number, it would navigate the "Phenotype-Gene Relationships" and "Allelic Variants" tables to extract the relevant `rs` IDs, using Levenshtein distance to filter by phenotype similarity.

```fsharp
// In gRNA/Omim.fs

module Omim =
    let rsFromOmim (mimNumber: string) (diseaseName: string) =
        async {
            // 1. Fetches the OMIM page for the MIM number.
            // 2. Extracts the genes associated with the phenotype.
            // 3. For each gene, visits the allelic variants table.
            // 4. Filters variants by phenotype similarity using Levenshtein distance.
            // 5. Extracts and returns the rs IDs.
        }
```

### Main Workflow

`gRNA/Main.fs` orchestrates the whole process, connecting the modules above into a cohesive workflow. Its public entry point, `getBestgRNAFromHGVS`, takes an HGVS string, the spacer size, the seed region bounds (`seedStart`/`seedEnd`), a `BowtieService`, a cancellation token, and a `complement` flag, and returns a `ResultFromHGVS` record:

```fsharp
// In gRNA/Main.fs

type ResultFromHGVS = {
    gRNA: SpacerFinder.gRNAResult list          // Ranked candidates for the mutated sequence
    originalGRNA: SpacerFinder.gRNAResult list  // Ranked candidates for the original (wild-type) sequence
    mutatedSequence: string
    originalSequence: string
    extraNucleotids: int                        // Flanking context length used on each side
}
```

1. **Input:** receives an HGVS notation, a spacer size, and a seed region.
2. **Parsing:** uses `HGVS.fs` to interpret the mutation.
3. **Sequence:** uses `SequenceRepository.fs` to fetch the reference sequence from NCBI (cached in-process per accession).
4. **Mutation:** uses `Sequence.fs` to build both the mutated and the original (wild-type) subsequence, with flanking context on each side. When `complement` is requested, both sequences are complemented (not reverse-complemented) before spacer generation — this powers the app's complement-strand analysis mode.
5. **gRNA search:** uses `SpacerFinder.fs` to generate and score all candidate gRNAs **for both the mutated and the original sequence**, coordinating calls to `BowtieWrapper.fs` and `RNAFoldWrapper.fs`.
6. **Substitution rule:** if the HGVS mutation type is `Substitution`, `applySubstitutionSpecialRule` is applied to both candidate lists (see section 4.4) — each list is replaced by its single mismatch-engineered candidate, or emptied if no candidate matches.
7. **Output:** returns the two ranked gRNA lists (mutated and original), both sequences, and the flanking-context length, so the UI can render the original-vs-mutated comparison and both result tables side by side.

This modular, science-grounded approach lets the application design efficient, specific gRNAs for editing genes associated with disease.
