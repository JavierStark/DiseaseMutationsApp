# `grna` CLI reference

Source of truth: `gRNA.Cli/Program.fs`. Everything here was checked against the built image
(`disease-mutations-app:latest`, CLI at `/usr/local/bin/grna` -> `dotnet /opt/grna-cli/gRNA.Cli.dll`).

## Contents

- [Global behaviour](#global-behaviour): argument parsing, streams, exit codes, environment, cancellation
- [`doctor`](#grna-doctor) · [`resolve`](#grna-resolve) · [`disease`](#grna-disease) · [`design`](#grna-design) · [`fold`](#grna-fold) · [`pool`](#grna-pool)
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

`grna <command> --help` (or `-h`) prints the same usage text and exits 0.

### Argument parsing (important, it is minimal)

- `--name value` pairs, in any order, anywhere after the command.
- Value-less flags: **only** `--complement`, `--network`, `--help`. Every other `--x` consumes the next token as its value.
- **Unknown options are rejected.** A typo (`--capasity 2`, `--spacr 20`, `--complment`) exits 2 with
  `error: Unknown option --capasity for 'pool'.`, and a flag that does not apply to the command (`pool --count 5 --complement`)
  exits 2 as well. Nothing falls back silently to a default.
- An option at the end with no value: `error: Option --out needs a value.` (exit 2).
- No `--opt=value` syntax: `--spacer=28` becomes an unknown option named `spacer=28` and needs a value after it.

### Streams

- **stdout**: only the result (report, plan, fold result, resolve lines, doctor lines, usage).
- **stderr**: progress (`ok   <hgvs>`, `FAIL <hgvs>: <message>`, and `note <hgvs>: <warning>` when a result carries a warning, for example that the substitution rule could not be applied near the end of the sequence), `pool` summary, `<rsID>: no NG_ notations found`,
  `error: ...` messages, `cancelled.`.
- `--out <file>` writes UTF-8 **without BOM** and overwrites (except the resumable batch case, below).

### Exit codes

| Code | Meaning | Typical cause |
|---|---|---|
| 0 | ok | |
| 1 | failure | partial batch failure; unexpected exception; Ctrl-C (`cancelled.`) |
| 2 | bad input | argument errors and unknown options; invalid HGVS (no colon, no change, reversed range, lower-case or IUPAC alleles, a stated reference base that differs from the real sequence, a `c.` position on a genomic record or with an intronic offset, repeat notation); `--spacer` outside 18-60; `--seed` out of range; unreadable or empty input file; `pool --capacity 0`; a resumed report made with another spacer/seed |
| 3 | missing native dependency | Bowtie binary/index or Python/ViennaRNA unusable; `doctor` with any FAIL |
| 4 | upstream failure | NCBI / dbSNP HTTP failure, child process returned unparseable output |
| 125 | (wrapper scripts / `docker run`) | Docker failed: image missing, daemon down |

For `design`: if **every** job failed, the code comes from the type of the **first failed variant in input order** (2/3/4, else 1);
if **some** failed, it is 1 and the successful rows are still emitted.

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
`/app-status` page.

## `grna resolve`

```
grna resolve <rsID> [<rsID> ...] [--all-alleles]
```

- Accepts `rs334` or `334` (the `rs` prefix is optional, case-insensitive).
- stdout: one `<rsID-as-typed>\t<HGVS>` line per notation (e.g. `rs334\tNG_000007.3:g.70614A>T`). By default only the disease-relevant allele(s) on the main RefSeqGene record are listed (rs334 yields 1); `--all-alleles` lists every allele on every RefSeqGene record (rs334 yields 12).
- Only `NG_` notations are returned. None found: `<rsID>: no NG_ notations found` on stderr, still exit 0.
- HTTP failure: exit 4. Needs internet only (no Bowtie/Python), so it works natively on any OS.

## `grna disease`

```
grna disease <name | OMIM number | MONDO id> [--list] [--pick N] [--subtypes]
             [--min-significance pathogenic-likely|pathogenic|any] [--format rs|csv|tsv|json] [--out file]
```

Finds the ClinVar variants of a disease and prints their rsIDs. The disease name is looked up in the Monarch Initiative API
(no key, no CAPTCHA); the variants come from NCBI ClinVar (`elink` OMIM to ClinVar, a server-side classification filter, batched
`esummary`). It replaces the removed OMIM scraper.

- The query is a free-text name (`"sickle cell disease"`), an OMIM number (`603903`, `OMIM:603903`) or a MONDO id (`MONDO:0011382`).
  An OMIM number needs no Monarch call. A malformed number or id exits 2 before any request.
- Several matches for a name: the first is used and a `note:` on stderr says so. `--list` prints the matches
  (`N<TAB>MONDO id<TAB>name<TAB>OMIM numbers`) and exits 0; `--pick N` chooses one.
- A parent disease that only has an OMIM *phenotypic series* (Fanconi anemia) is expanded through its direct subtypes
  automatically (up to 40); `--subtypes` forces that expansion for a disease that has its own OMIM number too.
- `--min-significance`: `pathogenic-likely` (default: Pathogenic, Likely pathogenic, Pathogenic/Likely pathogenic),
  `pathogenic`, or `any` (every ClinVar record linked to the disease, including benign and uncertain).
- `--format rs` (default): one distinct `rsNNN` per line, ready for `grna design --input`. Records without an rsID are left out
  (stderr says how many); `--format csv|tsv|json` keep them, with a genomic HGVS (GRCh38) derived from ClinVar's canonical SPDI when
  it is a simple variant. CSV columns: `rsID,HGVS,Gene,Classification,Review status,Stars,ClinVar ID,Variant,Conditions`.
- At most 3,000 ClinVar records are read per search; when more match, stderr carries a `warning:` and the list is incomplete.
- stderr also carries progress and the counts (`923 ClinVar records matched; 923 listed, 833 with an rsID, 90 without.`).
- Exit codes: 0 ok (also when ClinVar has no record: empty output), 2 bad input (no such disease, no numeric OMIM number, bad option),
  4 Monarch or NCBI failure. Needs internet only, so it works natively on any OS. Counts drift as ClinVar is updated.
- The list is a set of candidates from ClinVar's classifications, not a clinical statement; check each variant before using it.

```bash
grna disease 603903 --out sickle.txt                 # contains rs334
grna design --input sickle.txt --out report.csv      # then design guides for them (Docker image)
```

## `grna design`

```
grna design --hgvs <HGVS>           [--spacer 28] [--seed 10-17] [--complement] [--format csv|json|tsv] [--out file] [--concurrency 2] [--snp-pos 3] [--mismatch-pos 4]
grna design --input <file|session.json> [--spacer 28] [--seed 10-17] [--complement] [--format csv|json|tsv] [--out file] [--concurrency 2] [--snp-pos 3] [--mismatch-pos 4]
```

| Option | Default | Notes |
|---|---|---|
| `--hgvs` | | exactly one of `--hgvs` / `--input`. An rsID here is also accepted and expanded. |
| `--input` | | text list or web-app session `.json` (detected by `.json` extension) |
| `--spacer` | 28 (or the session's) | spacer length, 18 to 60, else exit 2 |
| `--seed` | `10-17` (or the session's) | `start-end`, inclusive, 0-based in the spacer; must satisfy `0 <= start <= end < spacer` else exit 2 |
| `--complement` | off | design against the opposite strand (the reverse complement, read 5'->3') instead of the reference strand; one strand per run |
| `--format` | `csv` | `csv`, `json` or `tsv` (case-insensitive); anything else exit 2 |
| `--out` | stdout | output file |
| `--concurrency` | 2 | variants in flight, clamped to 1-4. Bowtie is serialised per process anyway, so >2 rarely helps. |
| `--snp-pos` | 3 | substitution rule: where the variant sits in the engineered spacer, counted from the spacer's 3' end (1 = last letter). Must be 1..spacer size and differ from `--mismatch-pos`; otherwise exit 2. |
| `--mismatch-pos` | 4 | substitution rule: which letter is deliberately changed to A (or U if it is A), counted from the 3' end. A resumed report made with other positions is refused (exit 2); the positions are in the report's `# Diana report` line. |

Explicit `--spacer`/`--seed` override the values stored in a session file.

### Inputs

- **HGVS**: e.g. `NC_000017.11:g.7674220C>T`, `NC_000017.11:g.7674220_7674222del`, `...delinsAG`, `...dup`, `...insA`, `...inv`
  (see README "HGVS examples"). `g.`, `m.` and `n.` positions are designed. **`c.` positions are converted on a transcript record** (`NM_000518.5:c.20A>T` becomes `n.70A>T`
  using the CDS start from the GenBank record; this needs internet) and refused on a genomic record (`NG_`, `NC_`) or with an intronic offset
  (`c.92+2`); repeat, conversion and translocation notations are refused too. The stated
  reference base (or `del`/`dup`/`inv` sequence) must equal the real one, alleles must be upper-case A/C/G/T, and a deletion
  longer than the spacer leaves no window and is refused. Always quote HGVS in the shell.
- **rsIDs**: an item that is exactly `rs` + digits is resolved via dbSNP first; each selected `NG_` notation becomes its own job (the disease-relevant allele on the main record, or every one with `--all-alleles`).
  Bare digits are **not** treated as rsIDs, and `rs` or `rs12x` is a malformed variant (exit 2).
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

- Existing rows are keyed by `RS ID|HGVS|Strand`; jobs with a matching key are skipped; new rows are **appended** (no new header).
  An existing but empty file simply starts a fresh report.
- Variants that failed last time are absent from the file, so a plain re-run retries exactly those.
- The report's provenance line (`# Diana report; spacer=..; seed=..-..`) is compared with the current `--spacer`/`--seed`:
  **a different spacer or seed is refused (exit 2)**, so rows made with different parameters are never mixed. Use a new `--out`.
  Running the other strand (`--complement`) into the same file is fine: its variants have a different key and are appended.
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
RS ID,HGVS,Sequence Type,Rank,Sequence,Score,GC Content,Offtargets,Seed Region,Homopolymers,Fold Energy,Strand,WT Mismatches,Engineered,Scaffold Intact,Spacer Unpaired
,NC_000017.11:g.7674220C>T,Mutated,1,AGUUCCUGCAUGGGCGGCAUGAACAAGA,1,53.57,1,UGGGCGGC,0,-13.100000381469727,Normal,1,Yes,Yes,0.57
,NC_000017.11:g.7674220C>T,Original,1,AGUUCCUGCAUGGGCGGCAUGAACAGGA,1,57.14,1,UGGGCGGC,0,-13.300000190734863,Normal,1,No,Yes,0.61
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
| Offtargets | Bowtie hits, 0-6; **6 means "6 or more"** (saturated) |
| Seed Region | the seed slice of the spacer |
| Homopolymers | runs of >= 4 identical bases |
| Fold Energy | MFE in kcal/mol, full float precision |
| Strand | `Normal` or `Complement` |
| WT Mismatches | fewest letters in which the spacer's DNA target differs from the closest window of the other allele (a mutated row against the original sequence); 0-1 cannot tell the alleles apart; empty when not computed |
| Engineered | `Yes` for the spacer built by the substitution rule (listed first for that variant), else `No` |
| Scaffold Intact | `No` when the spacer pulls the scaffold's own hairpin apart in the fold of the complete gRNA, else `Yes` |
| Spacer Unpaired | share (0-1) of the spacer left unpaired in that fold; empty when not computed |

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
    "mutated":  [ { "rank": 1, "sequence": "AGUU...", "score": 1, "gcContent": 53.57, "offtargets": 1,
                    "seedRegion": "UGGGCGGC", "homopolymers": 0, "energy": -13.100000381469727,
                    "wtMismatches": 1, "engineered": true, "scaffoldIntact": true, "spacerUnpaired": 0.57,
                    "structure": "(.(((((....((((...." } ],
    "original": [ ... ]
  }
]
```

`>` is escaped as `>` (valid JSON). `structure` (dot-bracket of the complete gRNA) exists only in JSON.
`rsId` holds the digits as a string (e.g. `"334"`), or `null`. Keys are emitted alphabetically (`hgvs, mutated,
notices, original, rsId, strand`); don't rely on key order. `notices` is an array of warning strings (usually empty).

### Pool plan CSV

```
Tube ID,Guides in Tandem,Well
1,Guide 1; Guide 2; Guide 3; Guide 4; Guide 5,Plate 1 - A1
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
$S design --hgvs 'NC_000017.11:g.7674220C>T' --format json | jq -r '.[] | [.hgvs, .mutated[0].sequence, .mutated[0].offtargets] | @tsv'

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
