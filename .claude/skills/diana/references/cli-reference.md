# `grna` CLI reference

Source of truth: `gRNA.Cli/Program.fs`. Everything here was checked against the built image
(`disease-mutations-app:latest`, CLI at `/usr/local/bin/grna` -> `dotnet /opt/grna-cli/gRNA.Cli.dll`).

## Contents

- [Global behaviour](#global-behaviour): argument parsing, streams, exit codes, environment, cancellation
- [`doctor`](#grna-doctor) · [`resolve`](#grna-resolve) · [`design`](#grna-design) · [`fold`](#grna-fold) · [`pool`](#grna-pool)
- [Output formats](#output-formats): design CSV/TSV/JSON, pool plan CSV
- [Scripting recipes](#scripting-recipes)

## Global behaviour

### Invocation

```
grna <command> [options] [positional...]
grna --help | -h | help        usage text, exit 0
grna                           usage text, exit 2
grna <unknown>                 "Unknown command", usage on stderr, exit 2
```

There is no per-command help: `grna design --help` is parsed as a flag that `design` ignores, so it fails with
`error: design needs exactly one of --hgvs or --input.` (exit 2).

### Argument parsing (important, it is minimal)

- `--name value` pairs, in any order, anywhere after the command.
- Value-less flags: **only** `--complement`, `--network`, `--help`. Every other `--x` consumes the next token as its value.
- **Unknown options are accepted and ignored.** A typo (`--capasity 2`, `--spacr 20`) is silent and the default is used.
- A misspelled flag (`--complment`) swallows the next token: `design --complment --hgvs X` sets `complment=--hgvs`, leaves
  `X` positional and fails with "needs exactly one of --hgvs or --input".
- An option at the end with no value: `error: Option --out needs a value.` (exit 2).
- No `--opt=value` syntax: `--spacer=28` becomes an unknown option named `spacer=28` and needs a value after it.

### Streams

- **stdout**: only the result (report, plan, fold result, resolve lines, doctor lines, usage).
- **stderr**: progress (`ok   <hgvs>`, `FAIL <hgvs>: <message>`), `pool` summary, `<rsID>: no NG_ notations found`,
  `error: ...` messages, `cancelled.`.
- `--out <file>` writes UTF-8 **without BOM** and overwrites (except the resumable batch case, below).

### Exit codes

| Code | Meaning | Typical cause |
|---|---|---|
| 0 | ok | |
| 1 | failure | partial batch failure; unexpected exception (e.g. some malformed HGVS such as `garbage` crash the parser instead of raising an input error); Ctrl-C (`cancelled.`) |
| 2 | bad input | argument errors, invalid HGVS that the parser rejects, `--seed` out of range, unreadable input file |
| 3 | missing native dependency | Bowtie binary/index or Python/ViennaRNA unusable; `doctor` with any FAIL |
| 4 | upstream failure | NCBI / dbSNP HTTP failure, child process returned unparseable output |
| 125 | (wrapper scripts / `docker run`) | Docker failed: image missing, daemon down |

For `design`: if **every** job failed, the code comes from the last error's type (2/3/4, else 1); if **some** failed,
it is 1 and the successful rows are still emitted.

### Environment variables (read by the library)

| Variable | Default | Effect |
|---|---|---|
| `GRNA_BOWTIE_BINARY` | `bowtie/bowtie-align-s` | Bowtie 1 binary. Relative paths resolve against the CLI's base dir, then the CWD. In the image: `/app/bowtie/bowtie-align-s` (found via CWD `/app`). |
| `GRNA_BOWTIE_INDEX` | (unset: first index found under `bowtie/indexes`) | Index base path without suffix. In the image: `/app/bowtie/indexes/GRCh38_noalt_as`. |
| `GRNA_PYTHON` | `python3` | Python with `import RNA` (ViennaRNA 2.7.2). On Windows usually `python` or a venv's `python.exe`. |
| `GRNA_NCBI_API_KEY` | unset | NCBI key: 10 req/s instead of 3. |
| `GRNA_NCBI_CONTACT` | unset | Contact e-mail NCBI asks tools to send. |

### Cancellation

Ctrl-C (SIGINT) is wired to a cancellation token (`Console.CancelKeyPress` in `Program.fs`): child Bowtie/Python
process trees are killed, `cancelled.` goes to stderr, exit 1. To abort a containerised run started with `docker run -d`
or from a script, `docker stop`/`docker kill` the container (the wrappers use `--rm`, so it is removed afterwards).

## `grna doctor`

```
grna doctor [--network]
```

Prints one line per check: `[PASS|WARN|FAIL] <name>: <detail>`, followed by `       -> <remedy>` for non-passing checks.
Checks: Working directory, Bowtie binary (exists, runs `--version`), Bowtie index (resolved base and format),
ViennaRNA (`python -c "import RNA"` and version), NCBI reachability (only with `--network`; WARN, never FAIL), Memory
(always PASS; notes when < 4 GB). Exit 0 when nothing FAILs, else 3.

Healthy image output (verified):

```
[PASS] Working directory: cwd=/app base=/opt/grna-cli/
[PASS] Bowtie binary: /app/bowtie/bowtie-align-s (/app/bowtie/bowtie-align-s version 1.3.1)
[PASS] Bowtie index: /app/bowtie/indexes/GRCh38_noalt_as [Bowtie 2 (.bt2)]
[PASS] ViennaRNA: RNA 2.7.2 via python3
[PASS] Memory: 2.9 GB available to the process (...)
```

`[Bowtie 2 (.bt2)]` is expected: the binary is Bowtie **1**.3.1 and it reads this `.bt2` index (see
`docs/runtime-contract.md`, V1). The same checks back `GET /healthz` (JSON, HTTP 503 when unhealthy) and the web
`/diagnostics` page.

## `grna resolve`

```
grna resolve <rsID> [<rsID> ...]
```

- Accepts `rs334` or `334` (the `rs` prefix is optional, case-insensitive).
- stdout: one `<rsID-as-typed>\t<HGVS>` line per notation (e.g. `rs334\tNG_000007.3:g.70614A>T`; rs334 yields 12).
- Only `NG_` notations are returned. None found: `<rsID>: no NG_ notations found` on stderr, still exit 0.
- HTTP failure: exit 4. Needs internet only (no Bowtie/Python), so it works natively on any OS.

## `grna design`

```
grna design --hgvs <HGVS>           [--spacer 28] [--seed 10-17] [--complement] [--format csv|json|tsv] [--out file] [--concurrency 2]
grna design --input <file|session.json> [--spacer 28] [--seed 10-17] [--complement] [--format csv|json|tsv] [--out file] [--concurrency 2]
```

| Option | Default | Notes |
|---|---|---|
| `--hgvs` | | exactly one of `--hgvs` / `--input`. An rsID here is also accepted and expanded. |
| `--input` | | text list or web-app session `.json` (detected by `.json` extension) |
| `--spacer` | 28 (or the session's) | spacer length, > 0 |
| `--seed` | `10-17` (or the session's) | `start-end`, inclusive, 0-based in the spacer; must satisfy `0 <= start <= end < spacer` else exit 2 |
| `--complement` | off | analyse the complement strand instead of the normal one (one strand per run) |
| `--format` | `csv` | `csv`, `json` or `tsv` (case-insensitive); anything else exit 2 |
| `--out` | stdout | output file |
| `--concurrency` | 2 | variants in flight, clamped to 1-4. Bowtie is serialised per process anyway, so >2 rarely helps. |

Explicit `--spacer`/`--seed` override the values stored in a session file.

### Inputs

- **HGVS**: e.g. `NC_000017.11:g.7674220C>T`, `NM_000546.6:c.215_217del`, `...delinsAG`, `...dup`, `...insA`, `...inv`
  (see README "HGVS examples"). `Repeat` notations parse but cannot be analysed. Always quote them in the shell.
- **rsIDs**: an item starting with `rs` + digit is resolved via dbSNP first; each `NG_` notation becomes its own job.
  Bare digits are **not** treated as rsIDs.
- **Text file** (`--input variants.txt`): split on newlines, `,` and `;`; trimmed; empty and `#`-prefixed items
  dropped; duplicates removed (first occurrence order kept).
- **Session JSON** (saved by the web app's *Save session*):
  `{"version":1,"input":"<comma/newline separated variants>","spacer":28,"seed":"10-17","shortlist":[]}`.
  Only `input`, `spacer` and `seed` are read.
- **stdin**: `--input /dev/stdin` (Linux/container; with Docker add `-i`). Not resumable, output to stdout.

### Execution

1. rsIDs are resolved sequentially (dbSNP). 2. Jobs run with bounded concurrency; each fetches the sequence from NCBI,
ranks spacers on the mutated and original sequence (Bowtie off-target counts + ViennaRNA folding). Per job, stderr gets
`ok   <hgvs>` or `FAIL <hgvs>: <message>`. 3. Rows are emitted in input order regardless of completion order.

Observed timing on a warm container: one HGVS took about 14 s (network fetch + alignment + folding). Batches scale
roughly linearly divided by concurrency; don't promise timings, measure.

### Resumable batches

Active only when **all** hold: `--input` given, format `csv`, `--out` given, and the `--out` file already exists with rows.
Then:

- Existing rows are keyed by `RS ID|HGVS`; jobs with a matching key are skipped; new rows are **appended** (no new header).
- Variants that failed last time are absent from the file, so a plain re-run retries exactly those.
- The key ignores strand, spacer and seed, and the original provenance line is kept. **Changing `--complement`,
  `--spacer` or `--seed` against an existing report silently skips everything already present.** Use a new `--out`.
- `--hgvs` runs, json/tsv formats and stdout output always overwrite / never resume.

## `grna fold`

```
grna fold <sequence>
```

- One positional sequence of A/C/G/U (or T, converted to U), case-insensitive. Anything else exit 2; zero or several
  positionals exit 2.
- stdout (2 lines): the normalised RNA, then `<dot-bracket>\t<MFE kcal/mol, 2 decimals>`:
  ```
  GAUUUAGACUACCCCAAAAACGAAGGGGACUAAAAC
  ..(((((....((((.........)))).)))))..	-6.70
  ```
- Needs Python + ViennaRNA only (no Bowtie, no network).

## `grna pool`

```
grna pool --guides <file> [--capacity 5] [--plate 96|384] [--model auto|2df|2dm|3d] [--out plan.csv]
grna pool --count <V>     [--capacity 5] [--plate 96|384] [--model auto|2df|2dm|3d] [--out plan.csv]
```

| Option | Default | Notes |
|---|---|---|
| `--guides` | | exactly one of `--guides` / `--count` |
| `--count` | | positive integer; guides are labelled `Guide 1..V` |
| `--capacity` | 5 | max guides per tube/well (K), 1-10000 |
| `--plate` | 96 | `96` or `384` |
| `--model` | `auto` | `2df` 2D fragmented, `2dm` 2D matrix, `3d`; `auto` = fewest wells, ties prefer 2df, then 2dm, then 3d |

- `--guides` with a **design report** (CSV or TSV, header `RS ID,HGVS,Sequence Type,...`; `#` lines skipped): one guide
  per HGVS, namely its best-ranked `Mutated` row on the first strand seen; guides are **labelled by HGVS**.
- `--guides` with any other file: every comma-separated item on every line is a guide label (deduplicated).
- Guide count 1-100000. Invalid combinations exit 2. Pure computation: no network, no native tools, works on any OS.
- stderr summary, e.g. `2D Fragmented: V=100 K=5 -> 40 wells (40 to prepare), each guide in 2`.

## Output formats

### Design CSV (default; same schema as the web export and the Pooling page import)

```
# Diana report; spacer=28; seed=10-17; generated=2026-10-06T17:41:28Z
RS ID,HGVS,Sequence Type,Rank,Sequence,Score,GC Content,Alignments,Seed Region,Homopolymers,Fold Energy,Strand
,NC_000017.11:g.7674220C>T,Mutated,1,AGUUCCUGCAUGGGCGGCAUGAACAAGA,1,53.57,1,UGGGCGGC,0,-13.100000381469727,Normal
,NC_000017.11:g.7674220C>T,Original,1,AGUUCCUGCAUGGGCGGCAUGAACAGGA,1,57.14,1,UGGGCGGC,0,-13.300000190734863,Normal
```

| Column | Content |
|---|---|
| RS ID | rsID **digits without `rs`** (e.g. `334`); empty for a direct HGVS input |
| HGVS | the analysed notation |
| Sequence Type | `Mutated` or `Original` (wild-type) |
| Rank | 1 = best; ties share a rank |
| Sequence | spacer, RNA (`U`) |
| Score | 0-1 |
| GC Content | percent |
| Alignments | Bowtie hits, 0-6; **6 means "6 or more"** (saturated) |
| Seed Region | the seed slice of the spacer |
| Homopolymers | runs of >= 4 identical bases |
| Fold Energy | MFE in kcal/mol, full float precision |
| Strand | `Normal` or `Complement` |

Line 1 is a provenance comment: skip lines starting with `#` when parsing. Numbers use the invariant culture (`.`).
Fields containing `,` or `"` are quoted per RFC 4180. All rows for a variant: every `Mutated` row, then every `Original` row.

### Design TSV

Same columns, tab-separated, header first, **no provenance line**.

### Design JSON

```json
[
  {
    "hgvs": "NC_000017.11:g.7674220C>T",
    "rsId": null,
    "strand": "Normal",
    "mutated":  [ { "rank": 1, "sequence": "AGUU...", "score": 1, "gcContent": 53.57, "alignments": 1,
                    "seedRegion": "UGGGCGGC", "homopolymers": 0, "energy": -13.100000381469727,
                    "structure": "(.(((((....((((...." } ],
    "original": [ ... ]
  }
]
```

`>` is escaped as `>` (valid JSON). `structure` (dot-bracket of the complete gRNA) exists only in JSON.
`rsId` holds the digits as a string (e.g. `"334"`), or `null`. Keys are emitted alphabetically (`hgvs, mutated,
original, rsId, strand`); don't rely on key order.

### Pool plan CSV

```
Tube ID,Mixture Name,Guides in Tandem,Well
1,Block 1 - Row 1,Guide 1; Guide 2; Guide 3; Guide 4; Guide 5,Plate 1 - A1
```

Guides inside a tube are separated by `; `. A tube with nothing to prepare reads `Empty - do not prepare`. Wells are
laid out row-major, continuing on `Plate 2 - ...` when a plate is full.

## Scripting recipes

```bash
S=.claude/skills/diana/scripts/grna.sh

# Fail fast on a broken install
$S doctor >/dev/null || { echo "install broken"; exit 1; }

# Best mutated spacer per variant from a CSV report (skip the # line and the header)
grep -v '^#' report.csv | awk -F, 'NR>1 && $3=="Mutated" && $4==1 {print $2"\t"$5}'

# JSON with jq: top candidate per variant
$S design --hgvs 'NC_000017.11:g.7674220C>T' --format json | jq -r '.[] | [.hgvs, .mutated[0].sequence, .mutated[0].alignments] | @tsv'

# Retry a batch until no variant fails (resume makes each pass cheap); stop on non-retryable codes
for i in 1 2 3; do $S design --input variants.txt --out report.csv; rc=$?; [ $rc -ne 1 ] && break; done; echo "rc=$rc"

# Both strands
$S design --input variants.txt --out normal.csv
$S design --input variants.txt --out complement.csv --complement
```

```powershell
$S = '.\.claude\skills\diana\scripts\grna.ps1'
& $S design --hgvs 'NC_000017.11:g.7674220C>T' --format json | ConvertFrom-Json | ForEach-Object { $_.mutated[0].sequence }
& $S design --input variants.txt --out report.csv; if ($LASTEXITCODE -ne 0) { "design exit $LASTEXITCODE" }
# The first line is a '#' comment, so Import-Csv would treat it as the header. Strip it first:
Get-Content report.csv | Where-Object { $_ -notmatch '^#' } | ConvertFrom-Csv | Where-Object { $_.'Sequence Type' -eq 'Mutated' -and $_.Rank -eq '1' }
```
