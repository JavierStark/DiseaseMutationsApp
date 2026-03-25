#!/usr/bin/env bash

set -euo pipefail

BOWTIE_IMAGE="disease-mutations-bowtie:latest"
APP_IMAGE="disease-mutations-app:latest"
REBUILD=false
REBUILD_BOWTIE=false
BUILD_ENV=(DOCKER_BUILDKIT=1 COMPOSE_DOCKER_CLI_BUILD=1)

log() {
	printf '[install] %s\n' "$1"
}

die() {
	printf '[install] ERROR: %s\n' "$1" >&2
	exit 1
}

# Parse command-line arguments
for arg in "$@"; do
	case "$arg" in
		--rebuild)
			REBUILD=true
			;;
		--rebuild-bowtie)
			REBUILD_BOWTIE=true
			;;
		-h|--help)
			echo "Usage:"
			echo "  ./DiseaseMutationApp.sh                  # Install (skip rebuild if images exist)"
			echo "  ./DiseaseMutationApp.sh --rebuild        # Force rebuild app image only"
			echo "  ./DiseaseMutationApp.sh --rebuild-bowtie # Force rebuild bowtie base image"
			echo "  ./DiseaseMutationApp.sh --rebuild --rebuild-bowtie"
			exit 0
			;;
		*)
			die "Unknown argument: $arg. Use --help for available options."
			;;
	esac
done

if ! command -v docker >/dev/null 2>&1; then
	die "Docker is not installed or not available in PATH."
fi

if docker compose version >/dev/null 2>&1; then
	COMPOSE_CMD=(docker compose)
elif command -v docker-compose >/dev/null 2>&1; then
	COMPOSE_CMD=(docker-compose)
else
	die "Docker Compose is not available. Install Docker Compose v2 or docker-compose."
fi

if ! docker info >/dev/null 2>&1; then
	die "Docker daemon is not running. Start Docker and try again."
fi

if [ ! -f "docker-compose.yml" ]; then
	die "docker-compose.yml was not found. Run this script from the project root."
fi

if [ "$REBUILD_BOWTIE" = "true" ] || ! docker image inspect "$BOWTIE_IMAGE" >/dev/null 2>&1; then
	[ -f "Dockerfile.bowtie-base" ] || die "Missing Dockerfile.bowtie-base in project root."
	log "Building Bowtie base image ($BOWTIE_IMAGE) using Dockerfile.bowtie-base ..."
	env "${BUILD_ENV[@]}" docker build -f Dockerfile.bowtie-base -t "$BOWTIE_IMAGE" .
else
	log "Bowtie base image already exists ($BOWTIE_IMAGE)."
fi

log "Building and creating application container(s) ..."
if [ "$REBUILD" = "true" ] || ! docker image inspect "$APP_IMAGE" >/dev/null 2>&1; then
	log "Building app image ($APP_IMAGE)..."
	env "${BUILD_ENV[@]}" "${COMPOSE_CMD[@]}" up -d --build
else
	log "App image already exists ($APP_IMAGE). Starting container..."
	"${COMPOSE_CMD[@]}" up -d
fi

log "Install completed successfully."
echo
echo "Usage:"
echo "  ./DiseaseMutationApp.sh                  # Install (skip rebuild if images exist)"
echo "  ./DiseaseMutationApp.sh --rebuild        # Force rebuild app image only"
echo "  ./DiseaseMutationApp.sh --rebuild-bowtie # Force rebuild bowtie base image"
echo "  ./DiseaseMutationApp.sh --rebuild --rebuild-bowtie"
echo
echo "How to start the app:"
echo "  ${COMPOSE_CMD[*]} up -d"
echo "-----------------------------------"
echo "Useful commands:"
echo "  ${COMPOSE_CMD[*]} down          # Stop containers"
echo "  ${COMPOSE_CMD[*]} logs -f app   # Follow application logs"
echo
echo "Application URL: http://localhost:5000"