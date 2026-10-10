# Troubleshooting

Always start with `grna doctor` (in the image: `docker run --rm --entrypoint grna disease-mutations-app:latest doctor`,
or `.claude/skills/diana/scripts/grna.sh doctor`). Each non-passing check prints `-> <remedy>`. Then match the symptom.

## Install / start

| Symptom | Cause | Fix |
|---|---|---|
| `[install] ERROR: Docker is not installed or not in PATH.` | no docker CLI | Install Docker Desktop / Engine with Compose v2. |
| `[install] ERROR: The Docker daemon is not running.` | daemon down | Start Docker Desktop (Windows/macOS) or `sudo systemctl start docker`; re-run. |
| `Docker Compose v2 is required` | only legacy `docker-compose` | Update Docker; the scripts call `docker compose`. |
| `Only N GB free where Docker stores images` | < 10 GB free | Free space (`docker system df`, prune unused images *after asking the user*), or move Docker's data root. |
| Installer silent for a long time on first run | downloading the ~3.7 GB index inside the base-image build | Wait; it is not hung. |
| Base build fails at `sha256sum -c` | corrupted/changed download | Re-run `./start.sh --rebuild-bowtie`. If the upstream object really changed, that is a pinning decision for the team (see `docs/runtime-contract.md`), not something to "fix" by editing the checksum. |
| `The app did not become healthy` | container crashed or a check failed | Read the printed logs; run `doctor` in the image; `docker compose logs app`. |
| `Bind for 127.0.0.1:5000 failed: port is already allocated` | `dotnet run` dev server or another app on 5000 | Stop it, or `docker compose down` first if the other one is what you want. |
| `.\start.ps1 cannot be loaded because running scripts is disabled` | execution policy | `powershell -NoProfile -ExecutionPolicy Bypass -File .\start.ps1` (what `start.bat` does). Don't change the machine policy. |
| Code changes not visible in the app/CLI | old app image | `./start.sh --rebuild`. |

## CLI

| Symptom | Cause | Fix |
|---|---|---|
| A file named `T`, `A`, `G` or `C` appeared; `FAIL ...C: Invalid HGVS format: Unknown mutation method` | unquoted HGVS: `>` redirected the output | Quote: `--hgvs 'NC_000017.11:g.7674220C>T'`. Delete the stray file. |
| `doctor`: `Bowtie binary: Not found at /work/bowtie/bowtie-align-s` (exit 3) | container workdir changed with `-w` | Keep workdir `/app`, or add `-e GRNA_BOWTIE_BINARY=/app/bowtie/bowtie-align-s -e GRNA_BOWTIE_INDEX=/app/bowtie/indexes/GRCh38_noalt_as` (the wrapper scripts do). |
| `doctor` natively on Windows: `exists but cannot be executed` | committed Bowtie is a Linux binary | Use the Docker image (or WSL) for `design`/`doctor`. `pool`, `resolve`, `fold` still work natively. |
| `Bowtie index directory '.../bowtie/indexes' not found` | native run without an index | Provide an index and `GRNA_BOWTIE_INDEX`, or use Docker. In the image: rebuild with `./start.sh --rebuild-bowtie`. |
| `import RNA failed` / `No module named 'RNA'` | Python lacks ViennaRNA, or `GRNA_PYTHON` points at the wrong interpreter | `pip install viennarna==2.7.2` into the interpreter `GRNA_PYTHON` names (Windows: the committed cp314 wheel). |
| `DLL load failed while importing _RNA: The filename or extension is too long` | Windows venv at a very long path | Recreate the venv at a short path (e.g. `C:\venvs\grna`). |
| `error: design needs exactly one of --hgvs or --input.` though you passed one | both given; a misspelled flag swallowed `--hgvs`; or `design --help` | Pass exactly one; check spelling of value-less flags (`--complement`). |
| An option seems to have no effect | misspelled option names are silently ignored | Compare against `grna --help` exactly; there is no `--opt=value` form. |
| `error: Seed must satisfy 0 <= start <= end < spacer.` | `--seed` outside the spacer | e.g. `--spacer 28 --seed 10-17`. |
| Batch re-run does nothing / finishes instantly | resume: every variant already present in `--out` | Intended when the report is complete. When changing `--spacer`/`--seed`/`--complement`, write to a new `--out`. |
| `design` exit 1 with some `FAIL` lines | partial failure; successful rows were written | Re-run the same command: resume retries only the failed variants. Read each `FAIL` reason first; input errors won't fix themselves. |
| exit 4 / `GrnaUpstreamException` / HTTP 429 | NCBI unreachable or rate limited | Check connectivity (`grna doctor --network`); set `GRNA_NCBI_API_KEY` + `GRNA_NCBI_CONTACT`; lower `--concurrency`; retry later. |
| `<rsID>: no NG_ notations found` | dbSNP has no `NG_` mapping for it (not an error) | Use an explicit HGVS (`NC_`/`NM_`) instead. |
| `FAIL <hgvs>: Mutation type Repeat not implemented` | `Repeat` HGVS notations parse but cannot be analysed | Known limitation (`docs/using-the-library.md`). |
| Output file empty except header, exit != 0 | every variant failed; the header is still written | Check exit code and stderr `FAIL` lines, not just whether the file exists. |
| Git Bash: `docker: invalid reference format` or paths like `C:/Program Files/Git/work` | MSYS path conversion of `/work` | `export MSYS_NO_PATHCONV=1` and use `$(pwd -W)` for the host side, or use `scripts/grna.sh`, which does both. |
| Files written through a bind mount are owned by uid 1654 (Linux) | container user `app` | Run with `--user "$(id -u):$(id -g)"` (the wrapper does on Linux), or write to stdout and redirect on the host. |
| JSON `hgvs` shows `\u003E` | standard JSON escaping of `>` | Parse with a JSON library; the value is correct. |
| PowerShell-written report looks garbled to other tools | `>` re-encodes native output | Use `--out <file>` instead of `>`. |
| `Unknown command '...'` (exit 2) | typo, or options before the command | Command first: `grna design --hgvs ...`. |

## Web app

| Symptom | Cause | Fix |
|---|---|---|
| `/healthz` HTTP 503 | a required check failed | Read its `checks[].remedy`, same as `doctor`. |
| "Connection lost" banner | Blazor circuit dropped | The run continues server-side ~10 min and the page rejoins. |
| Slow alignments | index served from a volume, or a very small Docker VM | Never mount the index; it must stay in the image layer. Memory beyond ~3 GB only keeps more of it cached. |
