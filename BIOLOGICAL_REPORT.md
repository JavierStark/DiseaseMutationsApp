## Biological Logic Report for Biologists: the gRNA Project

This document describes how a number of biological and bioinformatic concepts are implemented in the `gRNA` project. Its goal is to give a clear picture of how the science is translated into application logic, acting as a bridge between biology and software development.

### 1. HGVS Notation: the Language of Genetic Variants

**Scientific concept:**
HGVS notation (Human Genome Variation Society) is the gold standard for describing variations in DNA, RNA, and protein sequences. It allows precise, unambiguous communication about mutations such as substitutions, deletions, or insertions. For example, `NC_000017.11:g.7674220C>T` describes a substitution at position 7,674,220 of the chromosome 17 reference, where a Cytosine (C) has been replaced by a Thymine (T).

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

**What is accepted and what is refused.** The parser is strict, because a variant that is silently misread produces a convincing but wrong design:

* The code must be `ACCESSION:TYPE.CHANGE`; a missing colon, a position without a change, a position of 0 or beyond the 32-bit range, a reversed range (`10_5del`), and a substitution that changes nothing (`C>C`) are rejected with a message (CLI exit code 2).
* Alleles must be upper-case `A`, `C`, `G` or `T` (no IUPAC codes, no lower case).
* Before any design, the variant is checked against the sequence that NCBI returns (`Main.validateAgainstSequence`): the position must lie inside the accession, an insertion needs two adjacent positions, and **the stated reference base must equal the base actually at that position**. `NC_000017.11:g.7674220A>T` is refused because base 7,674,220 is `C`.
* Only genomic (`g.`), mitochondrial (`m.`) and non-coding (`n.`) positions are designed, because they index the fetched sequence directly. **`c.` (coding-sequence) positions are converted only on a transcript record** (`NM_`, `XM_`; changed 2026-10-16): an mRNA has no introns, so `c.N` is simply position `cdsStart + N - 1` of the transcript (`c.-N` is `cdsStart - N`, `c.*N` is `cdsEnd + N`), with `cdsStart`/`cdsEnd` read from the single-range `CDS` feature of the GenBank record (`SequenceRepository.parseCdsRange`; a split or reverse CDS is refused). `HGVS.convertCodingToTranscript` rewrites `NM_000518.5:c.20A>T` as `NM_000518.5:n.70A>T` before parsing, the stated reference base is then checked against the real sequence as for any `n.` variant, and a notice records the conversion. The design is made on the mRNA itself, which is what Cas13 binds. Intronic offsets (`c.92+2`) do not exist in an mRNA and are refused; `c.` on a genomic record (`NG_`, `NC_`) is refused with the advice to use the `NM_` accession, because introns lie between the exons and the exon map is not read. A `c.` position that reaches `validateAgainstSequence` unconverted is always refused, so it is never used as a plain index. Repeat (`[n]`), conversion and translocation notation parse but are refused for design.
* The spacer size must be between 18 and 60 nt and the seed range must lie inside the spacer.

### 2. SNP (Single Nucleotide Polymorphism): Common Variations

**Scientific concept:**
A SNP is a variation at a single nucleotide position in the genome. It is the most common type of genetic variation between individuals, often identified by an `rs` number (Reference SNP cluster ID). The application uses these `rs` identifiers to find the associated HGVS notations, connecting a common variation to its formal genetic description.

**Implementation:**
`gRNA/SNP.fs` talks to NCBI's dbSNP database to translate an `rs` ID into its corresponding HGVS notations. This lets the system work with a standardized format starting from a SNP identifier.

*What is returned (changed 2026-10-16).* The dbSNP answer for one rsID lists every alternative allele on several RefSeqGene (`NG_`) records. `rs334`, for example, gives 4 records x 3 alleles = 12 notations, of which only one (`NG_000007.3:g.70614A>T`, HBB Glu6Val) is the sickle-cell variant. The response is now parsed as JSON (`SNP.parseVariants`) instead of scanning it for text patterns, and each notation carries what dbSNP knows about it:

* **Default selection (`Preferred`):** the main RefSeqGene record only (the first `NG_` record whose reference base equals that of the mRNA `NM_` record at this position; dbSNP lists the primary record first), and, when dbSNP names any pathogenic or likely-pathogenic allele, only those alleles. If the default would be empty, everything is returned, never nothing. This is a heuristic: the web app has a "Show all alleles and gene records" box and the CLI has `--all-alleles` to get the full list.
* **Gene and orientation:** the gene symbol and whether the gene is read along the `+` strand of that accession (the gene's orientation on the chromosome combined with the record's `is_aln_opposite_orientation`). See section 4.1.2.
* **Significance:** the ClinVar significances dbSNP lists for the allele.

```fsharp
// In gRNA/SNP.fs
type SnpVariant = { Hgvs; Accession; Gene; GeneOnPlusStrand; Significance; Preferred }
let parseVariants (json: string) : SnpVariant list      // pure, tested on a captured rs334 response
let getHgvsNotationsAsync rs ct      // default: Preferred only
let getAllHgvsNotationsAsync rs ct   // every notation
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

An **inversion** (`inv`) is written into the sequence as the **reverse complement** of the segment, as the HGVS recommendations define it (`GTA` becomes `TAC`). An earlier version only reversed it, which is a different sequence.

### 4. Search and Optimization of gRNA Spacers: the Heart of CRISPR

**Scientific concept:**
Designing an effective gRNA is the most critical step of a CRISPR experiment. The "spacer" — that ~20-28 nucleotide sequence that guides the Cas13 protein — must meet several criteria to ensure high cutting efficiency at the intended site while minimizing off-target effects. A suboptimal design can lead to low editing efficiency or unwanted mutations elsewhere in the genome.

Our strategy centers on a multi-factor analysis for every possible spacer, evaluating both its intrinsic properties and its predicted behavior in the genome.

**Implementation (`gRNA/SpacerFinder.fs`):**

The `SpacerFinder` module is the application's main engine. It orchestrates a multi-step process to identify the best gRNA candidates from a DNA sequence containing a mutation of interest.

#### 4.1. Candidate Generation: the Sliding Window

The first step is generating all possible spacers. This is done with a "sliding window" (`slidingWindow`) technique that scans the mutated DNA sequence, extracting subsequences of the configured gRNA length (the "spacer size", user-configurable between 18 and 60 nt, 28 by default). A mutated region shorter than the spacer has no window; the application then reports that instead of searching.

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

#### 4.1.1. The two strands: "Normal" and "Complement" results

Every variant can be designed against either strand of the reference; the result tables and the CSV `Strand` column say which.

* **Normal (+ strand).** The window is read 5'→3' on the reference strand. The spacer is the reverse complement of the window, so it hybridises to the reference strand. Bowtie is asked where that window occurs in the genome.
* **Complement (− strand).** Both the original and the mutated sequence are first replaced by their **reverse complement**, i.e. the opposite strand read 5'→3', and then go through exactly the same steps. The windows, the spacers, the Bowtie query and the folding therefore all describe real 5'→3' molecules of the opposite strand; the spacer of a complement result equals the corresponding stretch of the reference strand written as RNA. The position of the mutation is mirrored accordingly (`Main.mirrorSpan`).

*History, so that older reports can be read correctly.* Until 2026-10-13 the complement mode complemented the sequence without reversing it, which gave a spacer equal to the reverse of the reference strand and a Bowtie query that is a sequence on neither strand: its alignment count was 0 for the TP53 example where the normal strand gives 1. Reports made before that date contain complement rows that should be regenerated.

#### 4.1.2. Which tab binds the mRNA? (added 2026-10-16)

Cas13 cuts RNA, so a spacer is useful for knock-down or RNA detection only if it is **complementary to the gene's mRNA**. The mRNA has the sequence of the strand the gene is read along. The "Normal" tab designs the spacer as the reverse complement of the `+` strand of the accession, so it binds the mRNA when the gene is read along `+`, and the "Complement" tab does when the gene is read along `-` (`HgvsData.TargetsMrna`).

* For an rsID the orientation comes from dbSNP (section 2). `NG_` records are normally stored in gene orientation, so for `NG_` notations the Normal tab is the one that binds the mRNA. HBB, TP53 and PAH are on the minus strand of their chromosome, so for `NC_` coordinates it is the Complement tab.
* The result tabs say so ("mRNA" or "antisense" badge, gene symbol), the mRNA-binding tab is listed first, and the antisense tab carries a warning.
* A typed HGVS has no orientation information: the page says it is unknown and that the user must check the strand. Looking the orientation up for typed coordinates is future work.

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
* **Concept:** The stability of the gRNA-target DNA duplex is influenced by GC content. Too low a content can result in unstable binding, while too high a content can hinder Cas13 dissociation after cutting. The range used is **40-60%**. *[Needs source: no Cas13-specific GC range was found; 40-60% is reported for DNA-targeting guide design. Treat it as a borrowed heuristic until a Cas13 reference is added.]*
* **Implementation:** `calculateGCScore` assigns a score of 1.0 when the GC content is inside the ideal range, **boundaries included** (`lower <= GC% <= upper`). Outside that range the score falls back to a value proportional to the distance from the ideal (`gc/40` below, `(100-gc)/40` above), so it always stays within 0-1. (Earlier versions treated the bounds as exclusive, which made exactly 40% GC score 1.5 and outrank a perfect 50% candidate; fixed with boundary tests.)

```fsharp
// In gRNA/SpacerFinder.fs

let calculateGCScore (gcContent: float) (lowerThreshold: float, upperThreshold: float) =
    if gcContent <= upperThreshold && gcContent >= lowerThreshold then
        1.0
    else if gcContent < lowerThreshold then
        gcContent / lowerThreshold
    else
        (100.0 - gcContent) / (100.0 - upperThreshold)
```

**b) Presence of homopolymers:**
* **Concept:** Runs of four or more identical nucleotides (e.g., `AAAA` or `GGGG`) can cause premature termination of gRNA transcription by RNA polymerase III and have been shown to reduce CRISPR efficiency. *[Team decision, 2026-10-13: runs of any of the four bases are counted, not only U. Be aware that the Pol III termination signal reported in the literature is a run of T (U in the RNA), at least four and reliably six, and only matters when the guide is expressed from a Pol III promoter; for A, C and G runs this report has no source yet.]*
* **Implementation:** `countHomopolymers` uses a regular expression to count occurrences of these problematic runs in the RNA alphabet (`A`, `C`, `G`, `U`). A lower count is better.

**c) Off-target analysis with Bowtie:**
* **Concept:** For CRISPR therapy to be safe, the gRNA must be highly specific to the target sequence. Off-target analysis searches the whole genome for sequences similar to the candidate spacer that could be cut by mistake. Bowtie is an ultra-fast bioinformatic tool for aligning short sequences against a reference genome.
* **Implementation:** Bowtie is invoked on the raw DNA window (not the RNA spacer) allowing up to 2 mismatches, with up to 6 reported alignments per sequence (`-v 2 -k 6`). An alignment count (`Allignments`) of 1 is ideal, indicating a unique spacer; a higher count severely penalizes the candidate. The count saturates at 6 (`-k 6`), which the tables show as "6+".

> **A count of 0 is ranked best, on purpose.** The ranking sorts the count in ascending order, so a window with no Bowtie hit (0) sorts ahead of a window with exactly one (1). This is the team's chosen rule and it is easy to misread: 0 does not mean "unique". It means the sequence of that window was not found in the reference within 2 mismatches. For a substitution the window is almost the reference, so it normally has at least one hit; for edited sequences a 0 is expected, because a window that spans an insertion or duplication junction has no counterpart in the genome. In the runs of 2026-10-13 on `NC_000017.11:g.7674220` (a 3-nt delins, a 1-nt insertion and a 3-nt duplication) the top-ranked candidates all had 0, so the alignment criterion did not separate them and the energy, GC and homopolymer criteria decided the order; a 3-nt inversion at the same place gave 1. Treat a 0 as "no genomic match for this edited sequence", not as proof of specificity.

**d) gRNA secondary structure:**
* **Concept:** The complete gRNA (spacer + scaffold) must fold into a functional structure to bind Cas13. If the spacer itself folds into a stable secondary structure (hairpins, loops), it can interfere with its function. `RNAFold` (ViennaRNA) is used to predict the structure and its minimum free energy (MFE).
* **Implementation:** Folding is run on the **complete RNA gRNA** — the 36 nt scaffold followed by the RNA spacer (`scaffold + spacer`, both already in RNA form) — not on the raw DNA window. An energy value (`RnaFoldResult.Energy`) closer to zero (less negative) is preferable, since it indicates a less stable, more accessible structure.

**e) Discrimination between the two alleles (added 2026-10-16):**
* **Concept:** for a diagnostic the guide must react to the mutated sequence and stay silent on the normal one. A guide whose DNA target differs from the normal sequence by 0 or 1 letter cannot be expected to do so. Earlier versions showed the two candidate lists separately and never said how a guide behaves on the *other* allele.
* **Implementation:** `SpacerFinder.annotateDiscrimination` gives every candidate `WtMismatches`: the fewest differing letters between the guide's DNA window (`windowOfSpacer`, the reverse complement of the spacer) and any same-length window of the **other** allele's sequence in the same orientation (a mutated-list guide against the original sequence, an original-list guide against the mutated one). Sliding over all windows makes this work for insertions and deletions, where the windows do not line up one-to-one. When the other sequence is shorter than the window, nothing can be compared and the whole window length is reported. It is computed after the substitution rule, so the engineered spacer is measured as it will be ordered. Checked by hand on cystic fibrosis F508del (junction guides: 6 to 8 mismatches; a guide lying in unchanged sequence: 1).
* **How it is used:** it is a column ("Mismatches vs other allele", sortable, with a filter), a red "not discriminating" chip when it is below 2, and a warning when no guide of a sequence reaches 2. **It does not change the ranking** (section 4.3). The threshold 2 is a working choice, not a literature value, and the metric ignores where in the spacer the mismatches fall. The CSV has `WT Mismatches` (empty when not computed).

**f) Scaffold hairpin and spacer accessibility (added 2026-10-16):** the single minimum free energy mixes two different things: the scaffold's own hairpin, which Cas13 needs, and the spacer's tendency to fold, which hinders binding to the target. Two checks are derived from the fold already computed for each complete gRNA, without changing the ranking:
* `ScaffoldIntact`: every base pair of the fold of the *scaffold alone* (computed once per process, `scaffoldReference`) is still present in the fold of the complete gRNA (`SpacerFinder.scaffoldIntact`, over `basePairs`). When it is not, the table shows a "hairpin broken" chip: the spacer pulls the hairpin apart and the guide may not load. Extra pairs between the scaffold and the spacer are not penalised by this check.
* `SpacerUnpaired`: the share of the spacer's letters that are unpaired (`.`) in the fold of the complete gRNA (`SpacerFinder.spacerUnpaired`). It is a proxy for accessibility *of the guide*; accessibility of the *target RNA site*, which also matters for Cas13, is not modelled.
For the engineered spacer both values are carried over from the candidate it was built from (the fold is not recomputed, as for the energy and the alignments). The MFE (Turner 2004 parameters) is a prediction, not a measurement.

**g) Configurable complete gRNA (adapters and custom flanks, added 2026-10-18):** the construct that is copied, folded on demand, shortlisted and exported is `[BioBricks 5'] [Frida 5'] [custom 5'] [DR] spacer [custom 3'] [Frida 3'] [BioBricks 3']` (`GuideConstruct`). The DR is a selector (LwaCas13a, the 36 nt scaffold above, or none). The two adapters are our own sequences, taken as given: Frida `AGAGCGTCTCTAAAC` / `GCTTAGAGACGTACT` and BioBricks `GGCTAGCGAATTCGCGGCCGCTTCT` / `AGTAGCGGCCGCTGCAGCGATCGT`; the BioBricks one is always outermost when both are on, and either side of a user-supplied custom flank may be empty. The sequences visibly contain recognition sites of cloning enzymes (a BsmBI site `CGTCTC` in Frida; EcoRI, NotI and PstI in BioBricks, and XbaI and SpeI where the two adapters meet), so the restriction scan reports only sites beyond those the chosen adapters bring (counted on both strands). The adapters were not tested for function here, and whether they leave Cas13 processing of the DR and spacer intact is not assessed by the program. The fold of the full construct is computed only when the user asks for it (**Get structure**); the table's `ScaffoldIntact`, `SpacerUnpaired` and energy are still those of DR + spacer and do not model adapters. The built guide can be exported as FASTA (header with the bare spacer and the construct recipe, DNA or RNA letters) in addition to the oligo order CSV.

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

After sorting, candidates with an identical sort key are grouped into the same `Rank` (ties share a rank; the next distinct group gets the next rank number). A final normalized `Score` from 0 to 1 is then assigned to every gRNA based on its rank relative to the total number of groups, where the best group scores 1.0. This gives the user a simple metric to quickly compare the *relative* order of candidates of one variant. **It is a rank percentile, not a prediction of guide activity**, which is why (changed 2026-10-16) the table explains it in a tooltip and shows "only candidate" instead of 100 % when a variant has a single ordinary candidate. The CSV still carries the number. The fixed choices behind the numbers (GC range 40-60, the mismatch positions, the scaffold length and the off-target reference) are written in the provenance line of every report.

This rigorous selection process ensures the gRNAs recommended by the application are not only capable of targeting the mutation of interest, but do so with the highest possible efficiency and safety.

#### 4.4. Special Rule for Substitution SNPs

**Concept:**
The intent of the rule is to make a guide more sensitive to a single-base difference between the two alleles of a substitution, by adding one deliberate ("synthetic") mismatch next to the base where the alleles differ, close to the 3' end of the spacer. *[Needs source: the literature that supports synthetic mismatches at these positions, and the choice of `A`/`U` as the replacement, has not yet been added to this report.]*

What the code actually produces, stated exactly because it is easy to read as something stronger: the spacer is designed against the **mutated** sequence, and the rule then changes one more base of it (index `N-4`). The engineered spacer therefore has **one** mismatch against the mutated target (the synthetic one) and **two** against the wild-type target (the synthetic one and the mutation itself at `N-3`). It is not a perfect match to either allele.

**Implementation:**
The system implements a special rule to capitalize on this strategy — **and it is applied only when the HGVS mutation type is `Substitution`** (checked in `Main.fs` before calling `applySubstitutionSpecialRule`; other mutation types skip it entirely). Using 0-based indices into a spacer of length `N` (`windowSize`), the rule uses two distinct positions, three bases apart, both counted from the 3' end:

1. **Detection (index `N-3`):** the system looks, among the already-ranked candidates, for the one whose mutation highlight starts exactly at local index `N-3` — the 3rd base from the spacer's 3' end (index `N-1` is the last/3'-most base, `N-2` the 2nd from the end, `N-3` the 3rd).
2. **Modification (index `N-4`):** if such a spacer is found, a modified version is created. `adjustFourthFromEndToAorU` flips the nucleotide at index `N-4` — one position further from the 3' end than where the mutation itself sits — changing it to `A` or `U` (whichever it was not originally). Its GC content, GC score, homopolymer count, and seed region are then recomputed from the adjusted sequence, while the alignment count and RNA-fold result are carried over unchanged from the original candidate (re-running Bowtie/RNAFold on the single-base-adjusted sequence would be redundant work for a metric that will be overridden anyway).
3. **Prioritization — engineered spacer first, the rest follows (changed 2026-10-16):** this modified spacer is assigned `Rank = 1`, `Score = 1.0` and `Engineered = true`, and `applySubstitutionSpecialRule` returns it **followed by the whole ordinary ranked list, unchanged** (the candidate it was built from stays in the list in its unmodified form). In the web app the engineered row is always listed first, whatever the sort, with a coloured row and an "Engineered" chip; the other rows keep their own ranks and scores, so two rows can both show rank 1. The CSV has an `Engineered` column (`Yes`/`No`). Before this date the single engineered candidate *replaced* the list and the user could not see or choose an alternative. **When no candidate matches the rule cannot apply, and the ordinary ranking is returned unchanged with a warning** (decision of 2026-10-13). A match is impossible only near the ends of the accession: the mutation must be the 3rd base of a full window, so at least 2 bases are needed on one side and `spacer size - 3` (25 for 28 nt) on the other, i.e. a substitution at position 1 or 2 of the accession, or in its last 24 bases, on the normal strand; on the complement strand the sides swap. `Main.specialRuleNotice` words the warning (`ResultFromHGVS.notices`).

*Open scientific question.* The position choice is still unsourced. Gootenberg et al. (Science 2017, SHERLOCK) report the best discrimination with the variant at **position 3** of the spacer and the synthetic mismatch at **position 5**, i.e. two bases apart, which is not what the code does (variant at index N-3 from the 3' end, mismatch at N-4, adjacent). Which end the numbering starts from could not be checked in the supplement. Until the team confirms the convention, treat the engineered spacer as one option among the ranked ones and compare it with the ordinary candidates using the discrimination column.

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
    |> function Some adjusted -> adjusted :: results | None -> results   // engineered first, then the ordinary list
```

This automatic rule offers the researcher a potentially more effective gRNA for editing substitution SNPs, using the mutation itself to improve the specificity of the process, while still showing the full ranked alternatives.

### 5. Alignment with Bowtie: Off-Target Analysis

**Scientific concept:**
See section 4.2(c) above for the underlying concept.

**Implementation:**
`gRNA/BowtieWrapper.fs` is a wrapper around the Bowtie executable. It runs every candidate spacer (the raw DNA window) through Bowtie and counts the alignments found in the human genome. A low alignment count (ideally 1) indicates high specificity. The wrapper resolves the reference index from `GRNA_BOWTIE_INDEX` when set, otherwise from the first index file (sorted by path; `.bt2`, `.bt2l`, `.ebwt` or `.ebwtl`) under `bowtie/indexes/`, and runs with 2 threads. The image ships `GRCh38_noalt_as` as `.bt2` files, which `bowtie-align-s` 1.3.1 reads (checked by running it on 2026-10-13). The reads are written to a temporary file and passed with `-r` (one read per line, numbered in file order exactly as with `-c`): putting thousands of windows on the command line exceeded the operating system's argument-length limit for long variants.

```fsharp
// In gRNA/BowtieWrapper.fs

module BowtieWrapper =
    let runBowtie (mismatches: int) (threads: int) (sequence: string) (cancellationToken: CancellationToken) =
        // Builds and executes the Bowtie command line, e.g.:
        // bowtie-align-s -x <genome_index> -v <mismatches> -k 6 --threads <threads> --mm -r <reads_file>
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

### 7. From a disease to variants: the OMIM scraper (removed) and its replacement

An earlier version could start from an OMIM disease number, scrape the allelic-variant tables of omim.org to find `rs` identifiers, and use Levenshtein distance between phenotype names to filter them. OMIM's site now sits behind a CAPTCHA that blocks the scraper, so the feature was switched off and, on 2026-10-13, its code (`Omim.fs`, `LevenshteinDistance.fs`, a sample dbSNP response) was deleted; the git history keeps it.

The same need is now met from different sources (`gRNA/DiseaseVariants.fs`, the `/disease` page and `grna disease`). A disease name, OMIM number or MONDO id is resolved through the Monarch Initiative API (names, OMIM cross-references and subtypes), and the variants are the ClinVar records that NCBI links to those OMIM numbers, kept when ClinVar's germline classification is pathogenic or likely pathogenic (or any classification, on request). What this does and does not claim:

- The scientific content is ClinVar's: its classification and review status are shown as they are, nothing is re-interpreted, and the page says the list is a candidate list, not a clinical statement. ClinVar changes every month, so a count is only true on the day it was read.
- There is no phenotype-similarity filter any more (the Levenshtein step is gone): the filter is ClinVar's classification, and the link to the disease is NCBI's OMIM-to-ClinVar link, which follows any reference to the OMIM number in a record. Most linked records name the disease among their own conditions (on 2026-10-13: 922 of 923 for OMIM 261600, 1,490 of 1,517 for 219700, 65 of 80 for 603903, 6 of 8 for 143100), but some do not (a transthyretin variant appeared under Huntington disease), so each record's conditions are displayed and none is dropped silently. Records of haplotypes and compound alleles can share one rsID.
- An OMIM *phenotypic series* (Fanconi anemia) has no numeric OMIM number of its own, so the numbers of its direct subtypes are used (up to 40). Searches stop at 3,000 records and say so.
- Where ClinVar has no rsID, a genomic HGVS on GRCh38 is derived from its canonical SPDI (substitutions, deletions, insertions, duplications and deletion-insertions of A/C/G/T only). It is left-aligned rather than 3'-shifted, which yields the same sequence; Diana still checks the stated reference base against the real sequence.
- Whether the guides designed for these variants are good is the job of the rest of the pipeline described in this report; the disease search only proposes inputs.

### Main Workflow

`gRNA/Main.fs` orchestrates the whole process, connecting the modules above into a cohesive workflow. Its public entry point, `getBestgRNAFromHGVS`, takes an HGVS string, the spacer size, the seed region bounds (`seedStart`/`seedEnd`), a `BowtieService`, a cancellation token, and a `complement` flag, and returns a `ResultFromHGVS` record:

```fsharp
// In gRNA/Main.fs

type ResultFromHGVS = {
    gRNA: SpacerFinder.gRNAResult list          // Ranked candidates for the mutated sequence
    originalGRNA: SpacerFinder.gRNAResult list  // Ranked candidates for the original (wild-type) sequence
    mutatedSequence: string
    originalSequence: string
    extraNucleotids: int                        // Flanking context length used on each side (never negative)
    windowStart: int                            // 1-based accession window the sequences were cut from
    windowEnd: int
}
```

1. **Input:** receives an HGVS notation, a spacer size, and a seed region. The spacer size (18-60) and the seed range are validated first.
2. **Parsing:** uses `HGVS.fs` to interpret the mutation; malformed codes are refused (section 1).
3. **Sequence:** uses `SequenceRepository.fs` to fetch the reference sequence from NCBI (cached in-process per accession). The response must be exactly one FASTA record of letters (it is upper-cased); an HTML or JSON error page, an empty body or a second record is an upstream error, never a sequence. The variant is then checked against it (position inside the sequence, stated reference base equal to the real base, supported coordinate system).
4. **Mutation:** uses `Sequence.fs` to build both the mutated and the original (wild-type) subsequence, with flanking context on each side (`spacer size - mutation length`, at least 0). When `complement` is requested, both sequences are replaced by their reverse complement before spacer generation (section 4.1.1), and the mutation position is mirrored. If the mutated sequence is shorter than the spacer there is no window and the run stops with a message.
5. **gRNA search:** uses `SpacerFinder.fs` to generate and score all candidate gRNAs **for both the mutated and the original sequence**, coordinating calls to `BowtieWrapper.fs` and `RNAFoldWrapper.fs`.
6. **Substitution rule:** if the HGVS mutation type is `Substitution`, `applySubstitutionSpecialRule` is applied to both candidate lists (see section 4.4): the mismatch-engineered candidate is put first and the ordinary list follows; if no candidate matches (mutation too close to an end of the sequence) the ranking is kept and a notice is added to the result.
6b. **Discrimination:** `annotateDiscrimination` fills `WtMismatches` for both lists against the other allele (section 4.2e).
7. **Output:** returns the two ranked gRNA lists (mutated and original), both sequences, and the flanking-context length, so the UI can render the original-vs-mutated comparison and both result tables side by side.

This modular, science-grounded approach lets the application design efficient, specific gRNAs for editing genes associated with disease.

---

## 9. Guide pooling (combinatorial screening)

*Code: `gRNA/Pooling.fs`; UI: the `/pooling` page.*

**Problem.** A disease panel can have hundreds of known variants; screening one guide per well is infeasible. Guides are produced
in tandem (at most **K** per transcript, a biological limit because longer tandem arrays risk RNA conformations that block Cas13; *[needs source: the limit of 5 is the team's working assumption until a reference is added]*)
and **pooled**: each guide is placed in **R** wells so that a positive readout still identifies it.

**Models** (V guides, K per well; N = total wells):

| Model | Layout | N | R |
|---|---|---|---|
| 2D fragmented | blocks of K x K guides; each block contributes its K rows then its K columns | `2K * ceil(V / K^2)` | 2 |
| 2D matrix | one `s x s` square, `s = ceil(sqrt V)`; every row and column split into chunks of K | `2s * ceil(s / K)` | 2 |
| 3D | a `c x c x c` cube, `c = ceil(cbrt V)`, sliced along three axes, slices split into chunks of K | `3c * ceil(c^2 / K)` | 3 |

The binary model (`N = ceil(log2 V)`) is deliberately excluded: it needs `K = V/2`, which violates `K <= 5`. `compareModels`
costs all three for a (V, K) and `bestModel` picks the cheapest (ties prefer the simpler layout).

**Decoding.** After screening, the positive wells identify guides: a guide is *implicated* when all R of its wells are positive.
With R = 2, two true hits can light up four wells and so also implicate two innocent guides at the intersections; `Pooling.decode`
reports those as *ambiguous* (a guide is *definite* only if some positive well is explained by it alone), lists *partially
supported* guides (some but not all wells positive: dropout or noise) and *unexplained* positive wells. Ambiguous guides need an
individual confirmation round.

**Specification source.** The well-count formulas were originally taken from a spreadsheet, `Combinatoria.xlsx`
(sheets "Esquema_softwareCompuestos_(V)" and "Pruebas_manuales"), which is **not in this repository**; the table above is the
written specification and `PoolingMathTests`/`PoolingInvariantTests` encode it. If the workbook is recovered it should be committed
next to this report.

## 10. Notes on the code excerpts in this report

- Section 3's `SNP` excerpt is simplified: the real module is `module gRNA.SNP`, uses `task { }` (not `async { }`), and
  `getHgvsNotationsAsync` now takes a `CancellationToken`, raises `GrnaUpstreamException` on HTTP failure and returns `[]` only for
  "no `NG_` mapping".
- The FORNA diagram link uses plain `http` (the FORNA server refuses https connections, checked 2026-10-13), with the sequence and structure in the query string unencoded because the server does not decode them; it is shown only when the user asks, inside a sandboxed frame, and the interface says that the sequence is sent unencrypted to the FORNA server.
- Section 3's `SNP` excerpt also omits the new check that the rsID number is digits only (it goes into a URL path).
