# Diana

*Diana: precision guide RNA design.* The name is explained in [docs/about-the-name.md](docs/about-the-name.md).

**iGEM Team UMA-Malaga 2026, Software.** This repository (`main` branch) hosts the full source code of the team's software
tool, as the competition requires. Source: <https://gitlab.igem.org/2026/software/uma-malaga/diana-grna-designer>.

Diana is a web application and command-line tool for turning disease-related variants (HGVS notations or rsIDs) into ranked
CRISPR-Cas13 guide RNA spacers, and for planning combinatorial guide pooling onto 96/384-well plates. Spacers are scored for
GC content, homopolymers, off-target alignments (Bowtie) and RNA secondary structure (ViennaRNA), for both the mutated and the
original (wild-type) sequence. It also flags restriction sites (BsaI, BsmBI, SapI, BbsI, EcoRI, PstI, XbaI, SpeI, EcoRV, NotI)
in the DNA you would order, and exports an order-ready oligo CSV.

> **AI assistance.** Parts of this code were written with an AI coding assistant and reviewed by the team. Read
> [.claude/RESPONSIBLE_AI_USE.md](.claude/RESPONSIBLE_AI_USE.md); the team remains responsible for everything committed here.

## Overview

### Diana: guide design (`/`)

- **Inputs**: HGVS (`NC_000017.11:g.7674220C>T`) and rsIDs (`rs334`), several at once. An rsID is resolved to its HGVS
  notations and each is analysed on both strands: a normal tab and a complement (`C`) tab.
- **Results** per variant: original and mutated sequences (mutation highlighted, copy as FASTA), two ranked spacer tables
  (sortable, filterable, paged, with an alignment-risk chip), the complete gRNA (scaffold + chosen spacer), ViennaRNA
  structure and a FORNA diagram link.
- **Runs**: cancellable (whole run or one variant), bounded-parallel, and they survive navigating to another page. A refresh
  restores your inputs (never results: download the CSV report to keep them).
- **Off-target loci**: expand any spacer to see where its target window aligns in the genome (chromosome, position, strand,
  mismatches; up to 50 alignments). The table's alignment chip still saturates at "6+", honestly labelled. Loci are looked up for the spacer exactly as displayed; for the
  substitution special rule's engineered spacer this can differ from the table's count, which is carried over from the unadjusted candidate.
- **Shortlist**: pick spacers across variants, send them to the Pooling page without a CSV round trip, or download an
  order-ready **oligo CSV** (DNA, optional T7 promoter, top/bottom pairs, plate wells addressed like the pooling plan).
- **Files and sessions**: load inputs from a plain list, a Diana CSV report or a saved session; *Save session* downloads a
  JSON document (inputs, spacer/seed, shortlist) that `grna design --input session.json` reads too.
- **Permalinks**: *Copy link to this design* encodes `?hgvs=...&spacer=28&seed=10-17`; opening it starts the run.
- **CSV report**: per rsID or all at once, with the spacer/seed parameters in a leading `#` line and a `Strand` column.

### Guide Pooling (`/pooling`)

Screening a whole variant panel one guide per well is infeasible, so guides are pooled combinatorially: at most K guides per
tube (a biological limit, default 5), laid out so each guide appears in R wells (2 for the 2D models, 3 for 3D) and a positive
readout still identifies the guide. Choose a guide count or paste a guide list / Diana CSV report; the page compares the
three models (2D fragmented, 2D matrix, 3D), shows the plate map and the tube list, and exports CSV. **Decode screening
results**: enter the positive wells (`A1, B3`, `Plate 2 - C4`, or tube numbers like `#12`) to get the implicated guides, with
overlapping multi-hit collisions flagged as ambiguous instead of hidden.

### Diagnostics (`/diagnostics`, `/healthz`)

The same preflight as `grna doctor`: Bowtie binary and index, Python and ViennaRNA, NCBI reachability, memory.

### `grna` command line

```
grna doctor [--network]
grna resolve rs334
grna design --hgvs NC_000017.11:g.7674220C>T --spacer 28 --seed 10-17 [--complement] [--format csv|json|tsv]
grna design --input variants.txt|session.json --out report.csv   # batch, resumable, bounded concurrency
grna fold "GAUUUAGACUACCCC..."
grna pool --guides report.csv --capacity 5 --plate 96 --model auto --out plan.csv
```

`design --format csv` emits the same schema as the web export, so a CLI report pastes straight into the Pooling tab. Exit codes
distinguish bad input (2), a missing native dependency (3) and an upstream failure (4).

## Architecture

- **`DiseaseMutationsApp/`**: Blazor Server (.NET 9). One interactive render mode declared once on the router, prerendering off.
  `AnalysisRunner` (scoped per circuit) owns runs; `AppStateService` holds per-circuit UI state.
- **`gRNA/`**: F# library: HGVS parsing, sequence retrieval (NCBI), spacer finding/scoring/ranking, Bowtie and ViennaRNA
  wrappers, pooling and decoding, diagnostics, CSV schema. Usable on its own: [docs/using-the-library.md](docs/using-the-library.md).
- **`gRNA.Cli/`**: the `grna` tool over the library.
- **Native tools**: Bowtie 1.3.1 (committed binary), the GRCh38 index (baked into the image), Python + ViennaRNA 2.7.2.

```
HGVS / rsID -> NCBI sequence -> sliding windows -> Bowtie (off-targets) + ViennaRNA (folding) -> ranked spacers
```

The Docker setup is two images. The **Bowtie base image** (`Dockerfile.bowtie-base`) downloads and checksum-verifies the
GRCh38 index once; the **app image** (`Dockerfile`) derives from it, adds Python/ViennaRNA, the published app and the CLI, and
runs as a non-root user with a `/healthz` HEALTHCHECK. **The index stays in the image on purpose**: Bowtie reads it
memory-mapped, and a volume or bind mount would make alignment 10x+ slower
([why](docs/runtime-contract.md#why-the-index-lives-in-the-image)).

## Installation

Requirements: Docker with Compose v2, about **10 GB free disk** for the first build (the ~3.7 GB index zip and the unpacked
index coexist), and about **3 GB RAM available to Docker** (the index is memory-mapped reclaimable cache, so more RAM only keeps more of it hot; no need to raise Docker's memory).

Get the code:

```bash
git clone https://gitlab.igem.org/2026/software/uma-malaga/diana-grna-designer.git
cd diana-grna-designer
```

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

## Using Diana

1. Enter variants (comma-separated) and the spacer size and seed range (defaults 28 and 10-17, inclusive, 0-based in the spacer).
2. **Run analysis**. rsIDs resolve first, then variants are analysed two at a time. Cancel the run, or one variant, any time.
3. Pick a tab per input; for rsIDs a second strip lists each resolved variant on both strands. Tabs use arrow keys.
4. Press **Use** on a spacer to build the complete gRNA, copy it, fold it, or add it to the shortlist.
5. Download the CSV report, or send the shortlist to Pooling.

Tab status is shown by shape, text and label (loading ring, ready dot, failed triangle, cancelled square), never colour alone.
If a variant fails it shows the reason and a **Retry this variant** button; other variants are unaffected.

### gRNA metrics

- **GC score**: 1.0 for GC content from 40% to 60% *inclusive*; below 40% proportional (`gc / 40`), above 60%
  `(100 - gc) / 40`. Always in 0-1.
- **GC content**: raw GC percentage, two decimals.
- **Homopolymers**: runs of 4 or more identical bases; 0 is best.
- **Alignments**: Bowtie hits of the DNA window (up to 2 mismatches). 1 is a unique target; the count saturates at 6, shown as
  "6+ saturated".
- **Energy**: minimum free energy (kcal/mol) of the *complete* gRNA (scaffold + spacer); closer to zero is preferred.
- **Score**: 0-1, from the rank after sorting by (Alignments, Energy, GC score, Homopolymers); ties share a rank.

Substitution variants get the **special rule**: when the mutation lands at the right position near the 3' end, a single
mismatch-engineered spacer (`Rank = 1`, `Score = 1.0`) replaces the whole ranked list for that variant, by design
([BIOLOGICAL_REPORT.md](BIOLOGICAL_REPORT.md) section 4.4).

The complete gRNA is the 36 nt scaffold `GAUUUAGACUACCCCAAAAACGAAGGGGACUAAAAC` followed by the spacer (the reverse complement of the
target window, as RNA).

### HGVS examples

```
NC_000017.11:g.7674220C>T          substitution      NM_000546.6:c.215_217delinsAG   deletion-insertion
NM_000546.6:c.215_217del           deletion          NM_000546.6:c.215_217dup        duplication
NM_000546.6:c.215_216insA          insertion         NM_000546.6:c.215_217inv        inversion
```

## Project layout

```
DiseaseMutationsApp/   Blazor Server app (Pages, Components, Services, Shared, wwwroot)
gRNA/                  F# library
gRNA.Cli/              the grna command-line tool
DiseaseMutationsAppTests/  NUnit + bUnit tests
docs/                  about-the-name, runtime-contract, using-the-library, running-as-a-service, design-system
examples/standalone.fsx    run the library from F# Interactive
scripts/check-pins.sh      dependency pinning gate
Dockerfile, Dockerfile.bowtie-base, docker-compose.yml, start.sh, start.ps1
```

## Testing

```bash
dotnet restore --locked-mode
dotnet test
```

Tests need no container and no network: the library's pure logic (scoring, parsing, pooling and decoding, CSV round trips),
the `AnalysisRunner` (concurrency bound, cancellation, per-variant cancel, timeouts, retry) against a fake pipeline, and the
components with bUnit. `RnaFoldIntegrationTests` run the real ViennaRNA when `python3` and `RNA` are available and skip
otherwise. The GitLab pipeline (`.gitlab-ci.yml`) runs the pinning gate and these tests; it builds no images, so run `grna doctor` in the locally built image yourself.

## Data and large files

This repository holds source code only. The GRCh38 Bowtie index (about 3.7 GB) is **not** stored here: the base image downloads it
from the public Bowtie index bucket at build time and verifies its SHA-256 (`Dockerfile.bowtie-base`). Only the Bowtie 1.3.1
binary (about 14 MB, `bowtie/`) and a Windows ViennaRNA wheel (about 2 MB, `DiseaseMutationsApp/`) are committed. Any dataset,
model or other heavy artifact produced by the team belongs on [Zenodo](https://teams.igem.org/go/deliverables/software/zenodo)
and should be referenced from this README.

## Configuration

See the table in [docs/runtime-contract.md](docs/runtime-contract.md#configuration): `GRNA_BOWTIE_BINARY`, `GRNA_BOWTIE_INDEX`,
`GRNA_PYTHON`, `GRNA_NCBI_API_KEY`, `GRNA_NCBI_CONTACT`, `Analysis__MaxConcurrentVariants`, `Analysis__VariantTimeoutSeconds`.
Hosting defaults live in [`Program.cs`](DiseaseMutationsApp/Program.cs) (circuit retention, message size, timeouts) and the
compose file ([`docker-compose.yml`](docker-compose.yml)); the container runs in `Production`.

## Troubleshooting

Start with `/diagnostics` or `grna doctor`: each failing check names its remedy.

- **Bowtie index not found**: the base image was not built or a custom `GRNA_BOWTIE_INDEX` is wrong. `./start.sh --rebuild-bowtie`.
- **ViennaRNA / Python errors**: `pip install viennarna==2.7.2`; check `GRNA_PYTHON`.
- **"No HGVS notations found" for an rsID**: dbSNP returned no `NG_` mapping (different from a lookup error).
- **Connection lost banner**: the server keeps the run going for about 10 minutes and the page rejoins automatically.
- **Slow alignments**: never move the index to a volume (it must stay in the image layer).
- **NCBI rate limits**: set `GRNA_NCBI_API_KEY` and `GRNA_NCBI_CONTACT`.

## Security

Single-tenant local tool: no authentication, bound to loopback by default, `Production` mode in the container (no stack traces),
non-root user. If you publish it, put an authenticating reverse proxy in front.

## Contributing

Fork, branch, commit, open a merge request on GitLab. Contributions are accepted under the project's MIT license. Add unit tests for new
behaviour; update this README and, for algorithm changes, [BIOLOGICAL_REPORT.md](BIOLOGICAL_REPORT.md). Dependencies are
pinned (see the pinning policy in [docs/runtime-contract.md](docs/runtime-contract.md#dependency-pinning-policy)); run
`scripts/check-pins.sh` before pushing and bump versions only in the central places listed there.

## License

MIT (an OSI-approved open-source license, as iGEM requires), see [LICENSE](LICENSE). The image also contains components under other licenses, notably **Bowtie (Artistic License 2.0)** and
**ViennaRNA (non-commercial-use license)**: see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Authors

iGEM Team UMA-Malaga 2026. Initial work by Javier Torralbo Cortes. The Attributions Form on the team wiki is the authoritative
record of contributors and sources.

## Acknowledgments and references

Bowtie (Langmead et al.), ViennaRNA (Lorenz et al.), forna (Kerpedjiev et al.), NCBI and dbSNP, the Genome Reference Consortium,
the HGVS nomenclature, and the CRISPR community. Licenses for these components are stated in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

### What each resource asks us to cite

We checked each resource's own citation guidance (July-October 2026) and cite accordingly:

| Resource | What it asks | Reference |
|---|---|---|
| Bowtie | Cite Langmead et al. 2009 | 1 |
| ViennaRNA | Main references: Lorenz et al. 2011 and Hofacker et al. 1994 (feature-specific papers are suggested, not required) | 2, 3 |
| forna | Kerpedjiev et al. 2015 | 4 |
| dbSNP | Cite the *Nucleic Acids Research* paper (Phan et al. 2025); to point at a single variant, cite its accession (e.g. rs334) | 5 |
| NCBI (sequences, E-utilities, Variation Services) | Acknowledge NCBI/NLM; automated clients should send `tool` and `email` (we send `tool=gRNA` and `email=$GRNA_NCBI_CONTACT`) | 6-8 |
| GRCh38 | Cite the assembly (Schneider et al. 2017) | 9 |
| HGVS nomenclature | Cite den Dunnen et al. 2016 rather than the web site | 10 |

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
12. Doench, J. G. et al. (2016). Optimized sgRNA design to maximize activity and minimize off-target effects of CRISPR-Cas9. *Nature Biotechnology*, 34(2), 184-191. https://doi.org/10.1038/nbt.3437
13. Bryson, J. W. (2025). Array Assembler Provides Greatly Simplified crRNA Array Design for CRISPR Cas12 and Cas13 Variants. *ACS Synthetic Biology*. https://doi.org/10.1021/acssynbio.5c00100
14. Karimi, M. et al. (2025). Integrating AI and CRISPR Cas13a for rapid detection of tomato brown rugose fruit virus. *Scientific Reports*, 15(1). https://doi.org/10.1038/s41598-025-11405-z
15. Gruber, A. R., Lorenz, R., Bernhart, S. H., Neuböck, R., & Hofacker, I. L. (2008). The Vienna RNA websuite. *Nucleic Acids Research*, 36(Web Server issue). https://doi.org/10.1093/nar/gkn188

## Future enhancements

- Re-enable OMIM to rsID conversion if a CAPTCHA-resistant approach appears (blocked upstream; `gRNA/Omim.fs` is excluded from the build).
- Run history and design comparison (reopen or diff earlier runs with different spacer/seed).
- Enzyme presets with a configurable scaffold: today the 36 nt Cas13 scaffold and the 28 / 10-17 defaults are fixed, and
  presets for other Cas variants need their scaffolds and seed windows verified against the literature first.

Not planned: additional genome assemblies (each adds ~4 GB to the image), authentication, ClinVar integration.

## Migration notes

The app moved from Blazor WebAssembly plus a separate API to a single Blazor Server process: one container, direct calls into the
F# library, no HTTP hop. The cost is a persistent circuit (hence the generous reconnect policy and the inputs-only refresh
persistence) and no static hosting. The earlier CORS setup was removed; nothing in the app uses cross-origin requests.

## Support

Open an issue in the GitLab project, or contact javiertorralbocortes@gmail.com.
