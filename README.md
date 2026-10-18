# Diana

*Diana: precision guide RNA design.* The name is explained in [docs/about-the-name.md](docs/about-the-name.md).

**iGEM Team UMA-Malaga 2026, Software.**
Source: <https://gitlab.igem.org/2026/software/uma-malaga/diana-grna-designer> ·
Team wiki, software page: <https://2026.igem.wiki/uma-malaga/software> · License: [MIT](LICENSE).

Diana is a web application and command-line tool that turns disease-related variants into ready-to-order **CRISPR-Cas13 guide
RNAs**, and then plans how to screen many of them on a plate. It was built for the team's Cas13 (SHERLOCK-style) detection
platform, where every variant of a disease needs its own guide and testing hundreds of guides one per well is not realistic. It has
three connected parts:

1. **Disease variants**: start from a disease (name, OMIM number or MONDO id) and read the ClinVar variants linked to it.
2. **Builder**: start from HGVS notations [10] or rsIDs [5] and get ranked spacers, scored for GC content, homopolymers, offtargets
   (Bowtie [1]) and RNA secondary structure (ViennaRNA [2, 3]), for both the mutated and the original sequence, with a check of
   how well each guide tells the two alleles apart, restriction-site and assembly-standard checks, the complete guide RNA
   (direct repeat, adapters, custom flanks) and order-ready exports.
3. **Guide Pooling**: lay the chosen guides out on 96 or 384-well plates so that a positive well still identifies the guide, and
   decode a screen back into guides.

Everything runs on your own machine (one Docker command), from a browser or from the `grna` command line. The interface has a
sidebar (Builder, Disease variants, Guide Pooling, App Status), a light and a dark theme, a skip link and keyboard-operable tabs.
Numbers in square brackets point to the [References](#references).

![Diana guide design page, light theme](https://static.igem.wiki/teams/6119/wiki/images/software/v6-00-hero-light.avif)

![Diana guide design page, dark theme](https://static.igem.wiki/teams/6119/wiki/images/software/v6-00-hero-dark.avif)

> **AI assistance.** Parts of this code were written with an AI coding assistant and reviewed by the team. Read
> [.claude/RESPONSIBLE_AI_USE.md](.claude/RESPONSIBLE_AI_USE.md); the team remains responsible for everything committed here.

## Contents

[Quick start](#quick-start) ·
[Disease variants](#disease-variants-disease) · [Builder](#builder) · [Guide Pooling](#guide-pooling-pooling) ·
[App Status](#app-status-app-status-healthz) · [`grna` command line](#grna-command-line) ·
[Standards and interoperability](#standards-and-interoperability) · [gRNA metrics](#grna-metrics) ·
[The complete gRNA](#the-complete-grna-direct-repeat-adapters-custom-flanks) ·
[Validation](#validation) · [Architecture](#architecture) · [Project layout](#project-layout) ·
[Installation](#installation) · [Testing](#testing) · [Configuration](#configuration) · [Troubleshooting](#troubleshooting) ·
[Security](#security) · [Contributing](#contributing) · [References](#references) · [Future enhancements](#future-enhancements)

## Quick start

Needs Docker with Compose v2, about 10 GB of free disk for the first build and about 3 GB of RAM available to Docker.

```bash
git clone https://gitlab.igem.org/2026/software/uma-malaga/diana-grna-designer.git
cd diana-grna-designer
./start.sh            # Linux / macOS / WSL / Git Bash   (Windows PowerShell: .\start.ps1 or start.bat)
```

When the script prints success, open <http://localhost:5000>. Details, the other routes and troubleshooting are in
[Installation](#installation). To drive the same code from scripts, see [`grna` command line](#grna-command-line) and
[.claude/skills/diana/SKILL.md](.claude/skills/diana/SKILL.md).

## Disease variants (`/disease`)

Start from a disease instead of a variant list. Type a disease name, an OMIM number or a MONDO id, pick the match, and Diana
reads the ClinVar variants linked to it (the pathogenic and likely pathogenic ones by default) into a sortable, filterable,
paged table. Tick the ones you want and **send them to the Builder**: *Send to Diana (replace)* fills its input box, *Add to
Diana inputs* keeps what is there and drops duplicates, and every row has a one-click send. Nothing runs until you press
**Run analysis**. Selections can also be copied or downloaded as a list for `grna design --input` (*All rsIDs (.txt)*,
*Table (.csv)*).

- **Where the data comes from.** The disease name is looked up in the Monarch Initiative knowledge graph [14] (no key needed),
  which also gives a disease's subtypes: a group such as Fanconi anemia has only an OMIM *phenotypic series*, so its numeric OMIM
  numbers are taken from its subtypes (up to 40). The variants come from NCBI ClinVar [13] through E-utilities [7] (`elink` from
  OMIM, a server-side classification filter, batched `esummary`), the same NCBI host Diana already uses for sequences.
- **What a row is.** The rsID (linked to dbSNP) or, when ClinVar has none, a genomic HGVS on GRCh38 derived from ClinVar's
  canonical SPDI [8], which Diana also accepts. Classification and review status (stars) are ClinVar's own and are not
  re-interpreted. Haplotype and compound records can share one rsID, so the page also counts distinct rsIDs.
- **Limits, all stated on the page.** At most 3,000 ClinVar records are read per search (a larger disease is flagged
  incomplete). Diana takes 50 inputs per run, so a bigger selection can be copied or downloaded but not sent. Some rsIDs have no
  gene-level `NG_` mapping in dbSNP and will show "No HGVS notations found" in the Builder.
- **Not a clinical statement.** It is a candidate list built from ClinVar's classifications, queried live (ClinVar changes
  every month). Check each variant before relying on it. NCBI links a ClinVar record to an OMIM number through any of its
  references, so a few records can be only loosely related to the disease. The conditions of each record are shown so you can
  judge; nothing is filtered out on that basis. ClinVar notes that its data are not for direct diagnostic use.
- The same search runs headless: `grna disease 603903 --out sickle.txt`.

![Disease variants: ClinVar variants of a disease](https://static.igem.wiki/teams/6119/wiki/images/software/v6-11-disease-results.avif)

## Builder

The guide design page, and the home page of the app (`/`).

- **Inputs.** HGVS (`NC_000017.11:g.7674220C>T`) and rsIDs (`rs334`), several at once (up to 50 per run). An rsID is resolved
  to its HGVS notations, **by default only the disease-relevant allele(s) on the main gene record** (rs334 gives one notation, not
  twelve; tick *Show all alleles and gene records*, or use `grna ... --all-alleles`, for everything dbSNP lists). Variants are
  checked before any work starts: malformed codes, lower-case or IUPAC alleles, a reference base that differs from the real
  sequence, `c.` positions on a genomic record, intronic offsets and repeat notation are refused with a message, and a refused
  input never stops the others.
- **Coding positions.** `c.` positions work on a transcript record: `NM_000518.5:c.20A>T` is converted to `NM_000518.5:n.70A>T`
  using the coding-sequence start read from the NCBI GenBank record (`c.-5` and `c.*3` included). The guides are then designed
  on the mRNA itself, which is what Cas13 binds, and a notice says what was converted.
- **Both strands, and which one binds the mRNA.** Each variant is analysed on the normal strand and on the complement (`C`),
  designed from the reverse complement (the opposite strand read 5'->3'). Cas13 binds RNA, so each tab says whether its spacers are
  **complementary to the gene's mRNA** (an "mRNA" badge and the gene symbol; that tab is listed first) or "antisense" (with a
  warning). For a typed HGVS the gene's orientation is unknown and the page says so.
- **Results per variant.** The original and mutated sequences (mutation highlighted, copy as FASTA), two ranked spacer tables
  (sortable, filterable, paged, with an offtarget-risk chip, the seed region highlighted and the variant marked), the complete gRNA,
  its ViennaRNA structure and a FORNA [4] force-directed diagram drawn in the browser.
- **Can this guide tell the alleles apart?** Each guide has a *Mismatches vs other allele* column: the fewest letters in which its
  DNA target differs from the closest stretch of the other allele (a mutated-sequence guide against the normal sequence). 0 or 1 is
  flagged "not discriminating" (red chip), a filter hides those, and a warning appears when no guide of a variant reaches 2. It
  informs the choice; it does not change the ranking, and it counts differences without judging their type or position (see
  [Validation](#validation) for why that matters).
- **Substitution rule.** For substitutions, a spacer with one deliberate extra mismatch is listed first, highlighted; the whole
  ordinary ranked list follows it ([details](#grna-metrics)). The two positions of the rule are parameters.
- **Assembly compatibility.** Each guide carries two badges, one per standard. BioBrick RFC[10] requires the DNA to be free of
  EcoRI, NotI, XbaI, SpeI and PstI sites and iGEM Type IIS RFC[1000] of BsaI and SapI sites; a green badge means compatible, a
  red one means a site is present (hover it for the enzyme). Ten cloning enzymes in all are scanned (the seven named above plus
  BsmBI, BbsI and EcoRV), on both strands, in the DNA you would order, and flagged with a chip next to the sequence.
- **Off-target loci.** Expand any spacer to see where its target window aligns in the genome (chromosome, position, strand,
  mismatches; up to 50 alignments). The table's offtarget chip still saturates at "6+", honestly labelled. Loci are looked up for
  the spacer exactly as displayed; for the substitution rule's engineered spacer this can differ from the table's count, which is
  carried over from the unadjusted candidate.
- **Complete gRNA.** Choose the direct repeat, add the Frida or BioBricks adapters or your own flanks, copy it, fold it, export
  it as FASTA ([below](#the-complete-grna-direct-repeat-adapters-custom-flanks)).
- **Shortlist.** Pick spacers across variants, send them to the Pooling page without a CSV round trip, or download an
  order-ready **oligo CSV** (DNA, optional T7 promoter, top/bottom pairs, plate wells addressed like the pooling plan; columns
  `Well, Name, Oligo, Sequence 5'-3', Length, Restriction sites`) or the complete gRNAs as FASTA.
- **Runs.** Cancellable (whole run or one variant), bounded-parallel, and they survive navigating to another page. A refresh
  restores your inputs (never results: download the CSV report to keep them).
- **Files and sessions.** Load inputs from a plain list, a Diana CSV report or a saved session; *Save session* downloads a
  JSON document (inputs, spacer/seed, repeat, construct, shortlist) that `grna design --input session.json` reads too.
- **Permalinks.** *Copy link to this design* encodes `?hgvs=...&spacer=28&seed=10-17` (also `?rs=`, `?snp=`, `?mm=`, `repeat=`);
  opening it fills in the form and you press **Run analysis**; a link never starts a run on its own.
- **CSV report.** Per input (rsID or HGVS) or all inputs at once, with the parameters in a leading `#` line and a `Strand` column.
- **Tooltips everywhere.** Hover (or focus) a column header, filter, option or badge for a one-paragraph explanation of what it
  is and how it is computed. The wording lives in one file, `DiseaseMutationsApp/Services/HelpText.cs`.

![Ranked spacers for a substitution: engineered guide first, offtargets, assembly badges, energy, discrimination](https://static.igem.wiki/teams/6119/wiki/images/software/v6-07-builder-ranked-table.avif)

### Using the Builder

0. No variant list yet? Open **Disease variants**, find the disease, tick the variants you want and press **Send to Diana**:
   the inputs below are filled in for you.
1. Enter variants (comma-separated), the spacer size and seed range, and, if you want to try other substitution-rule positions, the
   variant and mismatch positions (defaults 28 and 10-17, inclusive, 0-based in the spacer; spacer size 18 to 60).
2. **Run analysis**. rsIDs resolve first, then variants are analysed two at a time. Cancel the run, or one variant, any time.
3. Pick a tab per input; for rsIDs a second strip lists each resolved variant on both strands. Tabs use arrow keys.
4. Press **Use** on a spacer to build the complete gRNA, copy it, fold it, or add it to the shortlist.
5. Download the CSV report, or send the shortlist to Pooling.

Tab status is shown by shape, text and label (loading ring, ready dot, failed triangle, cancelled square), never colour alone.
If a variant fails it shows the reason and a **Retry this variant** button; other variants are unaffected.

HGVS examples:

```
NC_000017.11:g.7674220C>T          substitution      NC_000017.11:g.7674220_7674222delinsAG   deletion-insertion
NC_000017.11:g.7674220_7674222del  deletion          NC_000017.11:g.7674220_7674222dup        duplication
NC_000017.11:g.7674220_7674221insA insertion         NC_000017.11:g.7674220_7674222inv        inversion
NM_000518.5:c.20A>T                coding position on a transcript (converted to the mRNA)
```

## Guide Pooling (`/pooling`)

Screening a whole variant panel one guide per well is infeasible, so guides are pooled combinatorially: at most K guides per
tube (the guides of one tube are made in tandem, as one crRNA array [12]; the limit of 5 is the team's working assumption,
because longer arrays may fold in ways that hinder single guides, and it is adjustable), laid out so each guide appears in R wells (2 for the 2D models, 3 for 3D) and a positive readout still identifies the guide. Choose a guide
count or paste a guide list / Diana CSV report (or arrive from the Builder's shortlist); the page compares the three models (2D
fragmented, 2D matrix, 3D), shows the plate map and the tube list, and exports CSV. For V = 100 guides and K = 5 the 2D models need
40 wells and the 3D model 75, against 100 for one guide per well.

**Decode screening results**: enter the positive wells (`A1, B3`, `Plate 2 - C4`, or tube numbers like `#12`) to get the
implicated guides, with overlapping multi-hit collisions flagged as ambiguous instead of hidden and the positives marked on the
plate map. The CSV columns are `Tube ID, Guides in Tandem, Well`; slots the layout reserves but that hold no guides are listed as
`Empty - do not prepare`. Limits: 100,000 guides and 10,000 guides per well. The formulas and the reasoning are in
[BIOLOGICAL_REPORT.md](BIOLOGICAL_REPORT.md) section 9.

![Guide Pooling: plate map](https://static.igem.wiki/teams/6119/wiki/images/software/v6-22-pooling-plate-map.avif)

## App Status (`/app-status`, `/healthz`)

The same preflight as `grna doctor`: Bowtie binary and index, Python and ViennaRNA, NCBI reachability, memory.

![App Status](https://static.igem.wiki/teams/6119/wiki/images/software/v6-25-app-status.avif)

## `grna` command line

```
grna doctor [--network]
grna resolve rs334 [--all-alleles]
grna disease "sickle cell disease" --format rs --out sickle.txt   # rsIDs of a disease's pathogenic ClinVar variants
grna design --hgvs NC_000017.11:g.7674220C>T --spacer 28 --seed 10-17 [--repeat default|none|<RNA>] [--complement] [--format csv|json|tsv]
grna design --input variants.txt|session.json --out report.csv   # batch, resumable, bounded concurrency ([--all-alleles] for every dbSNP allele)
grna design --hgvs 'NC_000017.11:g.7674220C>T' --snp-pos 3 --mismatch-pos 4   # where the substitution rule puts the variant and its extra mismatch
grna fold "GAUUUAGACUACCCC..."
grna pool --guides report.csv --capacity 5 --plate 96 --model auto --out plan.csv
grna pool --count 100 --capacity 5 --plate 96            # plan from a number of guides
```

`design --format csv` emits the same schema as the web export, so a CLI report pastes straight into the Pooling tab. Results go
to stdout and progress and warnings to stderr. Exit codes distinguish bad input (2), a missing native dependency (3) and an
upstream failure (4). Unknown options are rejected (a typo is never silently ignored) and `--help` works after any command. A
resumed report (`--out` pointing at an existing report) must have been made with the same spacer, seed, repeat and rule positions,
and only a variant already present on the same strand is skipped. When every variant fails the exit code is that of the first
failed variant in input order; a mixed run exits 1. `grna disease 603903 --format rs --out sickle.txt` followed by
`grna design --input sickle.txt --out report.csv` goes from a disease to guides.

The full reference (every option, the output formats, scripting recipes) is in
[.claude/skills/diana/references/cli-reference.md](.claude/skills/diana/references/cli-reference.md); the skill itself
([.claude/skills/diana/SKILL.md](.claude/skills/diana/SKILL.md)) lets an AI agent install, start and drive the tool, and its
`scripts/grna.sh` / `scripts/grna.ps1` run the CLI from the Docker image with the current directory mounted.

## Standards and interoperability

Diana reads and writes existing formats instead of inventing new ones:

| Standard or format | How Diana uses it |
|---|---|
| **HGVS nomenclature** [10] | Input notation (`g.`, `m.`, `n.`, and `c.` on `NM_`/`XM_` transcripts), checked strictly against the real sequence. Repeat, conversion and translocation notation parse but are refused for design. |
| **dbSNP rsIDs** [5], **NCBI RefSeq** [6, 7] | An rsID is resolved to HGVS through the NCBI Variation Services; reference sequences and GenBank coding ranges come from E-utilities. |
| **ClinVar** [13] and **SPDI** [8] | Disease variants; ClinVar's canonical SPDI is converted to a genomic HGVS that Diana's own parser must accept. |
| **GRCh38** [9] | The reference for the offtarget search (Bowtie 1.3.1 index `GRCh38_noalt_as`) and for coordinates. |
| **BioBrick RFC[10]** and **iGEM Type IIS Assembly RFC[1000]** | Each guide gets a compatible / not compatible badge per standard, from the enzymes the standard excludes (EcoRI, NotI, XbaI, SpeI, PstI; BsaI, SapI). Diana checks the internal sites of the DNA you would order; it does not add or check the standards' prefix and suffix. The BioBricks adapter in the Builder is built from that standard's sites (EcoRI, NotI, XbaI/SpeI, PstI). |
| **FASTA** | Sequences copied from the Builder and the complete gRNAs (DNA or RNA) exported from the shortlist. |
| **Dot-bracket notation** (ViennaRNA [2, 3]) and **FORNA** [4] | The predicted structure is shown as dot-bracket and drawn with the FORNA viewer. |
| **CSV, TSV, JSON** | The CSV report schema is shared by the web export, `grna design` and the Pooling import (documented in the CLI reference); `design` can also write TSV and JSON. Session files are JSON; oligo orders and pooling plans are CSV. Every CSV field that starts with `=`, `+`, `-` or `@` is neutralised so a spreadsheet does not run it. |

**Not supported, stated plainly:** Diana does not read or write **SBOL**, and it does not register parts in the iGEM Registry; a
guide leaves the tool as FASTA, an oligo CSV or the CSV report. The built-in direct repeat is the LwaCas13a one; other Cas13
repeats must be typed in. For use from code, the F# library is packable as the NuGet package `gRNA` (not published) and is
documented in [docs/using-the-library.md](docs/using-the-library.md); [examples/standalone.fsx](examples/standalone.fsx) runs it
from F# Interactive. There is no HTTP design API: the integration points are the CLI, the files above and the library.

### Reading the CSV report

`RS ID, HGVS, Sequence Type, Rank, Sequence, Score, GC Content, Offtargets, Seed Region, Homopolymers, Fold Energy, Strand,
WT Mismatches, Engineered, Scaffold Intact, Spacer Unpaired`, after a first `#` line with the spacer, seed, direct repeat,
rule positions and the time. `Sequence Type` is `Mutated` or `Original`; `Strand` is `Normal` or `Complement`.

## gRNA metrics

- **GC score**: 1.0 for GC content from 40% to 60% *inclusive*; below 40% proportional (`gc / 40`), above 60%
  `(100 - gc) / 40`. Always in 0-1. The range is borrowed from DNA-targeting guide design; no Cas13-specific optimum was found.
- **GC content**: raw GC percentage, two decimals.
- **Homopolymers**: runs of 4 or more identical bases; 0 is best. Only the U-run rationale (it can stop transcription by RNA
  polymerase III) has a source; all four bases are counted by team decision.
- **Offtargets**: Bowtie hits of the DNA window against the genome (up to 2 mismatches). 1 is a unique target; the count saturates
  at 6, shown as "6+ saturated". **0 means "no genomic match" and is ranked best, by design**: it is common for insertions,
  duplications and deletions, whose junction windows do not exist in the genome, so for those variants the offtarget criterion
  does not separate candidates (see [BIOLOGICAL_REPORT.md](BIOLOGICAL_REPORT.md) section 4.2c). It is a **genome** check, not a
  transcriptome check.
- **Spacer unpaired** (`Spacer Unpaired` in the CSV): the share of the spacer's letters left unpaired in the fold of the complete
  gRNA; a spacer that folds on itself or sticks to the scaffold may bind its target less well.
- **Hairpin broken** (`Scaffold Intact = No`): the scaffold's own hairpin (the fold of the scaffold alone) is no longer fully
  formed in the complete gRNA, so the guide may not load into Cas13. Both checks come from the fold already computed; they inform
  the choice and do not change the ranking.
- **Energy**: minimum free energy (kcal/mol, Turner 2004 parameters [11]) of the gRNA folded with the **direct repeat chosen for
  screening** (the 36 nt scaffold by default); closer to zero is preferred.
- **Direct repeat for screening** (web form, `--repeat` in the CLI, `repeat=` in the permalink and session file): the 5' context
  each spacer is folded with while screening: LwaCas13a (the 36 nt DR, the default), *none* (the spacer alone, which also makes
  *Hairpin broken* always "No") or *your own* repeat as RNA/DNA. It changes only the Energy, Hairpin broken and Spacer unpaired
  columns (and the ranking through the energy), never the alignments. It is recorded in the report's first line, and a batch
  report cannot be resumed with another repeat.
- **Score**: 0-1, from the rank after sorting by (Offtargets, Energy, GC score, Homopolymers); ties share a rank. It is a *relative*
  number for comparing candidates of one variant, not a prediction of guide activity; when a variant has a single ordinary
  candidate the table says "only candidate" instead of 100 %.
- **Mismatches vs other allele** (`WT Mismatches` in the CSV): described [above](#builder); 2 or more can tell the alleles apart,
  more is better.

**Substitution rule.** Substitution variants get a mismatch-engineered spacer (`Rank = 1`, `Score = 1.0`, `Engineered = Yes`)
listed **first, highlighted**, with the whole ordinary ranked list following it unchanged. The idea, after the SHERLOCK
strategy [15], is to make a guide more sensitive to a single-base difference by adding one deliberate mismatch next to the
variant; the spacer then has one mismatch to the mutated target and two to the original. **The two positions of the rule are
parameters**: *Variant position* (default 3) and *Engineered mismatch position* (default 4) in the Builder form, `--snp-pos` /
`--mismatch-pos` on `grna design`, `?snp=` / `?mm=` in a permalink. Both are counted from the spacer's 3' end (1 = last letter) and
must differ; to use a position counted from the 5' end enter spacer size - position + 1. The positions are written in the CSV's
provenance line, and a resumed report made with other positions is refused. When the mutation is too close to an end of the
accession for such a window to exist (at the start or in the last 24 bases for a 28 nt spacer, the sides swapping on the
complement strand) the rule cannot apply: the ordinary ranking is shown instead, with a visible warning (a `note` line on stderr
and a `notices` field in `--format json` for the CLI). **The rule has not been shown to discriminate alleles in the lab** (see
[Validation](#validation)); the best positions are an open question ([BIOLOGICAL_REPORT.md](BIOLOGICAL_REPORT.md) section 4.4).

## The complete gRNA (direct repeat, adapters, custom flanks)

By default the complete gRNA is the 36 nt LwaCas13a direct repeat (DR) `GAUUUAGACUACCCCAAAAACGAAGGGGACUAAAAC` followed by the
spacer (the reverse complement of the target window, as RNA). The **Complete gRNA** card (after pressing **Use**) lets you change
what surrounds the spacer:

| Option | What it adds |
|---|---|
| **Direct repeat** selector | Which DR goes before the spacer: `LwaCas13a` (default) or `None`. More Cas13 DRs are a one-line addition to `GuideConstruct.DrPresets` once their sequence is verified. |
| **Frida adapter** | Our own engineered adapter: `AGAGCGTCTCTAAAC` - spacer - `GCTTAGAGACGTACT`. |
| **BioBricks assembly adapter** | `GGCTAGCGAATTCGCGGCCGCTTCT` - spacer - `AGTAGCGGCCGCTGCAGCGATCGT`. Usable alone or together with Frida; when both are on it always wraps the Frida adapter. |
| **Custom 5' / 3'** | Two free text boxes (A, C, G, T or U; either may stay empty) for your own DR, adapter or any other sequence. Invalid text is flagged and ignored. |

The pieces are assembled in this order (5' to 3'), with the "spacer" being the DR-plus-spacer core:

```
[BioBricks 5'] [Frida 5'] [custom 5'] [DR] SPACER [custom 3'] [Frida 3'] [BioBricks 3']
```

![The complete gRNA with both adapters and a custom 5' flank](https://static.igem.wiki/teams/6119/wiki/images/software/v6-15-builder-adapters.avif)

The setting is shared by the whole page and applies to everything downstream: the displayed sequence (adapter DNA letters are shown
as RNA, T to U), **Copy**, **Get structure** (the fold and the diagram are of the full construct), the shortlist, the
restriction-site warnings and the order CSV (as DNA; the T7 promoter, if ticked, goes in front of the outermost 5' piece). It is
saved in the session file and loaded back with it. Notes:

- The adapters carry cloning-enzyme sites on purpose (for example BsmBI in Frida; EcoRI, NotI, PstI and the XbaI/SpeI junctions in
  BioBricks). These designed sites are not reported as warnings, but any extra site brought by the spacer or a custom flank still is.
- The candidate table's `Energy`, `Spacer Unpaired` and `Hairpin broken` columns are computed with the direct repeat chosen for
  screening (LwaCas13a by default, none, or your own) and no adapters. **Get structure** folds the full construct, shows the same two
  checks on that fold and warns when the repeat hairpin is intact in the table but broken in the built guide, or the other way round.
- The command line `grna design` reports the screening fold (`--repeat`, no adapters); `grna fold` folds any RNA sequence you give
  it, so a built construct can be folded with it.
- The adapters were not tested for function in Diana, and the program does not assess whether they leave Cas13's processing of the
  DR and spacer intact.

**FASTA export.** The built, complete gRNA can be downloaded as FASTA: **FASTA** on the card for the selected guide (RNA), and
**Complete gRNAs (FASTA)** in the shortlist for all shortlisted guides (DNA by default, RNA with **FASTA as RNA**). Each record's
header is `>variant[_C]|spacer=<bare spacer>|construct=<recipe, e.g. LwaCas13a DR + Frida + BioBricks>|DNA|<length>nt`, followed by
the sequence on one line. The order CSV keeps its oligo format.

## Validation

The full account, with what each claim rests on, is in [docs/validation.md](docs/validation.md). In short:

- **Automated tests**: 1,124 unit and component test cases (NUnit, bUnit; no container, no network; 1,111 pass and 13 skip
  themselves because they need the live services or ViennaRNA) and 70 browser tests (Playwright, with axe-core accessibility
  scans), none failing. See [Testing](#testing).
- **Hand checks against the real sequences**: the sickle-cell (rs334) and cystic fibrosis F508del designs were rebuilt by hand from
  the NCBI sequences ([docs/validation.md](docs/validation.md) section 4); the real Bowtie index, ViennaRNA and the Docker routes are
  covered by `grna doctor` and a written [manual test plan](docs/manual-test-plan.md).
- **Wet lab**: guides designed with Diana for a cystic fibrosis SNP panel were functional in a SHERLOCK assay (signal significantly
  above the negative controls), and a five-guide tandem transcript was processed by Cas13a. **Allele discrimination was not
  achieved** with the mismatch placements tested, so the substitution rule is not lab-validated. The data and their limits are on
  the team wiki, [Results](https://2026.igem.wiki/uma-malaga/results) and
  [Engineering](https://2026.igem.wiki/uma-malaga/engineering) pages. The pooling layout has not been tested on a real pooled screen.

## Architecture

- **`DiseaseMutationsApp/`**: Blazor Server (.NET 9). One interactive render mode declared once on the router, prerendering off.
  `AnalysisRunner` (scoped per circuit) owns runs; `AppStateService` holds per-circuit UI state.
- **`gRNA/`**: F# library: HGVS parsing, sequence retrieval (NCBI), spacer finding/scoring/ranking, Bowtie and ViennaRNA
  wrappers, the disease search, pooling and decoding, diagnostics, CSV schema. Usable on its own:
  [docs/using-the-library.md](docs/using-the-library.md).
- **`gRNA.Cli/`**: the `grna` tool over the library.
- **Native tools**: Bowtie 1.3.1 (committed binary), the GRCh38 index (baked into the image), Python + ViennaRNA 2.7.2.

```
HGVS / rsID -> NCBI sequence -> sliding windows -> Bowtie (offtargets) + ViennaRNA (folding) -> ranked spacers -> complete gRNA -> shortlist -> oligo CSV / FASTA / pooling plate
disease name / OMIM / MONDO -> Monarch (names, subtypes) + NCBI ClinVar -> rsIDs or HGVS -> (the input of the line above)
```

The Docker setup is two images. The **Bowtie base image** (`Dockerfile.bowtie-base`) downloads and checksum-verifies the
GRCh38 index once; the **app image** (`Dockerfile`) derives from it, adds Python/ViennaRNA, the published app and the CLI, and
runs as a non-root user with a `/healthz` HEALTHCHECK. **The index stays in the image on purpose**: Bowtie reads it
memory-mapped, and a volume or bind mount would make alignment 10x+ slower
([why](docs/runtime-contract.md#why-the-index-lives-in-the-image)).

## Project layout

```
DiseaseMutationsApp/       Blazor Server app (Pages: Builder, Disease variants, Guide Pooling, App Status; Components, Services, wwwroot)
gRNA/                      F# library (HGVS, SpacerFinder, DiseaseVariants, Pooling, ReportSchema, Diagnostics, ...)
gRNA.Cli/                  the grna command-line tool
DiseaseMutationsAppTests/  NUnit + bUnit tests (library, CLI in-process, components, accessibility markup, opt-in live contract tests)
DiseaseMutationsApp.E2E/   Playwright browser tests against the real UI with a fake pipeline and a fake disease source
docs/                      about-the-name, runtime-contract, using-the-library, design-system, manual-test-plan, validation
examples/standalone.fsx    run the library from F# Interactive
scripts/check-pins.sh      dependency pinning gate
showcase/                  source of the feature-tour video (Playwright + Remotion; generated footage is not committed)
logos/                     the Diana logotype and icon (positive and negative, 1x and 2x)
BIOLOGICAL_REPORT.md       each biological step mapped to the code, with the open scientific questions
THIRD-PARTY-NOTICES.md     licenses of the bundled components
Dockerfile, Dockerfile.bowtie-base, docker-compose.yml, start.sh, start.ps1, start.bat
.claude/skills/diana/      AI-agent skill: install, run and drive the grna CLI (with grna.sh / grna.ps1 Docker wrappers)
```

## Installation

Requirements: Docker with Compose v2, about **10 GB free disk** for the first build (the ~3.7 GB index zip and the unpacked
index coexist), and about **3 GB RAM available to Docker** (the index is memory-mapped reclaimable cache, so more RAM only keeps
more of it hot; no need to raise Docker's memory).

### Route 1: one command (default)

```bash
./start.sh            # Linux / macOS / WSL / Git Bash
.\start.ps1           # Windows PowerShell   (or start.bat)
```

It runs preflight checks (Docker daemon, Compose v2, free disk), builds or pulls the images, starts the app and prints
success only once `http://localhost:5000/healthz` answers. If `GRNA_REGISTRY` points at a container registry holding prebuilt
images it pulls them (falling back to a local build if the registry is unreachable). The app listens on `127.0.0.1` only; set
`GRNA_BIND=0.0.0.0` to expose it on your network (there is no authentication).

### Route 2: build everything locally

```bash
./start.sh --build-local        # never pulls; builds the base and app images from source
./start.sh --rebuild            # force-rebuild the app image
./start.sh --rebuild-bowtie     # rebuild the index base image too (implies --rebuild)
```

Manual equivalent: `docker compose --profile base build bowtie-base && docker compose up -d --build`.

Validate any install with the real index inside the container:

```bash
docker run --rm --entrypoint grna disease-mutations-app:latest doctor
```

### Without Docker (development)

Needs the .NET SDK pinned in `global.json`, a Bowtie binary and index, and python3 with `viennarna`. Point the library at them
with `GRNA_BOWTIE_BINARY`, `GRNA_BOWTIE_INDEX`, `GRNA_PYTHON`, then `dotnet run --project DiseaseMutationsApp`. Everything else
(tests, the pooling page) works without any native tool.

## Testing

```bash
dotnet restore --locked-mode
dotnet test DiseaseMutationsAppTests
```

The unit suite has 1,124 test cases (1,111 pass, 13 skip themselves because they need the live services or
ViennaRNA) and needs no container and no network: the library's pure logic (scoring, parsing, validation, pooling and decoding, CSV
round trips), the `grna` CLI driven in-process (exit codes, option checking, resume), the `AnalysisRunner` (concurrency bound,
cancellation, per-variant cancel, timeouts, retry) against a fake pipeline, and the components with bUnit, including markup checks
for the accessibility requirements. `RnaFoldIntegrationTests` run the real ViennaRNA when `python3` and `RNA` are available and skip
otherwise. The GitLab pipeline (`.gitlab-ci.yml`) runs the pinning gate (which also covers the CI job images) and these tests,
publishes the coverage report, and builds no images, so run `grna doctor` in the locally built image yourself.

**Browser tests.** `DiseaseMutationsApp.E2E` has 70 tests that serve the real web app with a deterministic fake pipeline (no
Bowtie, ViennaRNA or NCBI needed) and drive Chromium with Playwright: runs, cancel and retry, limits, permalinks, CSV downloads,
the disease search and its hand-off to the Builder, the pooling plate map and decoder, dark/light theme, a 375 px phone viewport,
and an axe-core scan (WCAG 2.0 to 2.2 A/AA) of every page state. They skip themselves, with the reason, when Chromium is not
installed. To run them locally:

```bash
E2E_INSTALL_BROWSERS=1 dotnet test DiseaseMutationsApp.E2E   # first time only: downloads Chromium (about 150 MB)
dotnet test DiseaseMutationsApp.E2E                          # afterwards
```

**Disease search against the real services.** The Monarch and ClinVar client is tested offline against captured real
responses and a stubbed HTTP layer. `GRNA_LIVE_TESTS=1 dotnet test --filter Category=Live` runs a few contract tests against
the real services (not part of CI); it is the early warning for an upstream change.

**Manual tests.** What automation cannot check (the real index and tools, Docker routes per OS, assistive technology) is in
[docs/manual-test-plan.md](docs/manual-test-plan.md).

## Data and large files

This repository holds source code only. The GRCh38 [9] Bowtie index (about 3.7 GB) is **not** stored here: the base image downloads it
from the public Bowtie index bucket at build time and verifies its SHA-256 (`Dockerfile.bowtie-base`). Only the Bowtie 1.3.1
binary (about 14 MB, `bowtie/`) and a Windows ViennaRNA wheel (about 2 MB, `DiseaseMutationsApp/`) are committed.

## Configuration

See the table in [docs/runtime-contract.md](docs/runtime-contract.md#configuration): `GRNA_BOWTIE_BINARY`, `GRNA_BOWTIE_INDEX`,
`GRNA_PYTHON`, `GRNA_NCBI_API_KEY`, `GRNA_NCBI_CONTACT`, `Analysis__MaxConcurrentVariants`, `Analysis__VariantTimeoutSeconds`.
Hosting defaults live in [`Program.cs`](DiseaseMutationsApp/Program.cs) (circuit retention, message size, timeouts) and the
compose file ([`docker-compose.yml`](docker-compose.yml)); the container runs in `Production`.

## Troubleshooting

Start with `/app-status` or `grna doctor`: each failing check names its remedy.

- **Bowtie index not found**: the base image was not built or a custom `GRNA_BOWTIE_INDEX` is wrong. `./start.sh --rebuild-bowtie`.
- **ViennaRNA / Python errors**: `pip install viennarna==2.7.2`; check `GRNA_PYTHON`.
- **"No HGVS notations found" for an rsID**: dbSNP returned no `NG_` mapping (different from a lookup error).
- **Connection lost banner**: the server keeps the run going for about 10 minutes and the page rejoins automatically.
- **Slow alignments**: never move the index to a volume (it must stay in the image layer).
- **NCBI rate limits**: set `GRNA_NCBI_API_KEY` and `GRNA_NCBI_CONTACT` (the disease search uses them too).
- **"No disease matched"**: Monarch has nothing under that text. Try another spelling, an OMIM number or a MONDO id.
- **"The list is incomplete"** (Disease variants): more than 3,000 ClinVar records matched. Choose "Pathogenic only" or a more
  specific disease.
- **Monarch or NCBI "could not be reached" / HTTP 429**: press **Try again**; with many users behind one address set the NCBI key.

## Security

Single-tenant local tool: no authentication, bound to loopback by default, `Production` mode in the container (no stack traces),
non-root user. If you publish it, put an authenticating reverse proxy in front. `/healthz` returns each check's detail (which
contains file-system paths) only to loopback callers; from the host through Docker a request arrives from the bridge, so use
`/app-status` or `grna doctor` there. The FORNA preview is drawn in your browser by the bundled fornac library and sends nothing
anywhere. Only the optional "Open in FORNA" link, which you must click, sends the gRNA sequence to the FORNA server over plain http
(it does not offer https); the interface says so. Variants you enter are logged only at Debug level. The Disease variants page
sends what you type (a disease name or number) to the Monarch API and to NCBI (with `tool=gRNA` and `email=$GRNA_NCBI_CONTACT`); no
sequence or guide leaves the machine except for the NCBI sequence and variant lookups the design needs.

## Contributing

Fork, branch, commit, open a merge request on GitLab. Contributions are accepted under the project's MIT license. Add unit tests for new
behaviour; update this README and, for algorithm changes, [BIOLOGICAL_REPORT.md](BIOLOGICAL_REPORT.md). Dependencies are
pinned (see the pinning policy in [docs/runtime-contract.md](docs/runtime-contract.md#dependency-pinning-policy)); run
`scripts/check-pins.sh` before pushing and bump versions only in the central places listed there. To extend the software:
put pure logic in the F# library, expose it through a C# service, and cover it with tests
([docs/using-the-library.md](docs/using-the-library.md)).

## License

MIT, see [LICENSE](LICENSE). The image also contains components under other licenses, notably **Bowtie (Artistic License 2.0)** and
**ViennaRNA (non-commercial-use license)**: see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Authors

iGEM Team UMA-Malaga 2026. Initial work by Javier Torralbo Cortes.

## Acknowledgments and references

Diana builds on Bowtie [1], ViennaRNA [2, 3], forna [4], NCBI resources and dbSNP [5-8], the Genome Reference Consortium
assembly GRCh38 [9], the HGVS nomenclature [10], ClinVar [13] and the Monarch Initiative [14]. Licenses for these components
are stated in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

### References

1. Langmead, B., Trapnell, C., Pop, M., & Salzberg, S. L. (2009). Ultrafast and memory-efficient alignment of short DNA sequences to the human genome. *Genome Biology*, 10(3), R25. https://doi.org/10.1186/gb-2009-10-3-r25
2. Lorenz, R., Bernhart, S. H., Höner zu Siederdissen, C., Tafer, H., Flamm, C., Stadler, P. F., & Hofacker, I. L. (2011). ViennaRNA Package 2.0. *Algorithms for Molecular Biology*, 6, 26. https://doi.org/10.1186/1748-7188-6-26
3. Hofacker, I. L., Fontana, W., Stadler, P. F., Bonhoeffer, L. S., Tacker, M., & Schuster, P. (1994). Fast folding and comparison of RNA secondary structures. *Monatshefte für Chemie*, 125, 167-188. https://doi.org/10.1007/BF00818163
4. Kerpedjiev, P., Hammer, S., & Hofacker, I. L. (2015). Forna (force-directed RNA): simple and effective online RNA secondary structure diagrams. *Bioinformatics*, 31(20), 3377-3379. https://doi.org/10.1093/bioinformatics/btv372
5. Phan, L., Zhang, H., Wang, Q., Villamarin, R., Hefferon, T., Ramanathan, A., & Kattman, B. (2025). The evolution of dbSNP: 25 years of impact in genomic research. *Nucleic Acids Research*, 53(D1), D925-D931. https://doi.org/10.1093/nar/gkae977
6. Sayers, E. W. et al. (2025). Database resources of the National Center for Biotechnology Information in 2025. *Nucleic Acids Research*, 53(D1), D20-D29. https://doi.org/10.1093/nar/gkae979
7. Sayers, E. A General Introduction to the E-utilities. In: *Entrez Programming Utilities Help*. National Center for Biotechnology Information. https://www.ncbi.nlm.nih.gov/books/NBK25497/
8. Holmes, J. B., Moyer, E., Phan, L., Maglott, D., & Kattman, B. (2020). SPDI: data model for variants and applications at NCBI. *Bioinformatics*, 36(6), 1902-1907. https://doi.org/10.1093/bioinformatics/btz856
9. Schneider, V. A. et al. (2017). Evaluation of GRCh38 and de novo haploid genome assemblies demonstrates the enduring quality of the reference assembly. *Genome Research*, 27(5), 849-864. https://doi.org/10.1101/gr.213611.116
10. den Dunnen, J. T. et al. (2016). HGVS recommendations for the description of sequence variants: 2016 update. *Human Mutation*, 37(6), 564-569. https://doi.org/10.1002/humu.22981
11. Mathews, D. H. et al. (2004). Incorporating chemical modification constraints into a dynamic programming algorithm for prediction of RNA secondary structure. *PNAS*, 101(19), 7287-7292. https://doi.org/10.1073/pnas.0401799101
12. Bryson, J. W. (2025). Array Assembler Provides Greatly Simplified crRNA Array Design for CRISPR Cas12 and Cas13 Variants. *ACS Synthetic Biology*. https://doi.org/10.1021/acssynbio.5c00100
13. Landrum, M. J., Chitipiralla, S., Kaur, K., Brown, G., Chen, C., Hart, J., Hoffman, D., Jang, W., Liu, C., Maddipatla, Z., Maiti, R., Mitchell, J., Rezaie, T., Riley, G., Song, G., Yang, J., Ziyabari, L., Russette, A., & Kattman, B. L. (2025). ClinVar: updates to support classifications of both germline and somatic variants. *Nucleic Acids Research*, 53(D1). https://doi.org/10.1093/nar/gkae1090
14. Putman, T. E., Schaper, K., Matentzoglu, N., et al. (2024). The Monarch Initiative in 2024: an analytic platform integrating phenotypes, genes and diseases across species. *Nucleic Acids Research*, 52(D1), D938-D949. https://doi.org/10.1093/nar/gkad1082
15. Gootenberg, J. S. et al. (2017). Nucleic acid detection with CRISPR-Cas13a/C2c2. *Science*, 356(6336), 438-442. https://doi.org/10.1126/science.aam9321

## Future enhancements

- **Specificity.** A transcriptome offtarget index (Cas13 sees spliced transcripts, the genome check does not), a mismatch-aware
  discrimination score that knows where in the spacer the mismatches fall and avoids G-U wobble pairs (the lab tolerated them, see
  [Validation](#validation)), and a check of common population variants inside the spacer window.
- **Disease variants.** Add the rsIDs ClinVar lacks through the ClinGen Allele Registry, expand subtypes more than one level, read
  more than 3,000 records, include somatic and oncogenicity classifications (only germline classifications are used today), and
  show consequence type and population frequency.
- **Coding-sequence (`c.`) positions on genomic records** (`NG_`, `NC_`): they need the exon map, because introns lie between the
  exons. Today only transcript records (`NM_`, `XM_`) are converted; others are refused with the advice to use the `NM_` accession.
- **Run history and design comparison** (reopen or diff earlier runs with different spacer/seed).
- **Enzyme presets** for other Cas variants: the complete gRNA is configurable, but only the LwaCas13a DR is built in and the
  28 / 10-17 defaults are fixed. Other Cas variants need their DRs, seed windows and mismatch positions verified against the
  literature first. The candidate table's fold columns use the screening repeat and no adapters; the Complete gRNA card re-folds the
  full construct.
- **Pooling** tested against a real pooled screen, and SBOL export of designed guides.

Not planned: additional genome assemblies (each adds ~4 GB to the image), authentication.

## Support

Open an issue in the GitLab project, or contact javiertorralbocortes@gmail.com.
