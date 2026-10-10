#!/usr/bin/env bash
# Run the `grna` CLI from the Diana app image with the current directory mounted, so relative host paths
# (--input variants.txt, --out report.csv, --guides report.csv) work unchanged and outputs land next to you.
#
#   .claude/skills/diana/scripts/grna.sh doctor
#   .claude/skills/diana/scripts/grna.sh design --hgvs 'NC_000017.11:g.7674220C>T' > report.csv
#   .claude/skills/diana/scripts/grna.sh design --input variants.txt --out report.csv
#   printf 'rs334\n' | .claude/skills/diana/scripts/grna.sh design --input /dev/stdin --format json
#
# Works on Linux, macOS, WSL and Git Bash. Exit code is grna's own (0 ok, 1 failure, 2 bad input,
# 3 missing native dependency, 4 upstream/NCBI failure); 125 means Docker itself failed (e.g. image missing).
#
# Overrides: GRNA_APP_IMAGE (default disease-mutations-app:latest). GRNA_NCBI_API_KEY / GRNA_NCBI_CONTACT are
# forwarded when set.

set -euo pipefail

IMAGE="${GRNA_APP_IMAGE:-disease-mutations-app:latest}"

command -v docker >/dev/null 2>&1 || { echo "grna.sh: docker not found in PATH" >&2; exit 125; }
if ! docker image inspect "$IMAGE" >/dev/null 2>&1; then
	echo "grna.sh: image '$IMAGE' not found. Build it first: ./start.sh (or .\\start.ps1 on Windows)." >&2
	exit 125
fi

# Host directory to mount. Git Bash needs a Windows-style path and no MSYS path rewriting of /work.
host_dir="$(pwd)"
case "$(uname -s)" in
	MINGW*|MSYS*|CYGWIN*)
		host_dir="$(pwd -W)"
		export MSYS_NO_PATHCONV=1 MSYS2_ARG_CONV_EXCL='*'
		;;
esac

run_args=(run --rm
	-v "${host_dir}:/work"
	-w /work
	# The CLI resolves Bowtie relative to the CWD; with -w /work it must be told where the image keeps it.
	-e GRNA_BOWTIE_BINARY=/app/bowtie/bowtie-align-s
	-e GRNA_BOWTIE_INDEX=/app/bowtie/indexes/GRCh38_noalt_as
	--entrypoint grna)

# On native Linux, run as the calling user so files written to the bind mount are owned by you.
[ "$(uname -s)" = "Linux" ] && run_args+=(--user "$(id -u):$(id -g)")
# Forward stdin only when something is piped in (e.g. --input /dev/stdin).
[ -t 0 ] || run_args+=(-i)
[ -n "${GRNA_NCBI_API_KEY:-}" ] && run_args+=(-e GRNA_NCBI_API_KEY)
[ -n "${GRNA_NCBI_CONTACT:-}" ] && run_args+=(-e GRNA_NCBI_CONTACT)

exec docker "${run_args[@]}" "$IMAGE" "$@"
