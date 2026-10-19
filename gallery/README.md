# Example results (static site)

A small static site that shows saved results of the real `grna` tool: two variants (rs334, and the cystic fibrosis
F508del deletion as `NM_000492.4:c.1521_1523del`) and a pooling plan for 100 guides. It is published with GitLab Pages
from the `pages` job in [`.gitlab-ci.yml`](../.gitlab-ci.yml).

It is not the live tool. Nothing on it runs a design: every number was produced once by running Diana and saved here.

## What is in this folder

| Path | What it is |
|---|---|
| `data/` | The output files, exactly as the tool wrote them: report CSVs, JSON (with the dot-bracket folds), GenBank and SBOL 3 exports, pooling plans, `commands.txt` (the commands as run), `doctor.txt` and `provenance.json` |
| `gallery.css`, `gallery.js` | Styling and the light/dark switch. Colours and fonts come from the app's design tokens (`DiseaseMutationsApp/wwwroot`) |
| `../scripts/make-gallery-data.sh` | Regenerates `data/` by running the Diana Docker image (Bowtie, ViennaRNA, live NCBI) |
| `../scripts/build-gallery.py` | Builds the pages into `public/` with the Python 3 standard library only. Before writing any page it checks that the CSV and the JSON list the same spacers and that the seed and fold match |

## Regenerating

```bash
GRNA_APP_IMAGE=disease-mutations-app:latest GALLERY_SOURCE_COMMIT=<commit> scripts/make-gallery-data.sh
python3 scripts/build-gallery.py          # writes public/
```

Needs Docker, the image built from the commit you name, and internet access. The data depends on NCBI as it is on the day
you run it, so a later run can differ. `provenance.json` records the commit, the image and a note on how it was built.
Set `GALLERY_SOURCE_NOTE` to say more, for example if the image was built from a clean checkout.

## GitLab Pages

The `pages` job runs `scripts/build-gallery.py` on the default branch and publishes `public/`. It needs no Docker and
runs no Diana code. Pages has to be switched on for the project first (Deploy > Pages on gitlab.igem.org). Whether it is
enabled there is not known from this repository.

## What is not on the site

The interactive web app: runs and cancellation, the shortlist and the complete-gRNA card, the FORNA diagram, off-target
locations, the Disease variants search and the pooling decoder. These need Bowtie and ViennaRNA on a server, and live
NCBI access. Run Diana on your own machine to use them (`start.sh` in the repository root).
