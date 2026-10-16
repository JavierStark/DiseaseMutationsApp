# Manual test plan

What the automated suites cannot check: the real Bowtie index, ViennaRNA and NCBI, the Docker install routes on each
operating system, assistive technology, and behaviour over a real network. Run this before a release or after changing the
algorithm, the Dockerfile or the scripts. The automated checks (`dotnet test`, which includes the 63 browser tests in
`DiseaseMutationsApp.E2E`) must be green first.

Record every run in section 9. A step that was not run is written as "not run", never left blank.

## 0. Preconditions

| | |
|---|---|
| Machine | Docker with Compose v2, about 10 GB free disk for a first build, 3 GB RAM for Docker, internet access (NCBI) |
| Code | the commit under test checked out, `git status` clean; write its short hash in the log |
| Image | `./start.sh` (or `.\start.ps1`) finished with "Ready", or the image was built with `docker build -t disease-mutations-app:review .` |
| Reference | `NC_000017.11:g.7674220C>T` (TP53) is used throughout because the expected output of each step below is stated for it |

## 1. Install and health

| # | Step | Expected |
|---|---|---|
| 1.1 | Linux/macOS/WSL/Git Bash: `./start.sh`. Windows: `.\start.ps1` and `start.bat` in separate runs | Preflight checks pass, the images build or are reused, the script prints success only after `http://localhost:5000/healthz` answers; `start.bat` does not hang |
| 1.2 | `docker run --rm --entrypoint grna <image> doctor --network` | Every line `[PASS]`, exit 0 |
| 1.3 | `docker run --rm -w /tmp --entrypoint grna <image> doctor` (another working directory) | Same result as 1.2: the image sets `GRNA_BOWTIE_BINARY` and `GRNA_BOWTIE_INDEX` |
| 1.4 | `curl -s localhost:5000/healthz` from the host | HTTP 200 and `"healthy":true`; the checks list shows names and statuses only (a request from the host arrives from the Docker bridge, not loopback, so no paths) |
| 1.5 | Open `http://localhost:5000/app-status`, press **Run checks** | Table with all checks passing and the full detail (paths, versions) |
| 1.6 | Stop the app (`docker compose down`), start it with `GRNA_BIND=0.0.0.0` | Reachable from another device on the LAN; `/healthz` from that device shows no paths |
| 1.7 | `docker ps` after 2 minutes | Status `healthy` |

## 2. Design: one variant, real tools

| # | Step | Expected |
|---|---|---|
| 2.1b | `grna design --hgvs NG_000007.3:g.81703T>C` (3 bases from the end of the record), then with `--complement` | Normal strand: ordinary ranking and a `note ...` line on stderr (the web page shows the same text as a warning); complement strand: the engineered spacer first and the ordinary list after it, no note |
| 2.1 | Web app: enter `NC_000017.11:g.7674220C>T`, **Run analysis** | Done within about 30 s; one tab; mutated and original sequence cards; two tables; the engineered spacer is the FIRST row of each table (highlighted, "Engineered" chip, Rank 1, Score 100%) and the ordinary ranked list follows it; every row has a *Mismatches vs other allele* value |
| 2.1c | Web app: enter `rs334`, **Run analysis** | One input tab with one variant, `NG_000007.3:g.70614A>T`, gene **HBB**; its first tab is marked **mRNA** and the second **antisense** (with a warning panel). Tick *Show all alleles and gene records* and run again: 12 notations (24 tabs). CLI: `grna resolve rs334` gives 1 line, `--all-alleles` gives 12 |
| 2.1d | Web app: `NC_000007.14:g.117559592_117559594del` (CFTR F508del) | Top mutated guides show 6 or more *Mismatches vs other allele*; a guide lying wholly in unchanged sequence shows a red "not discriminating" chip; the filter *Min mismatches vs other allele = 2* hides it. The page says the gene orientation is unknown |
| 2.1e | Hover each column header, filter label and the form options of the Builder, Disease variants and Pooling pages | A native tooltip explains each term; none is empty or off-topic |
| 2.2 | Same variant, the `C` (complement) result of an rsID, or `grna design ... --complement` | Spacer sequence differs from 2.1; **Alignments is 1**, not 0; strand shown as "− strand, reverse complement, shown 5'→3'" |
| 2.3 | Change the spacer to 17, then to 61 | Message "between 18 and 60", **Run analysis** disabled |
| 2.4 | Enter `NC_000017.11:g.7674220A>T` (wrong reference base) | The variant's tab fails with "Reference mismatch ... states A but the sequence has C"; no stack trace; the other variants are unaffected |
| 2.5 | Enter `NM_000518.5:c.20A>T` (needs internet) | Converted to `NM_000518.5:n.70A>T` (a notice says so; HBB CDS starts at 51); the sickle-cell guides appear, designed on the mRNA, and the tab says the spacers bind the mRNA |
| 2.5b | Enter `NG_000007.3:c.20A>T` and `NM_000518.5:c.92+2T>C` | Both fail with a clear message (genomic record: use the NM_ accession or g.; intronic offset: not in an mRNA) |
| 2.5c | In any result table, find a guide whose complete-gRNA fold breaks the scaffold hairpin | The Energy cell shows a "hairpin broken" chip; *Spacer unpaired* shows a percentage; hover both for the explanation. A variant with one ordinary candidate shows "only candidate" instead of 100 % |
| 2.6 | Enter `NC_000017.11:g.7674220_7674300del` (81 bp, longer than the spacer) | Fails with "mutated region is 0 nt, shorter than the spacer size" |
| 2.7 | Enter `NC_000017.11:g.7674220_7679220dup` (5 kb) | Completes (several minutes), no "Argument list too long" |
| 2.8 | Enter `rs334` | 24 or so variants resolve; each has a normal and a `C` tab; the run can be cancelled from the progress bar |
| 2.9 | Press **Run analysis** with 51 comma-separated variants | Message "At most 50 variants per run" and the button disabled |
| 2.10 | Open `http://localhost:5000/?hgvs=NC_000017.11:g.7674220C%3ET&spacer=30&seed=10-17` | Form filled, banner "opened from a link", **no run started**; the run starts only on the button |
| 2.11 | Download the report with **Download report** (tab) and **Download all reports**, for an HGVS input and for an rsID | CSV opens in Excel with correct accents (UTF-8 BOM); same columns as `grna design --format csv` |
| 2.12 | Edit a downloaded CSV so an `HGVS` cell starts with `=1+1`, load it again | Nothing is evaluated by the spreadsheet; the cell shows `'=1+1` |

## 2a. Disease variants (real Monarch and NCBI)

Needs only internet access, no Bowtie or ViennaRNA, so steps 2a.1 to 2a.12 also work with `dotnet run --project DiseaseMutationsApp
--launch-profile http` and `http://localhost:5000/disease`. Counts are what ClinVar held on 2026-10-13 and drift by a few records a
month: judge the order of magnitude and the behaviour, not the digit.

| # | Step | Expected |
|---|---|---|
| 2a.1 | Open `/disease?q=sickle%20cell%20disease` | The box holds `sickle cell disease`, a banner says the link only fills the form, and **nothing is searched** until you press **Find diseases** |
| 2a.2 | **Find diseases** | About 10 matches; the first is `sickle cell disease MONDO:0011382`, described `OMIM 603903` and preselected; the others say their subtypes will be consulted |
| 2a.3 | **Find variants** (default classification) | About 80 ClinVar records, 78 with an rsID, 2 showing a genomic HGVS; the summary also gives the distinct rsID count (62) because haplotype records share rsIDs. Type `rs334` in **Contains**: records of the HBB gene appear, among them `NM_000518.5(HBB):c.20A>T` |
| 2a.4 | Tick three rows and press **Send to Diana (replace)** | You land on the Diana page; the box holds exactly those three codes, a banner says so, and **no run has started**. Pressing **Run analysis** is the usual run (on the Docker route) |
| 2a.5 | Go back with the navigation; the table and your selection are still there. Tick two more (one already sent) and press **Add to Diana inputs** | The box keeps the first three and adds only the new one; the banner says "1 input added (1 already there)" |
| 2a.6 | The per-row **Send to Diana** button on a row with no rsID | The row's genomic HGVS (`NC_000011.10:g....`) goes into the box; Diana accepts it |
| 2a.7 | `phenylketonuria` then **Find variants** | About 923 records, 833 with an rsID. **Pathogenic only** lists fewer |
| 2a.8 | `cystic fibrosis`, **Find variants**, then **Select all ... matching** | About 1,500 records. The action bar says Diana takes at most 50 inputs and the two send buttons are disabled; **Copy list** and **Download list (.txt)** still work (open the file: one rsID or HGVS per line) |
| 2a.9 | `Fanconi anemia` | The first match is a group ("phenotypic series PS227650"); **Also search its subtypes** is ticked and cannot be unticked. About 30 s later the summary says "Consulted 22 of 22 subtypes" and, because about 4,500 records match, a warning says the list is **incomplete** (3,000 read) |
| 2a.10 | `Huntington disease` | 8 records on the day: 3 with an rsID, 3 with only a genomic HGVS, 2 repeat expansions with neither (their checkboxes are disabled, the summary says so). Two records are only loosely related (a TTR variant and a PRNP one): their conditions column shows why, and the page note warns that this happens |
| 2a.11 | `zzzzqq`; then `OMIM:999999999`; then `OMIM:12ab`; then `22` | "No disease matched"; "No variants at this classification"; two messages "is not an OMIM number" (no request is made for the last two) |
| 2a.12 | Start a search, press **Cancel** at once; then disconnect the network and search | "Search cancelled." and the page is usable; then a red message naming Monarch or NCBI with **Try again** (it works once the network is back) |
| 2a.13 | Filter, sort by **Gene** and **Review**, change **Rows**, use **Previous/Next** | Rows change accordingly; the sorted header exposes its direction; the page number returns to 1 after a new filter |
| 2a.14 | Keyboard only: Tab to a row checkbox, Space, Tab to **Send to Diana (replace)**, Enter | Works; focus order follows the visual order; with a screen reader the selection count is announced |
| 2a.15 | Narrow the window to about 375 px | The page does not scroll sideways (the table scrolls inside its own box); the selection bar stays usable |
| 2a.16 | CLI: `grna disease 603903 --out sickle.txt`; `grna disease "Fanconi anemia" --list`; `grna disease 603903 --format csv` | `sickle.txt` holds about 62 distinct `rs...` lines (contains `rs334`); the list shows tab-separated matches; the CSV has the header `rsID,HGVS,Gene,Classification,Review status,Stars,ClinVar ID,Variant,Conditions`. Exit 0; `grna disease zzzzqq` exits 2 |
| 2a.17 | Docker route: `grna design --input sickle.txt --out report.csv` inside the image | A design report for the rsIDs that dbSNP maps to an `NG_` record; the others are reported "no notations", not as a failure of the run |

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

### 2026-10-13, branch `feature/disease-variants` (Disease variants), native `dotnet run`, real Monarch and NCBI

Run by Claude Code while building the feature; **not** a replacement for a human pass. Docker image and Bowtie/ViennaRNA were not used.

| Step | Result |
|---|---|
| 2a.1, 2a.2, 2a.3 | Run in the browser pane: link fills the box without searching; 10 matches, the first `sickle cell disease`; 80 records, 78 with an rsID |
| 2a.4 | Run: three rows sent with *replace*; the Diana page showed the three codes and the banner, no run started. The run itself was not done (needs the Docker route) |
| 2a.8 | Partly run: selecting all 64 inputs disabled both send buttons and showed the 50-input message. The copy and download buttons were not clicked in the pane (covered by the browser tests) |
| 2a.9 | Run: 22 of 22 subtypes, 4,543 matched, 3,000 read, "incomplete" warning, about 30 s |
| 2a.10 | Run: 8 records (3 rsIDs, 3 HGVS-only, 2 neither); two loosely related records seen (TTR, PRNP) |
| 2a.11 | Run: the four messages (a first run was spoiled by the hidden pane dropping the websocket and was repeated) |
| 2a.16 | Run with the built CLI: `grna disease 603903` (62 distinct rsIDs, contains rs334), `--list`, `--format csv`, exit 2 for `zzzzqq` and `OMIM:12ab` |
| 2a.5, 2a.6, 2a.7 (UI), 2a.12 (real network cut), 2a.13, 2a.14, 2a.15, 2a.17 | not run by hand; 2a.5 to 2a.7, 2a.13 to 2a.15 are covered by the browser tests against a fake source, 2a.7 by the CLI (923 records, 833 with an rsID) |
| Live contract tests | `GRNA_LIVE_TESTS=1 dotnet test --filter Category=Live`: 9 passed. They found the dropped compound classifications (cystic fibrosis) |

