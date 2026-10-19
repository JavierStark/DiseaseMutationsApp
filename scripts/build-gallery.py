#!/usr/bin/env python3
"""Builds the static gallery site (GitLab Pages) from the saved results in gallery/data.

    python3 scripts/build-gallery.py                 # writes ./public
    python3 scripts/build-gallery.py --out some/dir

The files in gallery/data are the unedited output of the real tool (scripts/make-gallery-data.sh). This script only
arranges them as web pages: it computes nothing about guides. Before it writes anything it checks that the report CSV and
the JSON of each example list the same spacers, that the seed shown is the seed that was used and that the fold fits the
guide, and it fails loudly if a file does not look the way it expects. Standard library only; nothing is fetched.
"""
import argparse
import csv
import html
import json
import re
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT / "gallery" / "data"
SITE_SRC = ROOT / "gallery"
WWWROOT = ROOT / "DiseaseMutationsApp" / "wwwroot"

REPO_URL = "https://gitlab.igem.org/2026/software/uma-malaga/diana-grna-designer"
WIKI_URL = "https://2026.igem.wiki/uma-malaga/software"

esc = html.escape


class BuildError(Exception):
    pass


def need(condition, message):
    if not condition:
        raise BuildError(message)


# ---------------------------------------------------------------- reading the saved results

def read_text(path):
    return path.read_text(encoding="utf-8")


def read_report(path):
    """The CLI report: a '# Diana report; k=v; ...' provenance line, then CSV. Returns (parameters, rows)."""
    lines = read_text(path).splitlines()
    need(lines and lines[0].startswith("# Diana report;"), f"{path}: no provenance line")
    params = {}
    for part in lines[0][len("# Diana report;"):].split(";"):
        key, _, value = part.strip().partition("=")
        params[key] = value
    rows = list(csv.DictReader(line for line in lines if not line.startswith("#")))
    need(rows, f"{path}: no rows")
    return params, rows


def parse_seed(text):
    match = re.fullmatch(r"(\d+)-(\d+)", text)
    need(match, f"unexpected seed parameter: {text!r}")
    return int(match.group(1)), int(match.group(2))


def load_example(example_id):
    folder = DATA / example_id
    params, rows = read_report(folder / "report.csv")
    docs = {}
    for strand in ("normal", "complement"):
        doc = json.loads(read_text(folder / f"{strand}.json"))
        need(isinstance(doc, list) and len(doc) == 1, f"{folder}/{strand}.json: expected one variant")
        docs[strand] = doc[0]
    seed = parse_seed(params["seed"])
    repeat = params["repeat"]

    # The CSV and the JSON come from two runs of the same command: they must agree.
    for strand_key, doc in docs.items():
        strand_name = strand_key.capitalize()
        need(doc["strand"] == strand_name, f"{folder}: {strand_key}.json is the {doc['strand']} strand")
        for kind, key in (("Mutated", "mutated"), ("Original", "original")):
            from_csv = [r["Sequence"] for r in rows if r["Strand"] == strand_name and r["Sequence Type"] == kind]
            from_json = [g["sequence"] for g in doc[key]]
            need(from_csv == from_json, f"{folder}: {strand_key} {key}: the report CSV and the JSON list different spacers")
            for guide in doc[key]:
                need(guide["seedRegion"] == guide["sequence"][seed[0]:seed[1] + 1],
                     f"{folder}: seed region {guide['seedRegion']} is not letters {seed[0]}-{seed[1]} of {guide['sequence']}")
        first = doc["mutated"][0]
        need(len(first["structure"]) == len(repeat) + len(first["sequence"]),
             f"{folder}: the fold of the first guide is not as long as repeat + spacer")
    hgvs = {doc["hgvs"] for doc in docs.values()}
    need(len(hgvs) == 1, f"{folder}: the two strands analysed different notations")
    return {"id": example_id, "params": params, "seed": seed, "repeat": repeat, "docs": docs,
            "hgvs": hgvs.pop(), "rs": docs["normal"]["rsId"], "input": read_text(folder / "input.txt").strip()}


POOL_LINE = re.compile(r"^(?P<name>.+): V=(?P<v>\d+) K=(?P<k>\d+) -> (?P<wells>\d+) wells "
                       r"\((?P<prep>\d+) to prepare\), each guide in (?P<rep>\d+)$")
WELL = re.compile(r"^Plate (?P<plate>\d+) - (?P<row>[A-Z])(?P<col>\d+)$")


def load_pooling():
    folder = DATA / "pooling"
    models = {}
    for key in ("auto", "2df", "2dm", "3d"):
        line = read_text(folder / f"summary-{key}.txt").strip()
        match = POOL_LINE.match(line)
        need(match, f"pooling/summary-{key}.txt: unexpected line {line!r}")
        with open(folder / f"plan-{key}.csv", encoding="utf-8", newline="") as handle:
            tubes = list(csv.DictReader(handle))
        info = {k: (v if k == "name" else int(v)) for k, v in match.groupdict().items()}
        need(len(tubes) == info["wells"], f"pooling/plan-{key}.csv: {len(tubes)} tubes, summary says {info['wells']} wells")
        info["empty"] = sum(1 for t in tubes if t["Guides in Tandem"].startswith("Empty"))
        need(info["wells"] - info["empty"] == info["prep"], f"pooling/plan-{key}.csv: tubes to prepare do not match the summary")
        for tube in tubes:
            need(WELL.match(tube["Well"]), f"pooling/plan-{key}.csv: unexpected well {tube['Well']!r}")
        info["tubes"] = tubes
        models[key] = info
    need(len({(m["v"], m["k"]) for m in models.values()}) == 1, "the pooling runs used different V or K")
    chosen = [k for k in ("2df", "2dm", "3d") if models[k]["name"] == models["auto"]["name"]]
    need(len(chosen) == 1, "could not tell which model 'auto' picked")
    models["auto"]["picked"] = chosen[0]
    return models


# ---------------------------------------------------------------- page pieces

NAV = [("index.html", "Overview"), ("rs334.html", "Sickle cell, rs334"),
       ("f508del.html", "CFTR F508del"), ("pooling.html", "Pooling plan")]


def page(filename, title, description, body, prov):
    nav = "".join(
        '<a href="{}"{}>{}</a>'.format(href, ' aria-current="page"' if href == filename else "", esc(label)) for href, label in NAV)
    return f"""<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'self'; script-src 'self'; font-src 'self'; img-src 'self'; base-uri 'none'; form-action 'none'">
<title>{esc(title)} · Diana examples</title>
<meta name="description" content="{esc(description)}">
<link rel="icon" href="favicon.svg" type="image/svg+xml">
<link rel="stylesheet" href="tokens.css">
<link rel="stylesheet" href="gallery.css">
<script src="gallery.js"></script>
</head>
<body>
<a class="skip-link" href="#main">Skip to the content</a>
<header class="site-bar">
  <a class="brand" href="index.html" aria-label="Diana, examples overview">
    <img class="brand-light" src="img/diana-logotype-positive.svg" alt="" width="136" height="44">
    <img class="brand-dark" src="img/diana-logotype-negative.svg" alt="" width="136" height="44">
  </a>
  <nav class="site-nav" aria-label="Examples">{nav}</nav>
  <button type="button" class="theme-toggle" id="theme-toggle" hidden>Dark theme</button>
</header>
<main id="main">
<div class="notice" role="note"><strong>Saved results, not the live tool.</strong> Everything on this site was produced once, on
{esc(prov["generatedOn"])}, by running Diana (commit <code>{esc(prov["sourceCommit"])}</code>) on the example shown. The pages cannot analyse
a variant you type. To design guides for your own variants, <a href="{REPO_URL}#quick-start">run Diana on your machine</a>
(one Docker command).</div>
{body}
</main>
<footer class="site-foot">
  <p>Diana: precision guide RNA design. iGEM Team UMA-Malaga 2026, Software. MIT License.
  <a href="{REPO_URL}">Source code</a> · <a href="{WIKI_URL}">Team wiki, software page</a>.</p>
  <p>Fonts: IBM Plex Sans, JetBrains Mono and Abril Fatface (SIL Open Font License), served from this site.
  This site loads nothing from any other server.</p>
</footer>
</body>
</html>
"""


def pre(inner, label, css=""):
    """A preformatted block that can scroll sideways, so it must be reachable with the keyboard (WCAG 2.1.1)."""
    cls = f' class="{css}"' if css else ""
    return f'<pre{cls} tabindex="0" role="region" aria-label="{esc(label)}">{inner}</pre>'


def size_label(path):
    size = path.stat().st_size
    return f"{size / 1024:.1f} KB" if size >= 1024 else f"{size} bytes"


def downloads(items):
    """items: (path relative to gallery/data, label, what it is)."""
    out = ['<ul class="downloads">']
    for rel, label, what in items:
        path = DATA / rel
        need(path.exists(), f"missing download {rel}")
        out.append(f'<li><a href="data/{esc(rel)}" download>{esc(label)}</a>'
                   f'<span>{esc(what)} · {size_label(path)}</span></li>')
    out.append("</ul>")
    return "\n".join(out)


def offtarget_chip(count):
    if count <= 0:
        css, label = "chip-none", "0 none"
    elif count == 1:
        css, label = "chip-unique", "1 unique"
    elif count <= 3:
        css, label = "chip-caution", f"{count} caution"
    elif count < 6:
        css, label = "chip-high", f"{count} high"
    else:
        css, label = "chip-saturated", "6+ saturated"
    return f'<span class="chip {css}">{label}</span>'


def mismatch_cell(value, judge):
    if value is None:
        return "–"
    if judge and value <= 1:
        return (f'<span class="chip chip-high" title="Cannot reliably tell the alleles apart: it differs from the other '
                f'allele by 0 or 1 letter.">{value} not discriminating</span>')
    return str(value)


def spacer_html(sequence, seed):
    start, end = seed
    return (f'<span class="seq">{esc(sequence[:start])}<span class="seed">{esc(sequence[start:end + 1])}</span>'
            f'{esc(sequence[end + 1:])}</span>')


TABLE_HEAD = """<thead><tr>
<th scope="col" class="num" title="Rank among the candidates (the engineered guide, when there is one, is listed first and shares rank 1)">Rank</th>
<th scope="col" title="The spacer as RNA, 5' to 3'. The seed region is highlighted.">Spacer (RNA)</th>
<th scope="col" class="num" title="Relative score of this candidate among the candidates of this variant, 0 to 1. Not a prediction of guide activity.">Score</th>
<th scope="col" class="num" title="GC content of the spacer, percent">GC %</th>
<th scope="col" title="Genome alignments of the DNA target window (GRCh38, up to 2 mismatches). 6 means 6 or more.">Offtargets</th>
<th scope="col" class="num" title="Runs of 4 or more identical letters; 0 is best">Homopolymers</th>
<th scope="col" class="num" title="Minimum free energy of the folded guide (kcal/mol); closer to zero is preferred">Energy</th>
<th scope="col" title="Fewest letters in which the guide's DNA target differs from the closest stretch of the other allele">Mismatches vs other allele</th>
<th scope="col" title="Whether the direct repeat's own hairpin is still formed in the fold of the complete guide">Scaffold hairpin</th>
<th scope="col" class="num" title="Share of the spacer's letters left unpaired in that fold">Unpaired</th>
</tr></thead>"""


def guide_row(guide, seed, judge_mismatches):
    engineered = guide["engineered"]
    chip = ('<span class="chip chip-engineered" title="Substitution rule: one or two deliberate extra mismatches near the variant (positions 10 and 11 from the 3&#39; end by default; the variant is at 9). '
            'Not shown to discriminate alleles in the lab.">engineered</span> ') if engineered else ""
    hairpin = ('<span class="chip chip-ok">intact</span>' if guide["scaffoldIntact"]
               else '<span class="chip chip-caution" title="The direct repeat hairpin is no longer fully formed, so the guide may '
                    'not load into Cas13.">broken</span>')
    row_class = ' class="engineered"' if engineered else ""
    return (f'<tr{row_class}>'
            f'<td class="num">{guide["rank"]}</td>'
            f'<td>{chip}{spacer_html(guide["sequence"], seed)}</td>'
            f'<td class="num">{guide["score"]:.2f}</td>'
            f'<td class="num">{guide["gcContent"]:.2f}</td>'
            f'<td>{offtarget_chip(guide["offtargets"])}</td>'
            f'<td class="num">{guide["homopolymers"]}</td>'
            f'<td class="num">{guide["energy"]:.2f}</td>'
            f'<td>{mismatch_cell(guide["wtMismatches"], judge_mismatches)}</td>'
            f'<td>{hairpin}</td>'
            f'<td class="num">{guide["spacerUnpaired"] * 100:.0f}%</td></tr>')


def guide_table(guides, seed, caption, judge_mismatches):
    rows = "\n".join(guide_row(g, seed, judge_mismatches) for g in guides)
    return (f'<div class="table-wrap" tabindex="0" role="region" aria-label="{esc(caption)}">'
            f'<table><caption>{esc(caption)}</caption>{TABLE_HEAD}<tbody>\n{rows}\n</tbody></table></div>')


def fold_block(guide, repeat):
    spacer = guide["sequence"]
    structure = guide["structure"]
    n = len(repeat)
    inner = (f'<span class="part-dr">{esc(repeat)}</span><span class="part-spacer">{esc(spacer)}</span>\n'
             f'<span class="part-dr">{esc(structure[:n])}</span><span class="part-spacer">{esc(structure[n:])}</span>')
    return pre(inner, "Fold of the first guide: sequence, then dot-bracket structure", "fold")


COLUMN_GUIDE = """<details><summary>What the columns mean</summary><div class="details-body"><dl class="facts">
<dt>Rank, Score</dt><dd>Candidates are ordered by offtargets, then energy, then GC score, then homopolymers; ties share a rank. The score
(0 to 1) is a relative number for comparing the candidates of one variant. It is not a prediction of how well a guide works.</dd>
<dt>Offtargets</dt><dd>Alignments of the DNA window against the human genome (GRCh38, up to 2 mismatches, Bowtie 1.3.1). 1 is a unique
target; the count stops at 6 and is shown as "6+". 0 means no genomic match and is ranked best, by design: it is common for deletions,
insertions and duplications, whose junction windows do not exist in the genome. It is a genome check, not a transcriptome check.</dd>
<dt>Homopolymers</dt><dd>Runs of 4 or more identical letters; 0 is best.</dd>
<dt>Energy</dt><dd>Minimum free energy in kcal/mol (ViennaRNA 2.7.2, Turner 2004 parameters) of the guide folded with the 36 nt LwaCas13a
direct repeat in front; closer to zero is preferred.</dd>
<dt>Mismatches vs other allele</dt><dd>The fewest letters in which the guide's DNA target differs from the closest stretch of the other
allele (for a guide from the mutated sequence: the normal sequence). 0 or 1 cannot tell the alleles apart, 2 or more can. It informs the choice
and does not change the ranking.</dd>
<dt>Scaffold hairpin, Unpaired</dt><dd>Read from the same fold. "Broken" means the direct repeat's own hairpin is no longer fully formed, so the guide
may not load into Cas13. Unpaired is the share of the spacer left unpaired. Both inform the choice and do not change the ranking.</dd>
<dt>Engineered</dt><dd>For substitutions, a spacer with one or two deliberate extra mismatches near the variant (the substitution rule, by default the variant at position 9 and the mismatches at 10 and 11 from the 3' end; after the lab specification, Molina Vargas et al. 2024 and Shembrey et al. 2024, and the
SHERLOCK strategy). It is listed first and shares rank 1. The rule has not been shown to discriminate alleles in the lab.</dd>
</dl></div></details>"""


# ---------------------------------------------------------------- pages

def caveats_block(extra=()):
    items = ["Saved results are a snapshot: sequences and variants were read from NCBI when the tool ran. Running Diana later can give different numbers.",
             "A computational ranking is a way to choose which guides to test. It is not a measurement and not a clinical statement; "
             "check every variant and test every guide before relying on it."]
    items.extend(extra)
    lis = "".join(f"<li>{i}</li>" for i in items)
    return f'<div class="caveats"><strong>Read this before relying on a number</strong><ul>{lis}</ul></div>'


EXAMPLE_TEXT = {
    "rs334": {
        "title": "Sickle cell variant, rs334",
        "lead": "rs334 is the sickle-cell variant (Glu6Val). Diana resolved the rsID to one notation on the main gene record and designed "
                "guides for it on both strands, as the web app does by default.",
        "extra": ["The substitution rule (the engineered guide) has not been shown to discriminate alleles in the lab."],
    },
    "f508del": {
        "title": "Cystic fibrosis F508del (CFTR)",
        "lead": "F508del is a three-letter deletion in CFTR, entered as a coding position on a transcript record. Diana converted it to "
                "transcript coordinates and designed guides on the mRNA itself, on both strands.",
        "extra": ["For a deletion there is no substitution rule, so no guide is engineered. Offtarget counts come from the genome index, "
                  "so a window that spans the deletion junction does not exist in the genome and shows 0. That means no genomic match, not a better guide."],
    },
}


def example_page(ex, prov):
    text = EXAMPLE_TEXT[ex["id"]]
    seed, repeat = ex["seed"], ex["repeat"]
    accession = ex["hgvs"].split(":")[0]
    rs = ex["rs"]
    notices = sorted({n for doc in ex["docs"].values() for n in doc["notices"]})
    params = ex["params"]

    facts = [("You entered", f"<code>{esc(ex['input'])}</code>")]
    if rs:
        facts.append(("Resolved to", f"<code>{esc(ex['hgvs'])}</code> (dbSNP <a href=\"https://www.ncbi.nlm.nih.gov/snp/rs{esc(rs)}\">rs{esc(rs)}</a>)"))
    facts.append(("Reference record", f"<a href=\"https://www.ncbi.nlm.nih.gov/nuccore/{esc(accession)}\">{esc(accession)}</a> (NCBI)"))
    facts.append(("Parameters", f"spacer {esc(params['spacer'])} nt, seed {seed[0]}-{seed[1]} (0-based, inclusive), "
                                f"substitution rule: {esc(params['special-rule'])}"))
    facts.append(("Screening repeat", f"LwaCas13a direct repeat, <span class=\"mono\">{esc(repeat)}</span>"))
    facts.append(("Offtarget index", esc(params["off-target index"])))
    facts_html = "<dl class=\"facts\">" + "".join(f"<dt>{k}</dt><dd>{v}</dd>" for k, v in facts) + "</dl>"

    notice_html = ""
    if notices:
        notice_html = ('<div class="caveats"><strong>Notice from the tool</strong><ul>'
                       + "".join(f"<li>{esc(n)}</li>" for n in notices) + "</ul></div>")

    sections = []
    for key, heading, explain in (
            ("normal", "Normal strand", "Target windows are taken from the sequence as given; each spacer is the reverse complement of its window."),
            ("complement", "Complement strand", "Target windows are taken from the reverse complement of the sequence (the opposite strand read 5' to 3'); "
                                                "each spacer is the reverse complement of its window.")):
        doc = ex["docs"][key]
        first = doc["mutated"][0]
        fold = (f'<h3>Fold of the first guide listed</h3>'
                f'<p>The first guide of the table above folded with the direct repeat (grey) in front of the spacer (bold); '
                f'minimum free energy {first["energy"]:.2f} kcal/mol, direct repeat hairpin '
                f'{"intact" if first["scaffoldIntact"] else "broken"}, {first["spacerUnpaired"] * 100:.0f}% of the spacer unpaired. '
                f'Dot-bracket notation: a dot is an unpaired letter, brackets are paired letters.</p>{fold_block(first, repeat)}')
        sections.append(
            f'<section aria-labelledby="{key}"><h2 id="{key}">{heading}</h2><p>{explain}</p>'
            + guide_table(doc["mutated"], seed, f"{heading}, guides for the mutated sequence ({len(doc['mutated'])} candidates)", True)
            + fold
            + f'<details><summary>Guides for the original (normal) sequence, {len(doc["original"])} candidates</summary>'
            + guide_table(doc["original"], seed, f"{heading}, guides for the original sequence ({len(doc['original'])} candidates)", False)
            + '</details></section>')

    gb_block = pre("<code>" + esc(read_text(DATA / ex["id"] / "guides.gb")) + "</code>", "GenBank file contents")
    xml_block = pre("<code>" + esc(read_text(DATA / ex["id"] / "guides.xml")) + "</code>", "SBOL 3 file contents")
    exports = f"""<section aria-labelledby="exports"><h2 id="exports">Exports</h2>
<p>The files below are the tool's own output for this example. The two GenBank and SBOL 3 files hold the complete guide you would order for the
first guide listed on each strand: the LwaCas13a direct repeat and the spacer behind a T7 promoter
(<code>grna export report.csv --t7</code>). They were written by <code>grna export</code>; they are not hand-edited. The project's tests
read the files with Biopython and pySBOL3. Opening them in Benchling or SnapGene has not been checked.</p>
{downloads([
    (f"{ex['id']}/report.csv", "Report, CSV", "Both strands, the same schema as the web export and the Pooling import"),
    (f"{ex['id']}/normal.json", "Normal strand, JSON", "Adds the dot-bracket fold of each guide"),
    (f"{ex['id']}/complement.json", "Complement strand, JSON", "Adds the dot-bracket fold of each guide"),
    (f"{ex['id']}/guides.gb", "Complete guides, GenBank", "INSDC flat file, one record per strand"),
    (f"{ex['id']}/guides.xml", "Complete guides, SBOL 3", "RDF/XML, one Component per strand"),
])}
<details><summary>Preview the GenBank file</summary>{gb_block}</details>
<details><summary>Preview the SBOL 3 file</summary>{xml_block}</details>
</section>"""

    body = f"""<p class="kicker">Example, Builder results</p>
<h1>{esc(text['title'])}</h1>
<p class="lede">{esc(text['lead'])}</p>
{facts_html}
{notice_html}
{caveats_block(text['extra'])}
<ul class="strand-links" aria-label="On this page"><li><a href="#normal">Normal strand</a></li><li><a href="#complement">Complement strand</a></li><li><a href="#exports">Exports</a></li></ul>
<p class="small muted">Which strand matches the gene's mRNA depends on the gene's orientation on the record. The live web app reads it from dbSNP
for rsIDs and marks the tab; this saved page does not show it.</p>
{COLUMN_GUIDE}
{''.join(sections)}
{exports}
<p><a href="index.html#how">How these results were made</a> · <a href="index.html">Overview</a></p>"""
    return page(f"{ex['id']}.html", text["title"],
                f"Saved Diana results for {text['title']}: ranked Cas13 guide RNA spacers on both strands, with GenBank and SBOL 3 exports.",
                body, prov)


def plate_table(tubes, rows="ABCDEFGH", cols=12):
    placed = {}
    for tube in tubes:
        m = WELL.match(tube["Well"])
        need(m["plate"] == "1", "this gallery draws one plate only")
        need(m["row"] in rows and 1 <= int(m["col"]) <= cols, f"well {tube['Well']} is outside a 96-well plate")
        placed[(m["row"], int(m["col"]))] = tube
    head = "<tr><td></td>" + "".join(f'<th scope="col">{c}</th>' for c in range(1, cols + 1)) + "</tr>"
    body = []
    for r in rows:
        cells = []
        for c in range(1, cols + 1):
            tube = placed.get((r, c))
            if tube is None:
                cells.append('<td><span class="well"></span></td>')
            elif tube["Guides in Tandem"].startswith("Empty"):
                cells.append(f'<td><span class="well well-empty" title="Tube {esc(tube["Tube ID"])}: reserved by the layout, nothing to prepare"></span></td>')
            else:
                cells.append(f'<td><span class="well well-filled" title="Tube {esc(tube["Tube ID"])}: {esc(tube["Guides in Tandem"])}">{esc(tube["Tube ID"])}</span></td>')
        body.append(f'<tr><th scope="row">{r}</th>{"".join(cells)}</tr>')
    return (f'<div class="plate-scroll" tabindex="0" role="region" aria-label="96-well plate map"><table class="plate">'
            f'<caption>Plate 1, rows A to H, columns 1 to 12. The number in a well is its tube.</caption>'
            f'<thead>{head}</thead><tbody>{"".join(body)}</tbody></table></div>')


def tube_table(tubes, caption):
    rows = "".join(f'<tr><td class="num">{esc(t["Tube ID"])}</td><td>{esc(t["Guides in Tandem"])}</td><td>{esc(t["Well"])}</td></tr>' for t in tubes)
    return (f'<div class="table-wrap" tabindex="0" role="region" aria-label="{esc(caption)}"><table><caption>{esc(caption)}</caption>'
            f'<thead><tr><th scope="col" class="num">Tube</th><th scope="col">Guides in tandem</th><th scope="col">Well</th></tr></thead>'
            f'<tbody>{rows}</tbody></table></div>')


MODEL_TITLE = {"2df": "2D fragmented", "2dm": "2D matrix", "3d": "3D"}


def pooling_page(models, prov):
    v, k = models["auto"]["v"], models["auto"]["k"]
    picked = models["auto"]["picked"]
    stats = "".join(
        f'<div class="stat"><div class="stat-value">{models[key]["wells"]}</div>'
        f'<div class="stat-label">wells, {MODEL_TITLE[key]}</div></div>' for key in ("2df", "2dm", "3d"))
    stats += f'<div class="stat"><div class="stat-value">{v}</div><div class="stat-label">wells, one guide per well</div></div>'
    rows = "".join(
        f'<tr><th scope="row">{MODEL_TITLE[key]}</th><td class="num">{models[key]["wells"]}</td><td class="num">{models[key]["prep"]}</td>'
        f'<td class="num">{models[key]["rep"]}</td><td>{"chosen by <code>--model auto</code>" if key == picked else ""}</td></tr>'
        for key in ("2df", "2dm", "3d"))
    model_table = ('<div class="table-wrap" tabindex="0" role="region" aria-label="Pooling models compared">'
                   '<table><caption>The three pooling models for the same guides</caption><thead><tr>'
                   '<th scope="col">Model</th><th scope="col" class="num">Wells</th><th scope="col" class="num">Tubes to prepare</th>'
                   '<th scope="col" class="num">Each guide appears in</th><th scope="col">Note</th></tr></thead>'
                   f'<tbody>{rows}</tbody></table></div>')
    maps = []
    for key in ("2df", "2dm", "3d"):
        info = models[key]
        inner = plate_table(info["tubes"]) + tube_table(info["tubes"], f"{MODEL_TITLE[key]}: every tube")
        maps.append(f'<details{" open" if key == picked else ""}><summary>{MODEL_TITLE[key]}: plate map and tube list '
                    f'({info["wells"]} wells, {info["prep"]} tubes to prepare)</summary><div class="details-body">{inner}</div></details>')
    body = f"""<p class="kicker">Example, Guide Pooling</p>
<h1>Pooling plan for {v} guides</h1>
<p class="lede">Testing a whole variant panel one guide per well is not realistic, so Diana lays guides out in pools: at most {k} guides per tube,
each guide in several tubes, so that a positive well still points to its guide. This is the plan the tool wrote for {v} guides on a 96-well plate.</p>
<dl class="facts">
<dt>Guides</dt><dd>{v}, labelled Guide 1 to Guide {v}. They are placeholders: this is a layout, not a set of designed guides and not tied to any disease.</dd>
<dt>Guides per tube</dt><dd>at most {k} (the team's working assumption, because longer crRNA arrays may fold in ways that hinder single guides; it is adjustable)</dd>
<dt>Plate</dt><dd>96 wells</dd>
<dt>Command</dt><dd><code>grna pool --count {v} --capacity {k} --plate 96 --model auto</code> and the same with <code>2df</code>, <code>2dm</code> and <code>3d</code></dd>
</dl>
{caveats_block(["The pooling layouts have not been validated on real screening data. Testing of a pooled screen in the laboratory has started; there are no results yet.",
                "Decoding a screen (entering the positive wells to get the implicated guides) is part of the web app and is not shown here."])}
<h2>Wells needed</h2>
<div class="stats">{stats}</div>
{model_table}
<p>The two 2D models need the same number of wells here; the tool's automatic choice, <code>--model auto</code>, picked
<strong>{MODEL_TITLE[picked]}</strong>. The 3D model needs more wells but puts each guide in 3 tubes instead of 2.</p>
<h2>Plate maps</h2>
<div class="legend"><span><span class="well well-filled"></span>a tube to prepare (hover for its guides)</span>
<span><span class="well well-empty"></span>reserved by the layout, nothing to prepare</span><span><span class="well"></span>not used</span></div>
{''.join(maps)}
{downloads([("pooling/plan-auto.csv", "Plan chosen by auto, CSV", "Columns Tube ID, Guides in Tandem, Well"),
            ("pooling/plan-2df.csv", "2D fragmented, CSV", "The same columns"),
            ("pooling/plan-2dm.csv", "2D matrix, CSV", "The same columns"),
            ("pooling/plan-3d.csv", "3D, CSV", "The same columns")])}
<p><a href="index.html#how">How these results were made</a> · <a href="index.html">Overview</a></p>"""
    return page("pooling.html", f"Pooling plan for {v} guides",
                f"Saved Diana pooling plan for {v} guides on a 96-well plate, three models compared.", body, prov)


def index_page(prov, examples, models):
    commands_block = pre("<code>" + esc(read_text(DATA / "commands.txt").strip()) + "</code>", "Commands as run")
    doctor_block = pre("<code>" + esc(read_text(DATA / "doctor.txt").strip()) + "</code>", "Installation check output")
    install_block = pre("<code>git clone " + esc(REPO_URL) + ".git\ncd diana-grna-designer\n./start.sh            # Windows PowerShell: .\\start.ps1</code>",
                        "Commands to install and start Diana")
    cards = []
    for ex in examples:
        t = EXAMPLE_TEXT[ex["id"]]
        n_m = len(ex["docs"]["normal"]["mutated"]) + len(ex["docs"]["complement"]["mutated"])
        cards.append(f'<div class="card"><span class="kicker">Builder results</span><h3>{esc(t["title"])}</h3>'
                     f'<p><code>{esc(ex["input"])}</code>: {n_m} candidate spacers for the mutated sequence over both strands, '
                     f'complete guides as GenBank and SBOL 3.</p><a class="go" href="{ex["id"]}.html">See the results</a></div>')
    cards.append(f'<div class="card"><span class="kicker">Guide Pooling</span><h3>Pooling plan for {models["auto"]["v"]} guides</h3>'
                 f'<p>{models["2df"]["wells"]} wells in the 2D models and {models["3d"]["wells"]} in 3D, against {models["auto"]["v"]} for one guide per well.</p>'
                 f'<a class="go" href="pooling.html">See the plan</a></div>')
    est = prov["imageCreated"][:10]
    facts = [("Date", esc(prov["generatedOn"])),
             ("Code", f"commit <code>{esc(prov['sourceCommit'])}</code> ({esc(prov['sourceNote'])})"),
             ("Docker image", f"<code>{esc(prov['image'])}</code>, built {esc(est)}<br><span class=\"mono small muted\">{esc(prov['imageId'])}</span>"),
             ("Tools", "Bowtie 1.3.1, ViennaRNA 2.7.2, off-target index GRCh38 (no ALT contigs); see the checks below"),
             ("Data", "Sequences and variants were read from NCBI (E-utilities, dbSNP) while the tool ran, so they are as NCBI had them on that date."),
             ("Repeatable", "<code>scripts/make-gallery-data.sh</code> produces every data file; <code>scripts/build-gallery.py</code> turns them into these pages. "
                            "A second run on the same day gave identical designs.")]
    facts_html = "<dl class=\"facts\">" + "".join(f"<dt>{k}</dt><dd>{v}</dd>" for k, v in facts) + "</dl>"
    body = f"""<p class="kicker">iGEM Team UMA-Malaga 2026, Software</p>
<h1>Diana: example results</h1>
<p class="lede">Diana turns disease-related variants into ready-to-order CRISPR-Cas13 guide RNAs, then plans how to screen many of them on a plate.
This site shows saved results of the real tool for two variants and one pooling plan.</p>
<div class="cards">{''.join(cards)}</div>

<h2>What this site is, and is not</h2>
<p>These pages are the unedited output files of Diana's command-line tool, <code>grna</code>, saved in the project repository
(<code>gallery/data</code>). A script arranges them as web pages and tables; nothing is calculated in your browser.</p>
<p>The interactive web app is not here: runs you can cancel, the shortlist and the complete-gRNA card, the FORNA structure diagram, off-target
locations, the Disease variants search and the pooling decoder. They need a server with Bowtie, ViennaRNA and live access to NCBI.
Run Diana on your own machine to use them.</p>

<h2 id="how">How these results were made</h2>
{facts_html}
<p>The commands, exactly as run (each in the folder named after the example):</p>
{commands_block}
<p>The tool's own check of its installation, run before the designs:</p>
{doctor_block}
{downloads([("commands.txt", "Commands", "As run"), ("doctor.txt", "Installation check", "grna doctor output"),
            ("provenance.json", "Provenance", "Date, commit, image")])}

<h2>Read this before relying on a number</h2>
<div class="caveats"><ul>
<li>The ranking is a default order, not a measurement. The score compares candidates of one variant and is not a prediction of how well a guide works.</li>
<li>Offtargets come from a genome alignment, not a transcriptome one.</li>
<li>The substitution rule (the engineered guide) has not been shown to discriminate alleles in the lab. Pooling layouts and the adapters have not been
validated on real screening data.</li>
<li>These are candidates for testing, not clinical statements. ClinVar and dbSNP data change; check every variant.</li>
<li>The wording of the limits and the validation are in the <a href="{REPO_URL}#validation">project README</a>.</li></ul></div>

<h2>Run Diana yourself</h2>
<p>Needs Docker with Compose v2, about 10 GB of free disk for the first build and about 3 GB of RAM available to Docker.</p>
{install_block}
<p>Then open <code>http://localhost:5000</code>. The same code runs headless as <code>grna design --hgvs &lt;variant&gt;</code>.
See the <a href="{REPO_URL}#readme">README</a> and the <a href="{WIKI_URL}">team wiki software page</a>.</p>"""
    return page("index.html", "Example results", "Saved results of Diana, a CRISPR-Cas13 guide RNA design tool: two variants and a pooling plan.",
                body, prov)


# ---------------------------------------------------------------- assets and output

SHARED_ASSETS = [
    (WWWROOT / "css" / "tokens.css", "tokens.css"),
    (WWWROOT / "favicon.svg", "favicon.svg"),
    (WWWROOT / "img" / "diana-logotype-positive.svg", "img/diana-logotype-positive.svg"),
    (WWWROOT / "img" / "diana-logotype-negative.svg", "img/diana-logotype-negative.svg"),
] + [(WWWROOT / "fonts" / name, f"fonts/{name}") for name in (
    "abril-fatface-latin-400-normal.woff2", "ibm-plex-sans-latin-400-normal.woff2", "ibm-plex-sans-latin-600-normal.woff2",
    "jetbrains-mono-latin-400-normal.woff2", "jetbrains-mono-latin-600-normal.woff2",
    "LICENSE-Abril-Fatface.txt", "LICENSE-IBM-Plex-Sans.txt", "LICENSE-JetBrains-Mono.txt")]


def check_links(out):
    """Every local href/src of every page must point at a file in the output."""
    missing = []
    for path in sorted(out.glob("*.html")):
        for ref in re.findall(r'(?:href|src)="([^"#]+)(?:#[^"]*)?"', path.read_text(encoding="utf-8")):
            if re.match(r"^(https?:|mailto:)", ref):
                continue
            if not (out / ref).exists():
                missing.append(f"{path.name}: {ref}")
    need(not missing, "broken local links:\n  " + "\n  ".join(missing))


def build(out):
    prov = json.loads(read_text(DATA / "provenance.json"))
    examples = [load_example(i) for i in ("rs334", "f508del")]
    models = load_pooling()

    pages = {"index.html": index_page(prov, examples, models), "pooling.html": pooling_page(models, prov)}
    for ex in examples:
        pages[f"{ex['id']}.html"] = example_page(ex, prov)

    if out.exists():
        for child in out.iterdir():
            if child.is_dir():
                shutil.rmtree(child)
            else:
                child.unlink()
    out.mkdir(parents=True, exist_ok=True)
    for name, content in pages.items():
        (out / name).write_text(content, encoding="utf-8", newline="\n")
    for name in ("gallery.css", "gallery.js"):
        shutil.copyfile(SITE_SRC / name, out / name)
    for src, dest in SHARED_ASSETS:
        need(src.exists(), f"missing shared asset {src}")
        (out / dest).parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(src, out / dest)
    shutil.copytree(DATA, out / "data")
    check_links(out)
    return sorted(p.relative_to(out).as_posix() for p in out.rglob("*") if p.is_file())


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--out", default=str(ROOT / "public"), help="output directory (default: ./public); its contents are replaced")
    args = parser.parse_args()
    try:
        files = build(Path(args.out))
    except (BuildError, FileNotFoundError, KeyError, json.JSONDecodeError) as error:
        print(f"build-gallery: {error}", file=sys.stderr)
        return 1
    print(f"built {len(files)} files into {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
