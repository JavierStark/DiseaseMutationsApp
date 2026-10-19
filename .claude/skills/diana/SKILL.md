---
name: diana
description: Install, start, stop, health-check and drive Diana (this repository), with a strong focus on its `grna` command-line tool (doctor, resolve, disease, design, fold, pool). Use whenever a task involves building or running the Diana Docker images or web app, running `grna` commands or batch reports, piping CLI output into scripts or other tools, choosing between the Docker and native .NET routes, or troubleshooting an install (Bowtie binary/index, Python/ViennaRNA, NCBI, exit codes).
---

# Diana: install, run and use the `grna` CLI

Diana is a .NET 9 tool with two front-ends over one F# library (`gRNA/`): a Blazor Server web app
(`DiseaseMutationsApp/`, port 5000) and the **`grna` CLI** (`gRNA.Cli/`). This skill is about operating the software:
installing it, starting/stopping it and driving the CLI. The scoring algorithm is out of scope here
(see `BIOLOGICAL_REPORT.md` if a task really needs it).

**Ground rule.** Never invent CLI output, scores, sequences or timings. Run the command, read its real stdout/stderr and
exit code, and report those. If a command cannot run (no Docker, no image), say so instead of guessing what it would print.

## 1. Pick the route (decide this first)

Which `grna` subcommands work where:

| Subcommand | Needs | Docker image | Native `dotnet` on Windows | Native on Linux/WSL |
|---|---|---|---|---|
| `pool` | nothing | yes | yes | yes |
| `resolve` | internet (NCBI dbSNP) | yes | yes | yes |
| `disease` | internet (Monarch API + NCBI ClinVar) | yes | yes | yes |
| `fold` | Python 3 + `viennarna==2.7.2` | yes | yes, with setup (see references/install-and-operate.md) | yes, with `pip` |
| `doctor` | (reports on the others) | yes | runs, reports FAIL for Bowtie | yes |
| `design` | internet + **Linux** Bowtie binary + ~4 GB GRCh38 index + ViennaRNA | **yes (use this)** | **no** (the committed Bowtie is a Linux ELF) | only with a manually provisioned index |

**Default: use the Docker image** (`disease-mutations-app:latest`). It contains everything, already verified by
`grna doctor`. Go native only for `pool`/`resolve`/`disease`/`fold`, or when developing the CLI itself.

## 2. Check the current state before doing anything

```bash
docker image inspect disease-mutations-app:latest --format '{{.Id}}'              # app image (includes the CLI)
docker image inspect disease-mutations-bowtie:grch38-noalt-20260526 --format '{{.Id}}'  # index base image
docker ps --filter name=disease-mutations-app --format '{{.Names}} {{.Status}}'     # is the web app running?
curl -s http://localhost:5000/healthz                                              # JSON; HTTP 503 if unhealthy
```

- Both images present: skip installation; the CLI is usable immediately (it does **not** need the web app running).
- App image missing: install (section 3).

## 3. Install / start

Requirements: Docker with Compose v2, **~10 GB free disk** for the first build (the ~3.7 GB index zip is downloaded and
unpacked inside the build), ~3 GB RAM for Docker is enough. Run from the repository root:

```bash
./start.sh                  # Linux / macOS / WSL / Git Bash
```

```powershell
.\start.ps1                 # Windows PowerShell (or start.bat, which pauses at the end: avoid it in automation)
```

What it does: preflight (Docker daemon, Compose v2, free disk), builds or pulls the index base image, builds the app image,
`docker compose up -d`, then waits up to 2 minutes for `http://localhost:5000/healthz`, printing `[install] Ready: ...` on
success or the last 40 log lines and a non-zero exit on failure.

- **The first run is long** (the index download dominates). As an agent, run it in the background / with a long timeout
  and watch its `[install]` lines rather than assuming it hung.
- Flags: `--build-local` / `-BuildLocal` (never pull), `--rebuild` / `-Rebuild` (rebuild the app image, e.g. after code
  changes), `--rebuild-bowtie` / `-RebuildBowtie` (re-download the index; implies rebuild). `GRNA_REGISTRY=<registry>`
  makes it pull prebuilt images first (bash script only).
- The index is baked into the image **on purpose** (memory-mapped reads). Never add a volume or bind mount for it.

Validate the install:

```bash
docker run --rm --entrypoint grna disease-mutations-app:latest doctor     # exit 0 = every required check passes
```

Stop / logs / restart: `docker compose down`, `docker compose logs -f app`, `docker compose up -d`. Full lifecycle,
env knobs and the no-Docker route: [references/install-and-operate.md](references/install-and-operate.md).

## 4. Run the CLI (three ways, pick by need)

**A. Wrapper script (recommended for anything that reads or writes files).** Runs the image with the current directory
mounted at `/work`, so relative paths just work and outputs land next to you. Exit code is grna's own.

```bash
.claude/skills/diana/scripts/grna.sh doctor
.claude/skills/diana/scripts/grna.sh design --input variants.txt --out report.csv
```

```powershell
.\.claude\skills\diana\scripts\grna.ps1 design --input variants.txt --out report.csv
```

Use the script's absolute path when your working directory is elsewhere (the data directory is what gets mounted).

**B. Inside the running web-app container** (no host files involved; output via stdout):

```bash
docker exec disease-mutations-app grna design --hgvs 'NC_000017.11:g.7674220C>T' > report.csv
```

**C. Raw `docker run`** (what the wrapper does; useful in CI). Keep the image's working directory `/app`, mount data elsewhere:

```bash
docker run --rm -v "$PWD:/work" --entrypoint grna disease-mutations-app:latest design --input /work/variants.txt --out /work/report.csv
```

If you change the working directory (`-w /work`), you **must** also pass
`-e GRNA_BOWTIE_BINARY=/app/bowtie/bowtie-align-s -e GRNA_BOWTIE_INDEX=/app/bowtie/indexes/GRCh38_noalt_as`, otherwise
Bowtie is looked up under `/work/bowtie/` and `doctor`/`design` fail with exit 3.

## 5. Command cheat sheet (every line below was run against the real image)

```bash
grna --help   (or: grna <command> --help)                    # usage (exit 0)
grna doctor [--network]                                       # PASS/WARN/FAIL lines; exit 0 healthy, 3 otherwise
grna resolve rs334 [rs... ] [--all-alleles]                    # stdout: "<rsID>\t<HGVS>"; default = disease-relevant allele (1 line for rs334), --all-alleles = 12
grna design --hgvs 'NC_000017.11:g.7674220C>T'                # CSV report to stdout (default --format csv)
grna design --hgvs '<HGVS>' --format json|tsv [--out file]    # other formats
grna design --hgvs '<HGVS>' --spacer 23 --seed 10-17 --complement   # parameters (these values are the defaults, minus --complement)
grna design --hgvs '<HGVS>' --repeat none                           # fold the spacer alone; --repeat default (36 nt scaffold) or --repeat <RNA/DNA sequence> too
grna design --hgvs 'NM_000492.4:c.1624G>T' --snp-pos 9 --mismatch-pos 10 --mismatches 2 --bases A>A,G>A,C>C,U>C    # substitution rule: positions from the 3' end, number of artificial mutations, sequence base>guide base (defaults shown); a transcript c. position is converted
grna disease 603903 --format rs --out sickle.txt               # rsIDs of a disease's pathogenic/likely pathogenic ClinVar variants (progress on stderr, needs internet)
grna disease "sickle cell disease" --list                      # the matching diseases (--pick N chooses one, --subtypes widens, --format csv|tsv|json)
grna design --input variants.txt --out report.csv [--concurrency 2] # batch; RESUMABLE (re-run skips variants already in report.csv)
printf 'rs334\n' | grna design --input /dev/stdin --format json     # stdin input (Linux/container; docker needs -i, the wrapper adds it)
grna fold GAUUUAGACUACCCCAAAAACGAAGGGGACUAAAAC                 # 2 lines: sequence, then dot-bracket<TAB>energy
grna pool --count 100 --capacity 5 --plate 96 --model auto    # plate plan CSV to stdout, summary on stderr
grna pool --guides report.csv --out plan.csv                  # plan from a design report (one guide per variant)
grna export session.json --format genbank --t7 --out guides.gb   # shortlist + construct of a saved web session (offline); --format sbol for SBOL 3
grna export report.csv --top 1 --frida --out guides.gb           # best guide per variant of a design report
```

Inputs to `design`: HGVS notations (`g.`, `m.`, `n.`, and `c.` on an `NM_`/`XM_` transcript, which is converted and reported with a `note`)
and/or rsIDs (`rs334` gives its disease-relevant allele on the main gene record, one notation; `--all-alleles` gives every allele
dbSNP lists, 12 for `rs334`; each notation is analysed separately). `--input` takes a text file (one or more per line, `,`/`;` also separate, `#` lines ignored) or a web-app
session `.json`. Exact formats, output schemas, resume rules and pooling details:
[references/cli-reference.md](references/cli-reference.md).

## 6. Gotchas that will bite an agent

1. **Quote every HGVS.** `C>T` unquoted is a shell redirect: `grna design --hgvs NC_000017.11:g.7674220C>T` writes the
   report into a file named `T` and fails on the truncated `...7674220C` (exit 2). Use single quotes in bash and PowerShell.
2. **stdout is data, stderr is progress.** `design` prints `ok   <hgvs>` / `FAIL <hgvs>: <reason>` on stderr; `pool`
   prints its summary on stderr. Redirect/parse stdout only. Never allocate a TTY (`docker run -t`/`exec -t`), it merges them.
3. **Exit codes:** 0 ok, 1 failure (including *partial* batch failure: rows for the successes are still written; when
   *every* variant fails the code is that of the first failed variant in input order),
   2 bad input/arguments (malformed HGVS, wrong reference base, a `c.` position on a genomic record, spacer outside 18-60, an unknown option), 3 missing native dependency (run `doctor`), 4 upstream failure (NCBI). 125 from the wrappers
   or `docker run` = Docker problem (e.g. image not built).
4. **A failed `design` still prints the CSV header** (and provenance line). Check the exit code, not just whether output exists.
5. **Typos are rejected, not ignored.** An unknown option (`--capasity 2`) or a flag that does not apply to the command
   exits 2 and names it. Only `--complement`, `--all-alleles`, `--list`, `--subtypes`, `--network` and `--help` are value-less flags; there is no `--opt=value` form.
6. **Resume semantics.** With `--input` + `--out <file>.csv` + csv format, an existing report is appended to and variants
   already in it (keyed by RS ID + HGVS + strand) are skipped. A report made with a different `--spacer`, `--seed`, `--repeat`, `--snp-pos`, `--mismatch-pos`, `--mismatches` or `--bases` is
   refused (exit 2, the message names both settings): use a new `--out` file when changing those. Running the other strand (`--complement`) into the same
   file is fine and appends its rows.
7. **`--complement` analyses one strand per run:** the opposite strand (the reverse complement, read 5'->3'). For both
   strands run twice; the same `--out` file works. An offtarget count of 0 means "no genomic match" and ranks best (common for indels).
8. **Container working directory:** the image now sets `GRNA_BOWTIE_BINARY` and `GRNA_BOWTIE_INDEX` itself, so `-w` is safe; the overrides in section 4C only matter for an older image or a custom build.
9. **JSON escapes `>`** as `>` in `hgvs`. Use a real JSON parser (`jq`, `ConvertFrom-Json`), not grep.
10. **In PowerShell, prefer `--out` over `>`.** It writes UTF-8 without BOM exactly like the web export; PowerShell
    redirection re-encodes native output.
11. **Port 5000 is shared**: the compose container and `dotnet run --project DiseaseMutationsApp` both bind it. Stop one first.
12. **`design` needs network** (NCBI E-utilities for sequences, dbSNP for rsIDs). Set `GRNA_NCBI_API_KEY` and
    `GRNA_NCBI_CONTACT` for large batches (the wrappers forward them; `docker run` needs `-e`). `disease` needs the network too
    (Monarch and NCBI ClinVar).
13. **The CLI is not the whole app.** The interactive complete-gRNA builder, the oligo CSV, FASTA export, the shortlist and the
    plate-map page are web-app features. The one CLI route to a built construct is `grna export` (GenBank or SBOL 3, from a saved
    session or a design report, with `--frida`, `--biobricks`, `--custom5/3`, `--dr`, `--t7`; offline). To fold a built construct, pass its RNA to `grna fold`. A `design --format rs` list from `grna disease` leaves out
    ClinVar records that have no rsID; use `grna disease ... --format csv` to see their genomic HGVS.
14. **Counts from `disease` change.** ClinVar and Monarch are read live: on 2026-10-18 `grna disease 603903` gave 62 distinct
    rsIDs (80 records read). Never quote a count without running the command.

## 7. Typical end-to-end workflow

```bash
./start.sh                                                         # once (long the first time)
S=.claude/skills/diana/scripts/grna.sh
$S doctor || exit 1                                                # exit 3 => read the FAIL remedies
$S disease 603903 --format rs --out sickle.txt                     # optional: a disease to its ClinVar variants (rsIDs)
printf 'NC_000017.11:g.7674220C>T\nrs334\n' > variants.txt        # or write the variants yourself
$S design --input variants.txt --out report.csv; echo "design exit=$?"   # 1 => some variants failed; re-run resumes
$S pool --guides report.csv --capacity 5 --plate 96 --out plan.csv
```

The chain disease -> design -> pool was run on the real image on 2026-10-18 (`sickle.txt` into `design --input`, then `report.csv`
into `pool --guides`, all exit 0).

## 8. When something fails

Run `doctor` first: every FAIL line is followed by `-> <remedy>`. Then see
[references/troubleshooting.md](references/troubleshooting.md) (symptom -> cause -> fix table, covering install,
CLI, Windows and network failures).

## Reference files

- [references/cli-reference.md](references/cli-reference.md): every subcommand, option, default, input format,
  output schema (CSV/TSV/JSON/pool plan), resume rules, exit-code mapping, environment variables.
- [references/install-and-operate.md](references/install-and-operate.md): installer flags and env knobs, lifecycle
  (start/stop/logs/rebuild/update), image layout, native .NET route, packaging `grna` as a `dotnet tool`, tests and CI.
- [references/troubleshooting.md](references/troubleshooting.md): symptom-driven fixes.
- Repository docs: `README.md`, `docs/runtime-contract.md` (pins, env vars, concurrency), `docs/using-the-library.md`,
  `docs/validation.md` (what each claim rests on, measured timings, known limits), `BIOLOGICAL_REPORT.md` (the science behind
  every column) and `docs/manual-test-plan.md`.
