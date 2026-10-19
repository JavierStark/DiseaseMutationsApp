#!/usr/bin/env bash
# Regenerates gallery/data: the precomputed results the static demo shows (GitLab Pages, see gallery/README.md).
# Every file is the unedited output of the real tool, run once through the Docker image (Bowtie, ViennaRNA, live NCBI):
#
#   scripts/make-gallery-data.sh                                   # uses disease-mutations-app:latest
#   GRNA_APP_IMAGE=my-image GALLERY_SOURCE_COMMIT=391091a scripts/make-gallery-data.sh
#
# Needs Docker, the Diana image built from the commit you name, and internet access (NCBI). Takes about two minutes.
# Re-running changes the data: sequences and variants are read from NCBI at run time. Then build the page with
# scripts/build-gallery.py.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
ROOT="$(pwd)"
GRNA="$ROOT/.claude/skills/diana/scripts/grna.sh"
export GRNA_APP_IMAGE="${GRNA_APP_IMAGE:-disease-mutations-app:latest}"
DATA="$ROOT/gallery/data"
DATE_UTC="$(date -u +%F)"

[ -x "$GRNA" ] || { echo "make-gallery-data: $GRNA is not executable" >&2; exit 1; }

# Provenance: which code produced the data. The image cannot vouch for itself, so the operator names the commit
# (GALLERY_SOURCE_COMMIT) and may say how the image was built (GALLERY_SOURCE_NOTE); the page repeats both as written.
if [ -n "${GALLERY_SOURCE_COMMIT:-}" ]; then
  SOURCE_COMMIT="$GALLERY_SOURCE_COMMIT"
  SOURCE_NOTE="${GALLERY_SOURCE_NOTE:-commit named by whoever ran the script; not checked}"
else
  SOURCE_COMMIT="$(git rev-parse --short HEAD)"
  if [ -z "$(git status --porcelain -- gRNA gRNA.Cli DiseaseMutationsApp)" ]; then
    SOURCE_NOTE="${GALLERY_SOURCE_NOTE:-no uncommitted code changes when the script ran; the image is assumed to be built from this commit}"
  else
    SOURCE_NOTE="the code directories had uncommitted changes when the script ran, so the image may not match this commit"
    echo "warning: $SOURCE_NOTE." >&2
    echo "         Build the image from a clean checkout and set GALLERY_SOURCE_COMMIT." >&2
  fi
fi

IMAGE_ID="$(docker image inspect --format '{{.Id}}' "$GRNA_APP_IMAGE")"
IMAGE_CREATED="$(docker image inspect --format '{{.Created}}' "$GRNA_APP_IMAGE")"

# Clear the old data. Only files are removed: on Windows (OneDrive, antivirus) a directory can be locked for a while.
mkdir -p "$DATA"
find "$DATA" -type f -delete
: > "$DATA/commands.txt"

# run <directory> <grna arguments...>: runs the CLI in that directory (mounted as /work) and records the command.
run() {
  local dir="$1"; shift
  printf 'grna' >> "$DATA/commands.txt"
  printf ' %q' "$@" >> "$DATA/commands.txt"
  printf '    # in %s\n' "${dir#"$DATA/"}" >> "$DATA/commands.txt"
  ( cd "$dir" && "$GRNA" "$@" )
}

echo "== doctor"
"$GRNA" doctor > "$DATA/doctor.txt" || { cat "$DATA/doctor.txt" >&2; exit 1; }
cat "$DATA/doctor.txt"

# An example is one variant, designed on both strands as the web app does. The report CSV has both strands (the second run
# is appended to the first, as documented); the JSON files carry the fold of each guide and the notices.
example() {
  local id="$1" variant="$2"
  local dir="$DATA/$id"
  mkdir -p "$dir"
  printf '%s\n' "$variant" > "$dir/input.txt"
  echo "== $id ($variant)"
  run "$dir" design --input input.txt --out report.csv 2> "$dir/design-normal.log"
  run "$dir" design --input input.txt --out report.csv --complement 2> "$dir/design-complement.log"
  run "$dir" design --input input.txt --format json --out normal.json 2> /dev/null
  run "$dir" design --input input.txt --format json --complement --out complement.json 2> /dev/null
  run "$dir" export report.csv --t7 --format genbank --date "$DATE_UTC" --out guides.gb 2> "$dir/export-genbank.log"
  run "$dir" export report.csv --t7 --format sbol --out guides.xml 2> "$dir/export-sbol.log"
}

example rs334 "rs334"
example f508del "NM_000492.4:c.1521_1523del"

# Pooling: a layout for 100 guides, the example the README quotes. It is a plan, not tied to any disease.
echo "== pooling"
POOL="$DATA/pooling"
mkdir -p "$POOL"
for model in auto 2df 2dm 3d; do
  run "$POOL" pool --count 100 --capacity 5 --plate 96 --model "$model" --out "plan-$model.csv" 2> "$POOL/summary-$model.txt"
done

cat > "$DATA/provenance.json" <<EOF
{
  "generatedOn": "$DATE_UTC",
  "sourceCommit": "$SOURCE_COMMIT",
  "sourceNote": "$SOURCE_NOTE",
  "image": "$GRNA_APP_IMAGE",
  "imageId": "$IMAGE_ID",
  "imageCreated": "$IMAGE_CREATED"
}
EOF

echo "done: $(find "$DATA" -type f | wc -l) files in gallery/data"
