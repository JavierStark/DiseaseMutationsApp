#!/usr/bin/env bash
# One-command installer: preflight checks, build (or pull) the images, start the app, wait until it is healthy.
#
#   ./start.sh                     default: pull prebuilt images when GRNA_REGISTRY is set and reachable, else build locally
#   ./start.sh --build-local       build everything from source (works offline once the index zip is cached)
#   ./start.sh --rebuild           force rebuild of the app image
#   ./start.sh --rebuild-bowtie    force rebuild of the Bowtie index base image (implies --rebuild)
#
# The GRCh38 Bowtie index is baked into the image on every route: it is read memory-mapped, and a
# volume or bind mount would make alignment 10x+ slower (docs/runtime-contract.md).

set -euo pipefail

# Always operate from the repository root, wherever the script was invoked from.
cd "$(dirname "${BASH_SOURCE[0]}")"

BOWTIE_TAG="grch38-noalt-20260526"
BOWTIE_IMAGE="${GRNA_BOWTIE_BASE:-disease-mutations-bowtie:${BOWTIE_TAG}}"
APP_IMAGE="${GRNA_APP_IMAGE:-disease-mutations-app:latest}"
APP_URL="http://localhost:5000"
MIN_FREE_GB=10

REBUILD=false
REBUILD_BOWTIE=false
BUILD_LOCAL=false
export DOCKER_BUILDKIT=1 COMPOSE_DOCKER_CLI_BUILD=1

log() { printf '[install] %s\n' "$1"; }
die() { printf '[install] ERROR: %s\n' "$1" >&2; exit 1; }

usage() {
	sed -n '2,10p' "$0" | sed 's/^# \{0,1\}//'
}

for arg in "$@"; do
	case "$arg" in
		--rebuild) REBUILD=true ;;
		--rebuild-bowtie) REBUILD_BOWTIE=true; REBUILD=true ;;
		--build-local) BUILD_LOCAL=true ;;
		-h|--help) usage; exit 0 ;;
		*) die "Unknown argument: $arg. Use --help." ;;
	esac
done

# ---- Preflight ------------------------------------------------------------------------------------
command -v docker >/dev/null 2>&1 || die "Docker is not installed or not in PATH."
docker info >/dev/null 2>&1 || die "The Docker daemon is not running. Start Docker and try again."

if docker compose version >/dev/null 2>&1; then
	COMPOSE=(docker compose)
else
	die "Docker Compose v2 is required ('docker compose'). Install or update Docker."
fi

docker buildx version >/dev/null 2>&1 || log "warning: docker buildx not found; BuildKit features (cache mounts) may not work."
[ -f docker-compose.yml ] || die "docker-compose.yml not found next to start.sh."

need_base=false
if [ "$REBUILD_BOWTIE" = "true" ] || ! docker image inspect "$BOWTIE_IMAGE" >/dev/null 2>&1; then
	need_base=true
fi

# The base-image build holds the ~3.7 GB zip and the unpacked index at the same time.
if [ "$need_base" = "true" ] && [ "$BUILD_LOCAL" = "true" ] || { [ "$need_base" = "true" ] && [ -z "${GRNA_REGISTRY:-}" ]; }; then
	docker_root="$(docker info --format '{{.DockerRootDir}}' 2>/dev/null || echo /)"
	free_kb="$(df -Pk "$docker_root" 2>/dev/null | awk 'NR==2 {print $4}' || echo 0)"
	if [ -n "$free_kb" ] && [ "$free_kb" -gt 0 ] 2>/dev/null && [ "$free_kb" -lt $((MIN_FREE_GB * 1024 * 1024)) ]; then
		die "Only $((free_kb / 1024 / 1024)) GB free where Docker stores images; building the index image needs about ${MIN_FREE_GB} GB."
	fi
fi

# ---- Bowtie index base image ------------------------------------------------------------------------
if [ "$need_base" = "true" ]; then
	pulled=false
	if [ "$BUILD_LOCAL" = "false" ] && [ "$REBUILD_BOWTIE" = "false" ] && [ -n "${GRNA_REGISTRY:-}" ]; then
		log "Pulling prebuilt index image ${GRNA_REGISTRY}/bowtie-base:${BOWTIE_TAG} ..."
		if docker pull "${GRNA_REGISTRY}/bowtie-base:${BOWTIE_TAG}"; then
			docker tag "${GRNA_REGISTRY}/bowtie-base:${BOWTIE_TAG}" "$BOWTIE_IMAGE"
			pulled=true
		else
			log "Registry pull failed (unreachable or unauthenticated); falling back to a local build."
		fi
	fi
	if [ "$pulled" = "false" ]; then
		command -v curl >/dev/null 2>&1 || log "note: the index download runs inside the build; no host tools needed."
		log "Building the index base image ($BOWTIE_IMAGE). This downloads ~3.7 GB and takes a while ..."
		"${COMPOSE[@]}" --profile base build bowtie-base
	fi
else
	log "Index base image already present ($BOWTIE_IMAGE)."
fi

# ---- App image -----------------------------------------------------------------------------------------
if [ "$REBUILD" = "true" ] || ! docker image inspect "$APP_IMAGE" >/dev/null 2>&1; then
	built_or_pulled=false
	if [ "$BUILD_LOCAL" = "false" ] && [ "$REBUILD" = "false" ] && [ -n "${GRNA_REGISTRY:-}" ]; then
		if docker pull "${GRNA_REGISTRY}/app:latest"; then
			docker tag "${GRNA_REGISTRY}/app:latest" "$APP_IMAGE"
			built_or_pulled=true
		fi
	fi
	if [ "$built_or_pulled" = "false" ]; then
		log "Building the app image ($APP_IMAGE) ..."
		"${COMPOSE[@]}" build app
	fi
else
	log "App image already present ($APP_IMAGE)."
fi

log "Starting the application ..."
"${COMPOSE[@]}" up -d

# ---- Wait for health -------------------------------------------------------------------------------------
log "Waiting for ${APP_URL}/healthz ..."
healthy=false
for _ in $(seq 1 60); do
	if command -v curl >/dev/null 2>&1; then
		if curl -fsS -o /dev/null "${APP_URL}/healthz" 2>/dev/null; then healthy=true; break; fi
	elif [ "$(docker inspect -f '{{.State.Health.Status}}' disease-mutations-app 2>/dev/null)" = "healthy" ]; then
		healthy=true; break
	fi
	sleep 2
done

if [ "$healthy" != "true" ]; then
	"${COMPOSE[@]}" logs --tail 40 app || true
	die "The app did not become healthy. Run: docker run --rm --entrypoint grna ${APP_IMAGE} doctor"
fi

log "Ready: ${APP_URL}"
cat <<EOF

Useful commands:
  ${COMPOSE[*]} logs -f app          # follow logs
  ${COMPOSE[*]} down                 # stop
  docker run --rm --entrypoint grna ${APP_IMAGE} doctor   # validate the install
EOF
