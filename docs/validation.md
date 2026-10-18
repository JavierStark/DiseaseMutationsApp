# Validation and evidence

What each claim about Diana rests on, how strong that evidence is, and what is **not** validated. Numbers come from runs of
this repository or from the team wiki; nothing here is estimated. Last checked 2026-10-18 against commit `b9397a4`.

Status words: **automated** (a test fails if it breaks), **by hand** (a person re-derived it from the real sequences),
**lab** (the SHERLOCK wet-lab work of the team), **not validated** (stated, not shown).

## 1. Summary

| Claim | Evidence | Status |
|---|---|---|
| HGVS parsing and validation (every mutation type, strict rejection of wrong input) | `HGVSTests`, `HGVSValidationTests`, `DesignValidationTests`, `CodingConversionTests`, `MainTests` | automated |
| The mutated and original sequences are built correctly (including inversion as reverse complement, strand mirroring) | `SequenceTests`, `MainTests`, `KnownDefectTests` | automated; sickle-cell (rs334) and F508del rebuilt by hand from the NCBI sequences |
| The spacer is the reverse complement of the target window, ranked as documented | `SpacerFinderTests`, `SpacerFinderHighlightTests`, `SpecialRulePositionTests` | automated, and by hand for rs334 and F508del (section 4) |
| Bowtie and ViennaRNA output are read correctly | `ParsingTests` (captured real output), `BowtieWrapperTests`, `RnaFoldIntegrationTests` (real ViennaRNA when present) | automated; the real index is checked by `grna doctor` and the [manual test plan](manual-test-plan.md) |
| Disease search returns what ClinVar and Monarch return | `DiseaseVariantsTests`, `CliDiseaseTests`, `DiseaseSearchPageTests` over trimmed real responses; opt-in `Category=Live` contract tests | automated offline; live contract tests are not in CI |
| Pooling layouts, well counts and decoding are correct | `PoolingMathTests`, `PoolingInvariantTests`, `PoolingLayoutTests`, `PoolingDecodeTests`, `WellAddressTests` (each guide in exactly R wells, no pool above K, unique well sets, 96 and 384-well addressing) | automated; formulas also checked against the team's design workbook, which is not in the repository |
| The interface works, is accessible and survives failure | `ComponentTests`, `AccessibilityTests`, `AnalysisRunnerTests`, 70 Playwright browser tests with an axe-core scan of every page state | automated |
| Guides designed with Diana activate Cas13a | Wet lab, cystic fibrosis panel: signal significantly above the negative controls in every setup | **lab** (see section 3) |
| A five-guide tandem transcript is processed by Cas13a | Wet lab: MT + 5G clearly above the no-Cas control | **lab**, partly (section 3) |
| Guides tell the two alleles apart | Wet lab: not achieved with the placements tested | **not validated** (a negative result) |
| The substitution rule improves allele discrimination | Not shown by the lab data | **not validated** |
| The ranking order (offtargets, energy, GC, homopolymers) predicts guide activity | No lab data; the score is a rank, not a prediction | **not validated** |
| The pooling layout works on a real screen | No pooled screen was run | **not validated** |
| The Frida and BioBricks adapters keep Cas13 processing intact | Not assessed by the program or by a test | **not validated** |

## 2. Automated tests

`dotnet test DiseaseMutationsAppTests`: **1,124 test cases, 1,111 passed, 13 skipped, 0 failed** (13 need the live services or a
Python with ViennaRNA and skip themselves). `dotnet test DiseaseMutationsApp.E2E`: **70 tests, 70 passed, 0 skipped** (real web
app, deterministic fake pipeline, Chromium through Playwright). Both on 2026-10-18.

What they cover, by area:

- **Input**: HGVS for every supported mutation type; malformed codes, reversed ranges, lower-case or IUPAC alleles, a reference base
  that differs from the real sequence, positions outside the sequence and unsupported coordinates all raise a typed input error;
  `c.` conversion on transcripts (including `c.-N` and `c.*N`) and its refusal elsewhere; rsID parsing on a captured dbSNP response.
- **Scoring and ranking**: GC content and score at the boundaries, homopolymers, seed extraction, sort order and ties, mutation
  highlighting, the substitution rule at its default and at other positions and its fallback with a warning, the discrimination
  metric, the scaffold-hairpin and spacer-unpaired checks, the direct-repeat choice, and the construct builder (adapters, custom
  flanks, FASTA).
- **Pooling**: well-count formulas, exact integer roots, layouts of the three models and invariants over many (V, K) pairs;
  decoding for single hits, collisions and dropouts.
- **Disease search**: parsers over trimmed real Monarch and NCBI responses (a phenotypic series, records with and without an rsID);
  request shapes, batching, the 3,000-record cap, subtype expansion, rate spacing, retry, error bodies and cancellation over a
  stubbed HTTP layer; every SPDI-derived HGVS must be accepted by Diana's own parser.
- **Files**: pasted lists, CSV reports (including tab-delimited), CSV round trips, session documents, the oligo order export and
  CSV formula neutralisation.
- **Command line** driven in-process: exit codes, rejection of unknown options, resumable batches, bounds on every number.
- **Runner and interface**: concurrency bound, cancellation, per-variant cancel, timeouts, retry; bUnit component tests with
  accessibility markup checks; browser tests of runs, limits, permalinks, downloads, the disease hand-off, the plate map, themes, a
  phone-sized screen and WCAG 2.0 to 2.2 A/AA scans.

Not covered by automation (and listed in the [manual test plan](manual-test-plan.md)): the real Bowtie index and tools, the Docker
install on each operating system, and screen readers. The GitLab pipeline runs the pinning gate and the unit tests; it does not
build the image.

## 3. Wet-lab evidence

The wet lab used cystic fibrosis (*CFTR*) as its model disease. The experiments and their figures are on the team wiki
([Results](https://2026.igem.wiki/uma-malaga/results), [Engineering](https://2026.igem.wiki/uma-malaga/engineering)); this is what
they say about the software, with their limits.

| Experiment (wiki section) | What was tested | Outcome | What it says about Diana | Limits |
|---|---|---|---|---|
| Specificity, positions 3 and 4 (Results 3.1, Fig. 8, 9) | Guides for a CF SNP with the variant at spacer position 3 and a synthetic mismatch at 4, against healthy and mutant templates | Fluorescence in every combination, significantly above the negative controls; no clear difference between alleles | The designed guides are **functional**; this placement did **not** discriminate | Triplicate for the wild-type guide, duplicate for the mutant guide (reagent limits) |
| Specificity, positions 5 to 11 (Results 3.2, Fig. 10, 11) | Seven guides, fixed mismatch at 3, variant moved across positions 5 to 11, in situ transcription | Strong signal for all; mostly **no significant** difference between templates; the two guides with a significant difference (positions 3 and 8) gave the *higher* signal on the healthy template, the opposite of the design | Functional again; discrimination **not achieved**. The team attributes it to G-U wobble pairs, which Cas13a tolerates | Triplicate; the explanation comes from inspecting the hybrids, not from a separate test |
| Substitution guides designed with the software (Engineering, cycle 5, iteration 1) | SNP guides from the then-current rule, ordered from IDT | Signal also with the wild-type sequence alone: not specific enough | The substitution rule, as first built, did **not** give discrimination; its positions were later made adjustable in the software | Reported in the narrative of the Engineering page; no figure. The revised-position assay (cycle 5, iteration 2) has no result yet |
| In situ transcription from DNA templates (Results 2, Fig. 6, 7) | The guide supplied as DNA with a T7 promoter instead of purified RNA | 1X template: signal up to 379.8 % of the RNA control at its peak; 2X lower | Supports the **DNA oligo with T7 promoter** that the oligo export produces; it is the same template architecture (T7 promoter, DR, spacer; the lab constructs also carry a terminator, which the export does not add) | Duplicate, high standard deviation at 1X; the lab constructs were designed in Benchling, and we do not claim they were generated by the export |
| Tandem array of five guides (Results 4, Fig. 12; Engineering cycle 4) | One transcript with five DR-spacer units, one of them complementary to the target | Signal clearly above the no-Cas control: Cas13a processes the array to some degree | Supports the idea behind K guides per tube in the pooling layout | Technical duplicate. It is **not** known whether all five positions are released or only the first; the single-position constructs (cycle 4, iteration 2) have no result yet |

What the lab data do **not** show: that the ranking weights pick better guides, that the substitution rule discriminates alleles,
that the pooling layout works on a real pooled screen, or that the Frida and BioBricks adapters preserve processing. The default
limit of K = 5 guides per tube is a working assumption supported only by the tandem experiment above.

The negative result has a direct consequence for the software, recorded in the README and in
[BIOLOGICAL_REPORT.md](../BIOLOGICAL_REPORT.md): the *Mismatches vs other allele* column counts differences but cannot tell a G-U
wobble from a destabilising mismatch, and it ignores where in the spacer the mismatches fall. A wobble-aware, position-aware
discrimination score is the first item of the future work.

## 4. Hand checks against the real sequences

From a one-off review written from a CRISPR-diagnostics scientist's seat on 2026-10-16 (its notes are in the git history, in
`SCIENTIST_REVIEW.md` at commit `23aeb41`) and from [BIOLOGICAL_REPORT.md](../BIOLOGICAL_REPORT.md): the sickle-cell (rs334, Glu6Val) design was rebuilt from the NCBI sequence and
the rank-1 spacer is the reverse complement of the mutated window with the documented engineered mismatch; for F508del the
wild-type and deleted sequences were rebuilt, the top mutated spacers occur in the deleted sequence and not in the reference and
sit 6 to 8 mismatches from any wild-type window, while the original-sequence spacers occur in the reference only; the pooling
arithmetic for V = 100, K = 5 (40 wells for 2D fragmented) matches the written formulas.

## 5. Performance

Measured on 2026-10-18 on a Windows 11 laptop with Docker (the Docker VM had 16 CPUs and about 2.9 GiB of memory; the compose file
limits the container to 1.5 CPUs), the `grna` CLI inside the running container, live NCBI, wall-clock including .NET start-up and
the NCBI download. One measurement each, so read them as orders of magnitude:

| Task | Time |
|---|---|
| `design` of `NM_000492.4:c.1521_1523del` (F508del, a transcript record) | 7.8 s and 10.2 s (two runs) |
| `design` of `rs334` (dbSNP resolution, both strands) | 9.9 s |
| `design` of `NC_000017.11:g.7674220C>T` (a chromosome record) | 27.5 s and 38.1 s (two runs) |
| `disease "cystic fibrosis"` (1,517 ClinVar records) | 16.8 s |
| `pool --count 100` (K = 5, 96-well plates) | 1.1 s |
| `pool --count 100000` (K = 5, 96-well plates, 40,000 tubes) | 96.8 s |

The design times are dominated by the NCBI download: the whole accession is fetched, so a chromosome record (`NC_`) takes
several times longer than a transcript (`NM_`). The container held about 870 MiB after these runs. The README and
[runtime-contract.md](runtime-contract.md) describe the concurrency model and the memory behaviour (the 4 GB index is
memory-mapped and reclaimable).

## 6. Known limits

1. Offtargets are counted against the genome, not the transcriptome.
2. The substitution rule's positions are not settled in the literature or in the lab; the ranking weights and the GC range are
   heuristics, and the K = 5 limit is a working assumption.
3. Allele discrimination is a count of differing letters. It ignores their position and type (G-U wobble pairs).
4. Only the LwaCas13a direct repeat is built in, and no protospacer-flanking rule or target-site accessibility is modelled.
5. A typed HGVS has no known gene orientation; `c.` positions work on transcripts only.
6. ClinVar and Monarch are queried live, so counts change; NCBI's OMIM link is looser than "this record's condition is this
   disease", and a search reads at most 3,000 records.
7. The pooling layout and decoder have not been tested on a real pooled screen, and decoding assumes few simultaneous true hits.
8. The score is a rank percentile for comparing candidates of one variant, not a prediction of activity.

## 7. Reproduce

```bash
dotnet restore --locked-mode
dotnet test DiseaseMutationsAppTests                      # 1,124 cases
E2E_INSTALL_BROWSERS=1 dotnet test DiseaseMutationsApp.E2E   # 70 browser tests (first run downloads Chromium)
GRNA_LIVE_TESTS=1 dotnet test --filter Category=Live      # contract tests against the real services
docker run --rm --entrypoint grna disease-mutations-app:latest doctor --network   # real Bowtie index, ViennaRNA, NCBI
```
