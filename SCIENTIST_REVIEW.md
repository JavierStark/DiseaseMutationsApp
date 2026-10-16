# Diana: review from a translational CRISPR-Dx scientist's seat

Date: 2026-10-16. Reviewer persona: a Cas13 diagnostics lab choosing guides that tell a pathogenic variant from wild type.
Tested: the already-running `disease-mutations-app:latest` container (healthy; `grna doctor --network` all PASS, Bowtie 1.3.1,
ViennaRNA 2.7.2). I did not check that this image matches the commit of this worktree.

## What I actually did

| Workflow | Where | Result |
|---|---|---|
| `rs334` + `NC_000017.11:g.7674220C>T` in the Builder | web | 25 jobs created (see 2.3); all finished; HbS result checked by hand |
| Disease search "phenylketonuria" (923 ClinVar records) | web | works, table readable |
| Disease search "sickle cell disease" (80), "cystic fibrosis" (1,517) | CLI | 8 s and 20 s, exit 0, CSV with gene, classification, stars, conditions |
| F508del (two ClinVar notations) design | CLI | 85 s for two variants; verified results against the real sequence (see 1) |
| `pool --count 100`, `fold`, bad input (wrong ref base, `c.`, spacer 12) | CLI | 40 wells matches the documented formula; errors clear, exit code 2 |
| Pooling page | web | only opened; **decoder, plate map, shortlist, oligo CSV, light theme, phone width not exercised by me** |
| Literature checks | web | Gootenberg 2017 (SHERLOCK), Cas13 design papers |

## 1. What is good (verified)

- **Sickle-cell result is correct.** Sequence pair `...CCTGAGGAGAAG` / `...CCTGTGGAGAAG` (Glu6Val). The rank-1 spacer is the reverse
  complement of the mutated window, with the documented engineered mismatch next to the SNP.
- **F508del, deletion junction.** I rebuilt the wild-type and deleted sequence from NCBI myself. Rank-1 and rank-2 mutated
  spacers occur in the deleted sequence and not in the reference, and sit 6 to 8 mismatches from any wild-type window. The
  original-sequence spacers occur in the reference only. So junction-spanning guides do come first, even though the "0 alignments
  ranks best" rule was not designed for that.
- **Input validation is strict and honest**: a wrong reference base, `c.` notation and an out-of-range spacer are refused with
  specific messages and exit code 2. This prevents silent wrong designs. Few academic tools do this.
- **Provenance** (parameters in the CSV header, `Strand` column, resumable batches, session JSON, permalinks) suits lab notebooks.
- **Disease to variants** is fast, shows ClinVar stars and the conditions of each record, and states its limits. The doctor check is useful.
- Pooling arithmetic (V=100, K=5 gives 40 wells for 2D fragmented) matches the written formulas.

## 2. Scientific problems (ordered by impact)

### 2.1 Off-target check is against the genome, but Cas13 targets RNA  [high]
Bowtie runs on GRCh38 DNA. Cas13 sees transcripts. A spacer can hit the genome only in an intron (irrelevant) and miss a
paralog or a spliced junction (relevant). The alignment column therefore answers the wrong question for a diagnostic or
knockdown experiment. It also ignores other species' transcripts when the sample is a pathogen or mixed matrix.
*Fix:* offer a transcriptome index (RefSeq/Ensembl, spliced) as the default; keep the genome as optional. Say in the UI
what the index is.

### 2.2 No wild-type discrimination metric  [high for Dx]
A diagnostic guide must be reactive on the variant and silent on wild type. The app shows two separate tables (mutated,
original) and never says how the *mutated* guide behaves on the *wild-type* target. I had to compute it myself (6 to 8 mismatches
for the top F508del guides, but only **1** mismatch for rank-18 guides, which cannot discriminate). Add per-guide:
mismatch count and position(s) against wild type, whether the variant lies inside the seed/central region, and a "discriminates: yes/no" flag.
Guides with a lone mismatch to wild type should be visibly marked as non-discriminating.

### 2.3 rsID expansion creates noise  [high for usability]
`rs334` produced **24 variants over 4 accessions (NG_000007.3, NG_059281.1, NG_046672.1, NG_042296.1) x 3 alt alleles x 2
strands**. Only A>T (HbS) is the sickle allele; A>G and A>C are other dbSNP alleles at that position, and three of the four
accessions are other records of the locus. A scientist who typed one rsID waits for 25 jobs and must work out which is
meaningful. *Fix:* default to the clinically significant allele(s) and the canonical RefSeqGene/MANE accession, with a "show all"
toggle; show gene name and consequence in each tab.

### 2.4 Which strand is the transcript? Not shown  [high]
Cas13 needs a spacer complementary to the RNA. For a minus-strand gene (HBB, TP53, PAH, all in my tests) a design from an
`NC_` coordinate on the "Normal" tab yields a spacer with the *mRNA's own sequence*, which will not bind it. The `NG_`
accession from rsIDs happens to be in gene orientation, so rs334 "Normal" is correct, but the Disease-variants page sends 63 genomic
`NC_` HGVS (PKU) and the README example uses `NC_000017.11`. Nothing in the UI says "gene is on minus strand; use the
Complement tab". *Fix:* annotate gene, transcript and orientation; label tabs "targets mRNA / targets antisense" instead
of Normal/Complement; or pick the correct one by default.

### 2.5 Substitution special rule: placement not supported by what I could verify  [high; needs the authors' source]
The rule puts the variant at index N-3 (3rd base from the spacer's 3' end) and the synthetic mismatch at N-4. SHERLOCK
(Gootenberg et al. 2017) reports the best specificity with the SNP at **position 3** of the spacer and the synthetic mismatch
at **position 5**, i.e. two bases apart, not adjacent. My searches could not retrieve the supplement that fixes whether
positions are counted from the 5' (direct-repeat-proximal) end, so I cannot say the app is wrong. Two things are certain
from the app's own documents: the variant sits outside the configured seed (10-17), and `BIOLOGICAL_REPORT.md` itself says the
literature source is missing. Also the rule *replaces the whole ranked list with one guide*, so the user cannot choose among mismatch
positions. *Fix:* cite the source, state the numbering, let users tile the mismatch over positions (SHERLOCK did) and
show every variant (position of SNP x position of mismatch) with predicted discrimination; keep ordinary ranking visible.
The rule is skipped silently for non-substitutions (the app warns only near accession ends).

### 2.6 "0 alignments ranks best" is a trap  [medium]
For deletions, insertions and duplications the top ~15 candidates all have 0 alignments, so Bowtie adds nothing and ranking falls
to fold energy. The docs admit this. In the F508del data, 17 and 14 candidates tie at 0 before any 1-alignment guide appears,
and the first-ranked guide has 28.6% GC (GC score about 0.71) because GC is only third in the sort key. A scientist wants a
junction-spanning requirement ("covers the junction by at least n bases on each side") rather than a lucky consequence of an
alignment count.

### 2.7 Equivalent notations give different guide lists  [medium]
The two ClinVar HGVS for p.Phe508del (`117559592_117559594del` and `117559593_117559595del`) are different nucleotide
changes producing the same protein (ATC ATT vs ATC ATC ... ), so differing outputs are legitimate, but nothing tells the user.
Add a note when two records encode the same protein change, and normalise (HGVS 3' rule) when they encode the same DNA.

### 2.8 Scoring parameters are borrowed or unsourced  [medium]
The report itself marks as "needs source": GC 40-60% (a DNA-targeting heuristic), the homopolymer rule (Pol III termination
is about U runs and matters only for Pol III-expressed guides; irrelevant for synthetic RNA or in-vitro transcription with T7), the
5-guide tandem limit, and the mismatch placement. My search found no Cas13-specific GC range. Energy: the app prefers the
*least stable* fold of the whole crRNA (scaffold + spacer), but the scaffold's own hairpin is needed for Cas13 loading, so
what matters is that the **direct-repeat hairpin stays intact** and the **spacer is unstructured**. Report both, e.g. spacer
accessibility and a "scaffold hairpin preserved" check, instead of one MFE.
Also missing: target-site accessibility on the *target RNA* (local folding), which strongly affects Cas13 knockdown/detection;
the PFS rule for orthologs that have one (LshCas13a); and the Cas13 ortholog itself. The fixed 36 nt scaffold is
LwaCas13a-style and the README says other enzymes are not planned yet. Say "LwaCas13a" in the UI; Cas13b/d users are excluded.

### 2.9 Biological relevance of the variant list  [medium]
The ClinVar table (PKU) includes synonymous (p.Leu369=), canonical splice (c.509+2T>C) and frameshift records; a Cas13 guide
on genomic coordinates of a splice-site change may not exist in the mature transcript. Gene is sometimes the wrong locus
label (`LOC126861615` for PAH variants). The list is unsorted by relevance and has no population frequency, so it cannot
prioritise targets (for Dx: prevalent; for therapy: expressed). Records of compound haplotypes carry `rs334`
(HbS Travis, HbS Antilles) and would be designed as if they were plain HbS. Add gnomAD frequency, consequence type
(missense/nonsense/splice/indel), exon/intron flag, and warn about multi-variant records.

### 2.10 `c.` coordinates are refused  [medium]
Labs write `c.` and `p.` (the ClinVar rows even show `c.`/p.), yet the tool accepts only `g.`. A one-line
CDS-offset conversion from the NCBI record is the single biggest convenience gain. Until then, show the `g.` equivalent of
every `c.` row on the Disease page so a user can see what will be sent.

## 3. Missing for a real lab workflow

1. **Specificity panel for Dx**: score each guide against the near-neighbours it must not detect (paralogs, other alleles at
   the same site, other species) and across *all* common alleles at the target (SNPs under the guide kill detection).
2. **Variant-in-population check**: flag common SNPs inside the spacer window (gnomAD), since they cause false negatives.
3. **Assay context**: choice of readout (SHERLOCK/HOLMES), RPA primers for the amplicon, T7 promoter handling is present only
   in the oligo CSV; no RPA primer or amplicon design.
4. **Export of a lab-ready record**: protocol-neutral oligo order file exists (good); add IDT/Twist templates and a plate
   map in one PDF/Excel file, and a methods text stating tool versions, index, parameters (reproducibility).
5. **Run history and comparison** (already on the roadmap) and a persistent project, not only per-browser state; a refresh
   loses results by design.
6. **Experimental validation loop**: import wet-lab results (fluorescence per guide) and re-rank; the score is currently a
   rank-percentile with no physical meaning, so "Score 100 %" for the only candidate is misleading. Show raw metrics and
   confidence, not a relative score.
7. **Compute transparency**: a design of two variants took 85 s; 25 jobs took roughly 1 to 2 minutes of waiting in the UI. Show
   per-variant time and a total estimate.

## 4. Usability observations (web)

- Layout is clean; sidebar, status page and error messages are good. The Builder's "Analysing n of N" counts hidden jobs (25 from
  2 inputs) without explaining that an rsID expands.
- Variant tabs are named by accession only (`NG_000007.3:g.70614A>T`); no gene symbol or amino-acid change. A scientist
  thinks "HBB E6V", not an NG coordinate.
- The Disease page defaults to the first Monarch hit; the list contained a pig model, a non-human entry and an
  unrelated umbrella class ("inborn disorder of amino acid metabolism", which with "Also search its subtypes" could be huge). Filter to human diseases, and warn before searching umbrella classes.
- The Disease table shows `c.` notation but the Builder cannot take it (2.10).
- Not tested by me: keyboard accessibility, mobile layout, dark/light parity, pooling decoder interaction. The README says the
  automated E2E suite covers them.

## 5. Suggested priorities

1. Transcriptome off-target index and wild-type mismatch/discrimination columns (2.1, 2.2).
2. Show transcript orientation and gene, auto-pick the targeting strand; collapse rsID expansion to the relevant allele (2.4, 2.3).
3. Cite and make configurable the mismatch rule (SNP position x mismatch position); stop replacing the full ranking (2.5).
4. `c.` to `g.` conversion and gene/consequence labels everywhere (2.10, 4).
5. Replace the borrowed heuristics with sourced Cas13 parameters (accessibility, PFS, scaffold-hairpin check) (2.8).
6. Population-variant and near-neighbour specificity checks; amplicon/RPA support (section 3).

## 6. Limits of this review

Single sitting; one live container; NCBI and Monarch were live (results change monthly). Sickle and F508del guides were checked
by script against NCBI sequence; the scoring formulas were taken from `BIOLOGICAL_REPORT.md` and the observed outputs, not from a
code audit. Literature statements come from web search results that could not fully retrieve the Gootenberg supplement, so the
numbering convention in 2.5 remains unconfirmed. No wet-lab data was used, so no claim is made about guide *activity*.

Sources: Gootenberg et al. 2017, [Science](https://www.science.org/doi/10.1126/science.aam9321) and
[PMC5526198](https://pmc.ncbi.nlm.nih.gov/articles/PMC5526198/); Cas13 design background in
[PMC11564092](https://www.ncbi.nlm.nih.gov/pmc/articles/PMC11564092/) and
[PMC7294996](https://pmc.ncbi.nlm.nih.gov/articles/PMC7294996).
