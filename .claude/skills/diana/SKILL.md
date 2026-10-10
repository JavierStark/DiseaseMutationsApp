---
name: diana
description: Install, start, stop, health-check and drive Diana (this repository), with a strong focus on its `grna` command-line tool (doctor, resolve, design, fold, pool). Use whenever a task involves building or running the Diana Docker images or web app, running `grna` commands or batch reports, piping CLI output into scripts or other tools, choosing between the Docker and native .NET routes, or troubleshooting an install (Bowtie binary/index, Python/ViennaRNA, NCBI, exit codes).
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
| `fold` | Python 3 + `viennarna==2.7.2` | yes | yes, with setup (see references/install-and-operate.md) | yes, with `pip` |
| `doctor` | (reports on the others) | yes | runs, reports FAIL for Bowtie | yes |
| `design` | internet + **Linux** Bowtie binary + ~4 GB GRCh38 index + ViennaRNA | **yes (use this)** | **no** (the committed Bowtie is a Linux ELF) | only with a manually provisioned index |

**Default: use the Docker image** (`disease-mutations-app:latest`). It contains everything, already verified by
`grna doctor`. Go native only for `pool`/`resolve`/`fold`, or when developing the CLI itself.

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
grna --help                                                   # top-level usage (exit 0). Subcommands have no --help.
grna doctor [--network]                                       # PASS/WARN/FAIL lines; exit 0 healthy, 3 otherwise
grna resolve rs334 [rs... ]                                   # stdout: "<rsID>\t<HGVS>" per notation
grna design --hgvs 'NC_000017.11:g.7674220C>T'                # CSV report to stdout (default --format csv)
grna design --hgvs '<HGVS>' --format json|tsv [--out file]    # other formats
grna design --hgvs '<HGVS>' --spacer 28 --seed 10-17 --complement   # parameters (these values are the defaults, minus --complement)
grna design --input variants.txt --out report.csv [--concurrency 2] # batch; RESUMABLE (re-run skips variants already in report.csv)
printf 'rs334\n' | grna design --input /dev/stdin --format json     # stdin input (Linux/container; docker needs -i, the wrapper adds it)
grna fold GAUUUAGACUACCCCAAAAACGAAGGGGACUAAAAC                 # 2 lines: sequence, then dot-bracket<TAB>energy
grna pool --count 100 --capacity 5 --plate 96 --model auto    # plate plan CSV to stdout, summary on stderr
grna pool --guides report.csv --out plan.csv                  # plan from a design report (one guide per variant)
```

Inputs to `design`: HGVS notations and/or rsIDs (`rs334` is expanded to every `NG_` notation dbSNP returns, each analysed
separately). `--input` takes a text file (one or more per line, `,`/`;` also separate, `#` lines ignored) or a web-app
session `.json`. Exact formats, output schemas, resume rules and pooling details:
[references/cli-reference.md](references/cli-reference.md).

## 6. Gotchas that will bite an agent

1. **Quote every HGVS.** `C>T` unquoted is a shell redirect: `grna design --hgvs NC_000017.11:g.7674220C>T` writes the
   report into a file named `T` and fails on the truncated `...7674220C` (exit 2). Use single quotes in bash and PowerShell.
2. **stdout is data, stderr is progress.** `design` prints `ok   <hgvs>` / `FAIL <hgvs>: <reason>` on stderr; `pool`
   prints its summary on stderr. Redirect/parse stdout only. Never allocate a TTY (`docker run -t`/`exec -t`), it merges them.
3. **Exit codes:** 0 ok, 1 failure (including *partial* batch failure: rows for the successes are still written),
   2 bad input/arguments, 3 missing native dependency (run `doctor`), 4 upstream failure (NCBI). 125 from the wrappers
   or `docker run` = Docker problem (e.g. image not built).
4. **A failed `design` still prints the CSV header** (and provenance line). Check the exit code, not just whether output exists.
5. **Unknown options are silently ignored** (`--capasity 2` runs with the default 5), and an unknown `--flag` swallows
   the next token as its value. Only `--complement`, `--network` and `--help` are value-less flags. Spell options exactly.
6. **Resume semantics.** With `--input` + `--out <file>.csv` + csv format, an existing report is appended to and variants
   already in it (keyed by RS ID + HGVS, *not* strand or parameters) are skipped. Use a new `--out` file when changing
   `--spacer`, `--seed` or `--complement`, or the run silently skips everything / mixes parameters.
7. **`--complement` analyses one strand only.** For both strands run twice with different `--out` files.
8. **Don't change the container working directory** without the two `GRNA_BOWTIE_*` overrides (section 4C).
9. **JSON escapes `>`** as `>` in `hgvs`. Use a real JSON parser (`jq`, `ConvertFrom-Json`), not grep.
10. **In PowerShell, prefer `--out` over `>`.** It writes UTF-8 without BOM exactly like the web export; PowerShell
    redirection re-encodes native output.
11. **Port 5000 is shared**: the compose container and `dotnet run --project DiseaseMutationsApp` both bind it. Stop one first.
12. **`design` needs network** (NCBI E-utilities for sequences, dbSNP for rsIDs). Set `GRNA_NCBI_API_KEY` and
    `GRNA_NCBI_CONTACT` for large batches (the wrappers forward them; `docker run` needs `-e`).

## 7. Typical end-to-end workflow

```bash
./start.sh                                                         # once (long the first time)
S=.claude/skills/diana/scripts/grna.sh
$S doctor || exit 1                                                # exit 3 => read the FAIL remedies
printf 'NC_000017.11:g.7674220C>T\nrs334\n' > variants.txt
$S design --input variants.txt --out report.csv; echo "design exit=$?"   # 1 => some variants failed; re-run resumes
$S pool --guides report.csv --capacity 5 --plate 96 --out plan.csv
```

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
- Repository docs: `README.md`, `docs/runtime-contract.md` (pins, env vars, concurrency), `docs/using-the-library.md`.
