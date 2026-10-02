<#
.SYNOPSIS  One-command installer for Windows: preflight, build or pull images, start, wait for health.
.EXAMPLE   .\start.ps1               # default
.EXAMPLE   .\start.ps1 -BuildLocal   # build everything from source
.EXAMPLE   .\start.ps1 -Rebuild -RebuildBowtie
The GRCh38 Bowtie index stays baked into the image: it is read memory-mapped and a volume would be 10x+ slower.
#>
param([switch]$BuildLocal, [switch]$Rebuild, [switch]$RebuildBowtie)

$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot
$env:DOCKER_BUILDKIT = '1'

$bowtieTag = 'grch38-noalt-20260526'
$bowtieImage = if ($env:GRNA_BOWTIE_BASE) { $env:GRNA_BOWTIE_BASE } else { "disease-mutations-bowtie:$bowtieTag" }
$appImage = if ($env:GRNA_APP_IMAGE) { $env:GRNA_APP_IMAGE } else { 'disease-mutations-app:latest' }
$appUrl = 'http://localhost:5000'
if ($RebuildBowtie) { $Rebuild = $true }

function Log($m) { Write-Host "[install] $m" }
function Die($m) { Write-Host "[install] ERROR: $m" -ForegroundColor Red; exit 1 }
function Test-Image($name) { docker image inspect $name *> $null; return ($LASTEXITCODE -eq 0) }

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { Die 'Docker is not installed or not in PATH.' }
docker info *> $null
if ($LASTEXITCODE -ne 0) { Die 'The Docker daemon is not running. Start Docker Desktop and try again.' }
docker compose version *> $null
if ($LASTEXITCODE -ne 0) { Die "Docker Compose v2 is required ('docker compose')." }

$needBase = $RebuildBowtie -or -not (Test-Image $bowtieImage)
if ($needBase) {
    $pulled = $false
    if (-not $BuildLocal -and -not $RebuildBowtie -and $env:GRNA_REGISTRY) {
        Log "Pulling prebuilt index image $($env:GRNA_REGISTRY)/bowtie-base:$bowtieTag ..."
        docker pull "$($env:GRNA_REGISTRY)/bowtie-base:$bowtieTag"
        if ($LASTEXITCODE -eq 0) { docker tag "$($env:GRNA_REGISTRY)/bowtie-base:$bowtieTag" $bowtieImage; $pulled = $true }
        else { Log 'Registry pull failed; falling back to a local build.' }
    }
    if (-not $pulled) {
        Log "Building the index base image ($bowtieImage): ~3.7 GB download, needs ~10 GB free disk ..."
        docker compose --profile base build bowtie-base
        if ($LASTEXITCODE -ne 0) { Die 'Building the index base image failed.' }
    }
} else { Log "Index base image already present ($bowtieImage)." }

if ($Rebuild -or -not (Test-Image $appImage)) {
    Log "Building the app image ($appImage) ..."
    docker compose build app
    if ($LASTEXITCODE -ne 0) { Die 'Building the app image failed.' }
}

Log 'Starting the application ...'
docker compose up -d
if ($LASTEXITCODE -ne 0) { Die 'docker compose up failed.' }

Log "Waiting for $appUrl/healthz ..."
$healthy = $false
for ($i = 0; $i -lt 60 -and -not $healthy; $i++) {
    try { Invoke-WebRequest -Uri "$appUrl/healthz" -UseBasicParsing -TimeoutSec 5 | Out-Null; $healthy = $true }
    catch { Start-Sleep -Seconds 2 }
}
if (-not $healthy) {
    docker compose logs --tail 40 app
    Die "The app did not become healthy. Run: docker run --rm --entrypoint grna $appImage doctor"
}
Log "Ready: $appUrl"
