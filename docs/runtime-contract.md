# Runtime contract

What the gRNA library and the web app need from the machine they run on, what was verified, and the rules that
keep it reproducible. If you change a dependency, change it here.

## Native dependency matrix

| Dependency | Exact version | Where pinned | Verification |
|---|---|---|---|
| Bowtie (`bowtie/bowtie-align-s`) | **1.3.1** (Bowtie *1*, not Bowtie 2) | committed binary; SHA256 `45c58c69a10577e84e459e6b76d6c8f17ab030a5510d4b6399e6434228a9eaca`, asserted in `Dockerfile` and shown by `grna doctor` | Matches the `bowtie-align-s` inside the upstream `bowtie-1.3.1-linux-x86_64.zip` release asset (GitHub, tag `v1.3.1`). `--version` reports 1.3.1. |
| GRCh38 index | `s3://genome-idx/bt/GRCh38_noalt_as.zip`, 3,749,245,718 bytes, S3 last-modified 2026-05-26, ETag `c00cd594108b9cee4019315af8e2ef79-447` | `Dockerfile.bowtie-base` (`INDEX_SHA256`) | SHA256 `f12495639adbc9bc676eba68044c6bfb1145e0ca587beaf6c7c41446f9d3c573`, verified at image build. Image tag `grch38-noalt-20260526`. |
| ViennaRNA | **2.7.2** (`viennarna==2.7.2`) | `Dockerfile` (`VIENNARNA_VERSION`) | `import RNA; RNA.__version__` is reported by `grna doctor`. |
| Python | 3.11 (Debian bookworm `python3`) | base image digest | `python3 -c "import RNA"` |
| .NET SDK / runtime | SDK 9.0.306 / runtime 9.0.10 | `global.json`, image digests | `dotnet --version` |
| Base images | `mcr.microsoft.com/dotnet/sdk:9.0.306-bookworm-slim@sha256:81f6d622...7687`, `.../aspnet:9.0.10-bookworm-slim@sha256:3dcb3339...2682` | `Dockerfile*` | digests pinned, checked by `scripts/check-pins.sh` |
| NuGet | every direct and transitive version | `Directory.Packages.props` + committed `packages.lock.json` | `dotnet restore --locked-mode` |

## Facts that were verified, and how

**V1: Bowtie 1 vs Bowtie 2.** Settled empirically against the previously built image: the committed binary is
**Bowtie 1.3.1** (its SHA256 equals the `bowtie-align-s` of the upstream `bowtie-1.3.1-linux-x86_64` release asset, and
`--version` reports 1.3.1), and the baked index `GRCh38_noalt_as` is in **`.bt2` format** (`*.1.bt2 ... *.rev.2.bt2`, ~4 GB).
Bowtie 1.3.x reads that format, so the combination works: `bowtie-align-s -x <base> -c READ -v 2 -k 6 --threads 2 --mm`
returned alignments from the real index. The resolver accepts both families (`.bt2/.bt2l/.ebwt/.ebwtl`), sorts for a
deterministic choice, and lets `GRNA_BOWTIE_INDEX` name the base explicitly; `grna doctor` prints the resolved base.
Output: one tab-separated line per alignment on **stdout** (read index, strand, reference, offset, sequence, qualities,
a count column and, for mismatching hits, a `pos:ref>alt` list) and the `#` summary on **stderr**. Captured real output is a
fixture in `ParsingTests.cs`.

**V3: ViennaRNA output format.** With `viennarna 2.7.2`, `RNA.fold('GCGCAAAAGCGC')` returns a **list**,
`['((((....))))', -5.099999904632568]`, so the old bracket-stripping parser worked, but only by coincidence with an
*unpinned* package. The library no longer scrapes any repr: the Python snippet prints `structure<TAB>energy` itself and
`Parsing.parseFoldLine` owns the format (it still tolerates the legacy list/tuple reprs). `viennarna` is pinned to 2.7.2.
`RnaFoldIntegrationTests` runs the real library whenever python3 + ViennaRNA are present and skips otherwise.

**dbSNP.** `SNP.getHgvsNotationsAsync` calls `https://api.ncbi.nlm.nih.gov/variation/v0/refsnp/{id}`, a *legacy* surface,
and extracts `NG_` notations with a regex. An rsID with no `NG_` mapping yields an empty list (reported in the UI as "no
HGVS notations found"); an HTTP failure now raises `GrnaUpstreamException` (reported as an error). `NM_`/`NC_` notations
are intentionally not returned today. This is an API-surface risk of the same kind that already ended OMIM support.

## Why the index lives in the image

Bowtie is started with `--mm` (memory-mapped index). Served from a Docker volume or a bind mount, those reads are
10x or more slower. So the ~4 GB index is baked into the image layer and the container is sized around it. There is no
index volume in `docker-compose.yml` and there must not be one. The index is also not provisioned at runtime: it is a
property of the image, identical across the local-build and registry-pull routes.

## Process-wide concurrency model

- **Bowtie** is serialised per process by a single-slot semaphore on the `BowtieService` instance (a DI singleton).
  Throughput is one alignment batch at a time per process; scale by replicas, not threads
  ([running-as-a-service.md](running-as-a-service.md)).
- **ViennaRNA** launches are bounded to 2 concurrent Python interpreters per process.
- **Variants**: the web app analyses `Analysis__MaxConcurrentVariants` variants at once (default 2, clamped to 1-4). Each
  variant already runs its two strands' windows concurrently, so 2 variants means up to four heavy sub-tasks in flight.
  rsID resolution is separately bounded (`Analysis__MaxConcurrentDiscovery`, default 4).
- **Sequence cache**: whole NCBI records can be tens of MB, so the cache is bounded by insertion order to
  `SequenceCacheEntries` accessions (default 8), shares one in-flight fetch per accession, and never caches failures.
- **Cancellation** reaches child processes: cancelling a run kills the Bowtie/Python process trees.

## Memory

The index is memory-mapped, reclaimable page cache: it is *not* a hard resident-memory requirement. Verified on a Docker VM
with only ~3 GB: a 25-variant run (including a real rsID expanded to both strands) stayed around 700 MB resident and finished
in under 20 s. More page cache simply keeps more of the index hot. The compose `memory` limit is therefore only an upper
bound (default 4 GB, override with `GRNA_MEMORY_LIMIT`); `grna doctor` warns below 4 GB but the app works. Do not raise
Docker's memory just for this app: it is not needed, and over-provisioning the VM slows builds.

## Configuration

| Variable | Meaning |
|---|---|
| `GRNA_BOWTIE_BINARY` | path to `bowtie-align-s` (default `bowtie/bowtie-align-s`, resolved against the app base directory, then the CWD) |
| `GRNA_BOWTIE_INDEX` | explicit index base path (no suffix); otherwise the first sorted index under `bowtie/indexes` |
| `GRNA_PYTHON` | Python executable (default `python3`) |
| `GRNA_NCBI_API_KEY`, `GRNA_NCBI_CONTACT` | raise the NCBI limit to 10 req/s and identify the tool |
| `Analysis__MaxConcurrentVariants` | variants analysed at once (1-4) |
| `Analysis__VariantTimeoutSeconds` | per-variant timeout (default 600) |
| `GRNA_MEMORY_LIMIT`, `GRNA_BIND`, `GRNA_MAX_CONCURRENT_VARIANTS`, `GRNA_APP_IMAGE`, `GRNA_BOWTIE_BASE`, `GRNA_REGISTRY` | Compose / `start.sh` knobs |

## Health checks (`grna doctor`, `/healthz`, `/diagnostics`)

One routine, three surfaces. It checks: working directory, Bowtie binary (present, executable, version), Bowtie index
(resolved base), Python + ViennaRNA import and version, optionally NCBI reachability, and available memory. `/healthz`
returns JSON and HTTP 503 when any required check fails; the container `HEALTHCHECK` uses it.

## Registry (optional)

`.gitlab-ci.yml` runs only the pinning gate (`pins`) and the tests (`test`); it builds no images, because iGEM's runners have no privileged Docker. Images are built locally (`./start.sh`). `./start.sh` can still pull prebuilt images
from a registry when `GRNA_REGISTRY` is set (`<registry>/bowtie-base:grch38-noalt-20260526` and `<registry>/app:latest`), and falls
back to a local build when the registry is unreachable; `--build-local` never pulls. Rebuilding the base manually:
`./start.sh --rebuild-bowtie`, or `docker compose --profile base build bowtie-base`. Run `scripts/check-pins.sh` yourself before
pushing to enforce the pinning policy below.

## Dependency pinning policy

No floating, implicit or mutable version anywhere:

- NuGet: exact versions only, in `Directory.Packages.props`; transitive pinning on; `packages.lock.json` committed;
  restore with `dotnet restore --locked-mode`. The .NET SDK is pinned in `global.json`.
- Images: every `FROM` is pinned by `@sha256:` digest; no `:latest` inputs.
- pip: `==` versions only. Downloads (s5cmd, the index zip, the Bowtie binary) are SHA256-verified at build.
- Front-end assets: Bootstrap 5.3.3 and the two font families are vendored with versions and checksums recorded in
  `THIRD-PARTY-NOTICES.md` and [design-system.md](design-system.md); no CDN is used at runtime.

`scripts/check-pins.sh` enforces the mechanical parts. To bump something: change it in the one place above, regenerate the
lock files (`dotnet restore --force-evaluate`), update the checksum/version here, and run `scripts/check-pins.sh` to confirm.
