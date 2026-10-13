# Manual test plan

What the automated suites cannot check: the real Bowtie index, ViennaRNA and NCBI, the Docker install routes on each
operating system, assistive technology, and behaviour over a real network. Run this before a release or after changing the
algorithm, the Dockerfile or the scripts. The automated checks (`dotnet test`, which includes the 32 browser tests in
`DiseaseMutationsApp.E2E`) must be green first.

Record every run in section 9. A step that was not run is written as "not run", never left blank.

## 0. Preconditions

| | |
|---|---|
| Machine | Docker with Compose v2, about 10 GB free disk for a first build, 3 GB RAM for Docker, internet access (NCBI) |
| Code | the commit under test checked out, `git status` clean; write its short hash in the log |
| Image | `./start.sh` (or `.\start.ps1`) finished with "Ready", or the image was built with `docker build -t disease-mutations-app:review .` |
| Reference | `NC_000017.11:g.7674220C>T` (TP53) is used throughout because its expected output is recorded in `docs/review-findings.md` |

## 1. Install and health

| # | Step | Expected |
|---|---|---|
| 1.1 | Linux/macOS/WSL/Git Bash: `./start.sh`. Windows: `.\start.ps1` and `start.bat` in separate runs | Preflight checks pass, the images build or are reused, the script prints success only after `http://localhost:5000/healthz` answers; `start.bat` does not hang |
| 1.2 | `docker run --rm --entrypoint grna <image> doctor --network` | Every line `[PASS]`, exit 0 |
| 1.3 | `docker run --rm -w /tmp --entrypoint grna <image> doctor` (another working directory) | Same result as 1.2: the image sets `GRNA_BOWTIE_BINARY` and `GRNA_BOWTIE_INDEX` |
| 1.4 | `curl -s localhost:5000/healthz` from the host | HTTP 200 and `"healthy":true`; the checks list shows names and statuses only (a request from the host arrives from the Docker bridge, not loopback, so no paths) |
| 1.5 | Open `http://localhost:5000/diagnostics`, press **Run checks** | Table with all checks passing and the full detail (paths, versions) |
| 1.6 | Stop the app (`docker compose down`), start it with `GRNA_BIND=0.0.0.0` | Reachable from another device on the LAN; `/healthz` from that device shows no paths |
| 1.7 | `docker ps` after 2 minutes | Status `healthy` |

## 2. Design: one variant, real tools

| # | Step | Expected |
|---|---|---|
| 2.1 | Web app: enter `NC_000017.11:g.7674220C>T`, **Run analysis** | Done within about 30 s; one tab; mutated and original sequence cards; two tables; a single engineered spacer per table (substitution special rule, Rank 1, Score 100%) |
| 2.2 | Same variant, the `C` (complement) result of an rsID, or `grna design ... --complement` | Spacer sequence differs from 2.1; **Alignments is 1**, not 0; strand shown as "− strand, reverse complement, shown 5'→3'" |
| 2.3 | Change the spacer to 17, then to 61 | Message "between 18 and 60", **Run analysis** disabled |
| 2.4 | Enter `NC_000017.11:g.7674220A>T` (wrong reference base) | The variant's tab fails with "Reference mismatch ... states A but the sequence has C"; no stack trace; the other variants are unaffected |
| 2.5 | Enter `NM_000546.6:c.215_217del` | Fails with the message that `c.` positions are not converted |
| 2.6 | Enter `NC_000017.11:g.7674220_7674300del` (81 bp, longer than the spacer) | Fails with "mutated region is 0 nt, shorter than the spacer size" |
| 2.7 | Enter `NC_000017.11:g.7674220_7679220dup` (5 kb) | Completes (several minutes), no "Argument list too long" |
| 2.8 | Enter `rs334` | 24 or so variants resolve; each has a normal and a `C` tab; the run can be cancelled from the progress bar |
| 2.9 | Press **Run analysis** with 51 comma-separated variants | Message "At most 50 variants per run" and the button disabled |
| 2.10 | Open `http://localhost:5000/?hgvs=NC_000017.11:g.7674220C%3ET&spacer=30&seed=10-17` | Form filled, banner "opened from a link", **no run started**; the run starts only on the button |
| 2.11 | Download the report with **Download report** (tab) and **Download all reports**, for an HGVS input and for an rsID | CSV opens in Excel with correct accents (UTF-8 BOM); same columns as `grna design --format csv` |
| 2.12 | Edit a downloaded CSV so an `HGVS` cell starts with `=1+1`, load it again | Nothing is evaluated by the spreadsheet; the cell shows `'=1+1` |

## 3. Command line, real tools

Run through `.claude/skills/diana/scripts/grna.sh` or `grna.ps1` as well as `docker run`, because the wrappers differ per shell.

| # | Step | Expected |
|---|---|---|
| 3.1 | `grna design --hgvs 'NC_000017.11:g.7674220C>T' --format csv` | Report on stdout, exit 0 |
| 3.2 | Same with `--format json` and `--format tsv` | JSON parses (`rsId` is a string or null); TSV has one tab-separated record per row |
| 3.3 | `grna design --input list.txt --out report.csv`, interrupt with Ctrl-C, run again | Second run skips finished variants and appends; no duplicate rows |
| 3.4 | Repeat 3.3 with `--spacer 30` against the same `report.csv` | Refused, exit 2, message names both parameter sets |
| 3.5 | Repeat 3.3 with `--complement` and the same parameters | Appends the other strand's rows |
| 3.6 | `grna design --bogus 1 ...`, `grna design --help`, `grna pool --capacity 0 --count 10` | Exit 2 naming the option; usage and exit 0; exit 2 with a message (never a stack trace) |
| 3.7 | `grna pool --guides report.csv --capacity 5 --plate 96` | Plan CSV; a guide appearing on both strands counts once |
| 3.8 | `grna fold GAUUUAGACUACCCCAAAAACGAAGGGGACUAAAAC` | Two lines: sequence, then dot-bracket and energy |
| 3.9 | Disconnect the network, `grna design ...` | Exit 4 with an upstream error message |

## 4. Guide Pooling

| # | Step | Expected |
|---|---|---|
| 4.1 | `/pooling`: 100 guides, K = 5, 96-well, **Calculate distribution** | Three models compared; plate map and tube list; "Empty - do not prepare" wells are visibly different |
| 4.2 | Paste a Diana CSV report into **Guide list** | Count equals the number of distinct variants |
| 4.3 | Enter positive wells `A1, B3` and tube numbers `#12` | Positive wells are marked with a "+" as well as colour; the implicated guides are listed; ambiguous hits say so |
| 4.4 | Enter `#99999999999` | "Not recognised", the page keeps working |
| 4.5 | **Download CSV** and open it | Columns `Tube ID, Mixture Name, Guides in Tandem, Well` |
| 4.6 | 100,001 guides, or K = 10,001 | Refused with a message |

## 5. Accessibility by hand (what axe cannot judge)

| # | Step | Expected |
|---|---|---|
| 5.1 | Keyboard only through the whole design flow (Tab, Shift+Tab, arrows, Enter, Space) | Every control reachable and operable; focus always visible; tab strips move with the arrow keys |
| 5.2 | Skip link: Tab backwards from the heading, press Enter on **Skip to content** | Focus lands in the main content |
| 5.3 | Plate map with the arrow keys, Home and End | Focus moves between wells; the announcement names the well and its guides |
| 5.4 | NVDA or VoiceOver over a run | Progress is announced at milestones, not every few hundred milliseconds; a failed variant is announced as failed |
| 5.5 | Zoom to 200 % and 400 %, and a 375 px window | No loss of content, no sideways scrolling of the page; tables scroll inside their own region |
| 5.6 | Windows high-contrast mode, dark and light theme | Status shapes and text remain visible; text contrast acceptable |

## 6. Robustness

| # | Step | Expected |
|---|---|---|
| 6.1 | Start a long run, close the laptop lid or drop Wi-Fi for 30 s | "Connection lost" banner, the run keeps going, the page rejoins and shows the progress |
| 6.2 | Restart the container during a run | "Session expired" banner; inputs are restored from the browser; results are not |
| 6.3 | Run two browser tabs against the same server at once | Independent runs; no cross-talk |
| 6.4 | Upload a 2 MB text file as input | Rejected with a size message |
| 6.5 | Memory: `docker stats` during `rs334` | Stays within the limit set in `docker-compose.yml` |

## 7. Documentation

| # | Step | Expected |
|---|---|---|
| 7.1 | Follow the README "Installation" section on a clean machine without prior knowledge | It works as written |
| 7.2 | Every command in `.claude/skills/diana/SKILL.md` section 5 | Behaves as documented (the file says each line was run) |
| 7.3 | Open every link in the README and `docs/` | None broken |

## 8. Regression checklist for algorithm changes

Run after any edit to `gRNA/SpacerFinder.fs`, `Main.fs`, `HGVS.fs`, `Sequence.fs` or the Bowtie wrapper.

- 2.1 and 2.2 produce the same spacers and counts as the previous release, or the difference is explained in `BIOLOGICAL_REPORT.md`.
- A 3-nt delins, a 1-nt insertion, a 3-nt duplication and a 3-nt inversion at `NC_000017.11:g.7674220` still run (the first three give 0 alignments for the top candidates, by the documented rule).
- The numbers quoted in `BIOLOGICAL_REPORT.md` and the README are re-checked against the code.

## 9. Execution log

Add one block per run.

### 2026-10-13, commit `ef8df1b`..`b57582c` (review branches), image `disease-mutations-app:review`

Run by Claude Code at the request of the team lead; **not** a replacement for a human pass.

| Step | Result |
|---|---|
| 1.2, 1.3 | Run on the final image: all PASS, also from `-w /tmp` |
| 2.1 | Run: one engineered spacer per table, alignments 1 |
| 2.2 | Run: complement result had Alignments 1 after the fix (0 before) |
| 2.4, 2.5, 2.6 | Run: the three messages above, exit 2 |
| 2.7 | Run: exit 0 after 4 min 20 s, 1.8 MB report |
| 3.6 | Run on the final image: unknown option exit 2, `--capacity 0` exit 2, `design --help` exit 0 |
| 1.4 | Run: HTTP 200, names and statuses only |
| 2.10, 2.11 | Run in the browser pane: link fills the form without running; both report buttons offered for an HGVS input (the download itself was covered by the browser tests, not opened in Excel) |
| everything else | not run |

## 10. Results of the final real-stack pass

See the end of `docs/review-findings.md`.
