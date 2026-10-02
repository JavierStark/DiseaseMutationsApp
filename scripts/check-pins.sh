#!/usr/bin/env bash
# Pinning gate: fails on floating dependency versions. Run in CI (lint stage) and locally.
set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

fail=0
report() { printf 'PIN VIOLATION: %s\n' "$1" >&2; fail=1; }

# 1. No ':latest' on any image we pull. The locally built output names (disease-mutations-app:latest,
#    <registry>/app:latest) are artefacts we produce, not inputs.
hits=$(grep -nE ':latest' Dockerfile Dockerfile.bowtie-base docker-compose.yml .gitlab-ci.yml 2>/dev/null \
  | grep -vE '^[^:]+:[0-9]+:\s*#' \
  | grep -vE 'disease-mutations-app:latest|/app:latest|GRNA_APP_IMAGE' || true)
[ -z "$hits" ] || report "floating ':latest' reference:
$hits"

# 2. Base images must be pinned by digest.
hits=$(grep -nE '^FROM [a-z]|^\s+image: [a-z]' Dockerfile Dockerfile.bowtie-base .gitlab-ci.yml 2>/dev/null \
  | grep -vE '@sha256:' || true)
[ -z "$hits" ] || report "image without @sha256 digest:
$hits"

# 3. pip installs must pin an exact version (==).
hits=$(grep -nE 'pip3? install' Dockerfile Dockerfile.bowtie-base 2>/dev/null | grep -vE '^[^:]+:[0-9]+:\s*#' | grep -vE '==' || true)
[ -z "$hits" ] || report "unpinned pip install:
$hits"

# 4. NuGet: no version ranges or wildcards; every project has a committed lock file.
hits=$(grep -rnE 'Version="[^"]*[*,[(][^"]*"' --include=*.csproj --include=*.fsproj --include=Directory.Packages.props . 2>/dev/null \
  | grep -vE '/(obj|bin)/' || true)
[ -z "$hits" ] || report "NuGet version range or wildcard:
$hits"
for proj in gRNA/gRNA.fsproj DiseaseMutationsApp/DiseaseMutationsApp.csproj gRNA.Cli/gRNA.Cli.fsproj DiseaseMutationsAppTests/DiseaseMutationsAppTests.csproj; do
  [ -f "$(dirname "$proj")/packages.lock.json" ] || report "missing packages.lock.json for $proj"
done
[ -f global.json ] || report "global.json missing"

# 5. Downloads baked into images must be checksum-verified.
grep -q 'sha256sum -c' Dockerfile.bowtie-base || report "Dockerfile.bowtie-base has no sha256 verification"

if [ "$fail" -eq 0 ]; then echo "pin check passed"; fi
exit "$fail"
