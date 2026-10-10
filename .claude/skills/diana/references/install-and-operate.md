# Installing and operating Diana

All commands run from the repository root unless stated otherwise.

## Contents

- [What gets built](#what-gets-built)
- [Route 1: Docker via the installer (default)](#route-1-docker-via-the-installer-default)
- [Route 2: Docker by hand](#route-2-docker-by-hand)
- [Lifecycle: status, stop, logs, update, remove](#lifecycle)
- [Configuration knobs](#configuration-knobs)
- [Route 3: native .NET (no Docker)](#route-3-native-net-no-docker)
- [Packaging `grna` as a dotnet tool](#packaging-grna-as-a-dotnet-tool)
- [Tests, pin gate and CI](#tests-pin-gate-and-ci)

## What gets built

| Image | Tag (default) | Built from | Contents |
|---|---|---|---|
| Index base | `disease-mutations-bowtie:grch38-noalt-20260526` | `Dockerfile.bowtie-base` | ASP.NET runtime + the GRCh38 Bowtie index at `/app/bowtie/indexes/GRCh38_noalt_as.*` (downloaded from the public `s3://genome-idx` bucket, SHA-256 verified). ~4.4 GB. Changes approximately never. |
| App | `disease-mutations-app:latest` | `Dockerfile` (FROM the base) | Python 3 + `viennarna==2.7.2`, the published web app in `/app`, the CLI in `/opt/grna-cli` with a `/usr/local/bin/grna` launcher, the checksum-verified Bowtie 1.3.1 binary at `/app/bowtie/bowtie-align-s`. Runs as user `app`, workdir `/app`, entrypoint = web app, `HEALTHCHECK` on `/healthz`. |

The compose service is named `app`, its container `disease-mutations-app`, published on `127.0.0.1:5000`.

**Never put the index in a volume or bind mount**: Bowtie memory-maps it (`--mm`), and that is 10x+ slower from a
volume. It must stay in the image layer (`docs/runtime-contract.md`).

## Route 1: Docker via the installer (default)

Prerequisites: Docker with Compose v2 (`docker compose version`), daemon running, ~10 GB free in Docker's storage for
the first build, outbound HTTPS (S3 for the index at build time, NCBI at run time).

```bash
./start.sh [--build-local] [--rebuild] [--rebuild-bowtie] [--help]
```

```powershell
.\start.ps1 [-BuildLocal] [-Rebuild] [-RebuildBowtie]
start.bat [-BuildLocal] ...     # wraps start.ps1 with ExecutionPolicy Bypass, then `pause`s: interactive use only
```

Steps (both scripts): `cd` to the repo root -> check docker, daemon, compose v2 (bash also checks buildx and free disk)
-> base image: reuse if present, else pull from `$GRNA_REGISTRY` (bash only) or build it with
`docker compose --profile base build bowtie-base` -> app image: reuse if present (unless `--rebuild`), else pull/build
`docker compose build app` -> `docker compose up -d` -> poll `/healthz` 60 times, 2 s apart.

| Flag | Effect |
|---|---|
| (none) | Reuse existing images; build only what is missing. Re-running is cheap and idempotent. |
| `--rebuild` / `-Rebuild` | Rebuild the app image. **Use after changing any C#/F# code** so the container and CLI pick it up. |
| `--rebuild-bowtie` / `-RebuildBowtie` | Re-download and rebuild the index base image too (implies rebuild). Long. |
| `--build-local` / `-BuildLocal` | Never pull from a registry. |

Output to watch: lines prefixed `[install]`. Success ends with `[install] Ready: http://localhost:5000` (exit 0). Failure:
`[install] ERROR: ...` and a non-zero exit; a health timeout also dumps `docker compose logs --tail 40 app`.

Agent tips:
- The first base-image build downloads ~3.7 GB: run the installer in the background or with the longest timeout
  available, and poll its output. Don't kill it because it is quiet during the download.
- On Windows from Git Bash, `./start.sh` works; from PowerShell use `.\start.ps1` (not `start.bat`, which waits for a keypress).
- Verify afterwards with `docker run --rm --entrypoint grna disease-mutations-app:latest doctor` (exit 0).

## Route 2: Docker by hand

```bash
docker compose --profile base build bowtie-base     # once
docker compose build app                            # after code changes
docker compose up -d                                # start (detached)
curl -fsS http://localhost:5000/healthz             # wait until this returns 200
```

The CLI needs only the app image, not a running container: `docker run --rm --entrypoint grna disease-mutations-app:latest <cmd>`.

## Lifecycle

| Task | Command |
|---|---|
| Is it running / healthy? | `docker ps --filter name=disease-mutations-app --format '{{.Status}}'` (shows `(healthy)`), or `curl -s http://localhost:5000/healthz` |
| Follow logs | `docker compose logs -f app` |
| Stop (keeps images) | `docker compose down` |
| Start again | `docker compose up -d` (or `./start.sh`) |
| Apply code changes | `./start.sh --rebuild` (rebuilds the app image and recreates the container) |
| Validate install | `docker run --rm --entrypoint grna disease-mutations-app:latest doctor` |
| Shell in the container | `docker exec -it disease-mutations-app sh` (interactive humans only) |
| Remove images | `docker compose down && docker image rm disease-mutations-app:latest` (keep the base image unless you really want to re-download the index) |

The web UI: `http://localhost:5000/` (guide design), `/pooling`, `/diagnostics`. `GET /healthz` returns
`{"healthy":true,"checkedAt":...,"checks":[{"name","status":"pass|warn|fail","detail","remedy"}]}` with HTTP 200, or 503.

## Configuration knobs

Compose / installer (set in the environment or a `.env` file next to `docker-compose.yml`; `.env` is git-ignored):

| Variable | Default | Effect |
|---|---|---|
| `GRNA_BIND` | `127.0.0.1` | Host interface for port 5000. `0.0.0.0` exposes it to the network: **there is no authentication**, don't do this unprompted. |
| `GRNA_MEMORY_LIMIT` | `4G` | Container memory cap (upper bound only; ~3 GB VMs work). |
| `GRNA_MAX_CONCURRENT_VARIANTS` | 2 | Web app: variants analysed at once (1-4). |
| `GRNA_NCBI_API_KEY`, `GRNA_NCBI_CONTACT` | empty | Passed to the web app container. Never commit a real key. |
| `GRNA_APP_IMAGE` | `disease-mutations-app:latest` | App image name (also honoured by the wrapper scripts). |
| `GRNA_BOWTIE_BASE` | `disease-mutations-bowtie:grch38-noalt-20260526` | Base image name. |
| `GRNA_REGISTRY` | unset | `start.sh` pulls `<registry>/bowtie-base:<tag>` and `<registry>/app:latest` first. |

Web app only (ASP.NET configuration): `Analysis__MaxConcurrentVariants`, `Analysis__VariantTimeoutSeconds` (default 600).
Library/CLI variables (`GRNA_BOWTIE_BINARY`, `GRNA_BOWTIE_INDEX`, `GRNA_PYTHON`, NCBI): see `cli-reference.md`.

## Route 3: native .NET (no Docker)

Needs the .NET SDK pinned in `global.json` (9.0.306, `rollForward: latestPatch`); check with `dotnet --version`.

```bash
dotnet restore --locked-mode          # fails on any lock-file drift: that is intended
dotnet build -c Release
dotnet run --project gRNA.Cli -c Release -- pool --count 100 --capacity 5
dotnet run --project gRNA.Cli -c Release --no-build -- doctor   # --no-build after a build: faster, no MSBuild noise
```

Everything after `--` goes to `grna`. What works natively:

- **`pool`**: always (pure computation).
- **`resolve`**: with internet.
- **`fold`**: needs Python with ViennaRNA 2.7.2.
  - Linux/macOS/WSL: `python3 -m pip install viennarna==2.7.2` (in a venv), point `GRNA_PYTHON` at it if not `python3`.
  - Windows: a wheel for **CPython 3.14 x64** is committed: `DiseaseMutationsApp/viennarna-2.7.2-cp314-cp314-win_amd64.whl`.
    ```powershell
    py -3.14 -m venv C:\venvs\grna        # keep the venv path SHORT
    C:\venvs\grna\Scripts\python -m pip install DiseaseMutationsApp\viennarna-2.7.2-cp314-cp314-win_amd64.whl
    $env:GRNA_PYTHON = 'C:\venvs\grna\Scripts\python.exe'
    ```
    A venv under a very long path fails with `ImportError: DLL load failed while importing _RNA: The filename or extension
    is too long` (Windows MAX_PATH). Verified: the same wheel works from a short path.
    On Windows `python3` is often the Microsoft Store alias; set `GRNA_PYTHON` explicitly.
- **`design`** / a healthy **`doctor`**: needs a Bowtie 1 binary for your OS and the GRCh38 index.
  - The committed `bowtie/bowtie-align-s` is a **Linux x86-64** binary: on Windows `doctor` reports
    `exists but cannot be executed`. Use Docker (or WSL) for `design` on Windows.
  - On Linux/WSL: `chmod +x bowtie/bowtie-align-s`, obtain the index the way `Dockerfile.bowtie-base` does (~3.7 GB zip,
    verify its SHA-256) and unzip it into `bowtie/indexes/` (git-ignored), or set `GRNA_BOWTIE_INDEX` to its base path.
    Index files are found recursively under `bowtie/indexes`; the first base in sorted order wins.
  - Relative `GRNA_*` paths resolve against the CLI's output directory first, then the current directory. Run from the
    repo root, or use absolute paths.

Web app without Docker (development): set the same `GRNA_*` variables, then
`dotnet run --project DiseaseMutationsApp --launch-profile http` (http://localhost:5000, `Development` environment).
Stop the compose container first: both bind port 5000. The repo's `.claude/launch.json` defines this as the `app`
preview configuration.

## Packaging `grna` as a dotnet tool

`gRNA.Cli.fsproj` has `PackAsTool=true`, command name `grna`, package `gRNA.Cli` 1.0.0.

```bash
dotnet pack gRNA.Cli -c Release -o ./nupkg                                # ./nupkg/gRNA.Cli.1.0.0.nupkg
dotnet tool install gRNA.Cli --tool-path ./.tools --add-source ./nupkg     # local install (verified)
./.tools/grna --help
# or globally: dotnet tool install -g gRNA.Cli --add-source ./nupkg   (then `grna` is on PATH via ~/.dotnet/tools)
# upgrade after changes: bump <Version> or `dotnet tool uninstall` first, then install again
```

The package contains only the CLI and library: Bowtie, the index and ViennaRNA are **not** included, so the same
native-route limits apply. Keep `nupkg/` and tool folders out of commits.

The library itself can also be packed (`dotnet pack gRNA -c Release`) or used from F# Interactive:
`dotnet build gRNA -c Release && dotnet fsi examples/standalone.fsx` (see `docs/using-the-library.md`).

## Tests, pin gate and CI

```bash
dotnet restore --locked-mode
dotnet test                 # no container, no network needed; RnaFoldIntegrationTests skip without python3 + RNA
bash scripts/check-pins.sh  # "pin check passed" or PIN VIOLATION lines; run before pushing dependency changes
```

`.gitlab-ci.yml` runs exactly these two (stages `lint`: `pins`, `test`: `test`) and builds no images. To bump a
dependency, change it in its single pinned place (`Directory.Packages.props`, `global.json`, Dockerfile digests/ARGs),
run `dotnet restore --force-evaluate` to refresh lock files, and update `docs/runtime-contract.md`.
