<#
.SYNOPSIS  Run the `grna` CLI from the Diana app image with the current directory mounted (Windows PowerShell).
.DESCRIPTION
  Relative host paths (--input variants.txt, --out report.csv, --guides report.csv) work unchanged and outputs land
  in the current directory. The exit code is grna's own (0 ok, 1 failure, 2 bad input, 3 missing native dependency,
  4 upstream/NCBI failure); 125 means Docker itself failed (e.g. image missing).
  Overrides: $env:GRNA_APP_IMAGE (default disease-mutations-app:latest). GRNA_NCBI_API_KEY / GRNA_NCBI_CONTACT are
  forwarded when set.
.EXAMPLE  .\.claude\skills\diana\scripts\grna.ps1 doctor
.EXAMPLE  .\.claude\skills\diana\scripts\grna.ps1 design --hgvs 'NC_000017.11:g.7674220C>T' --out report.csv
.EXAMPLE  .\.claude\skills\diana\scripts\grna.ps1 design --input variants.txt --out report.csv
#>
$image = if ($env:GRNA_APP_IMAGE) { $env:GRNA_APP_IMAGE } else { 'disease-mutations-app:latest' }

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { [Console]::Error.WriteLine('grna.ps1: docker not found in PATH'); exit 125 }
docker image inspect $image *> $null
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("grna.ps1: image '$image' not found. Build it first: .\start.ps1")
    exit 125
}

$runArgs = @('run', '--rm',
    '-v', "$((Get-Location).ProviderPath):/work",
    '-w', '/work',
    # The CLI resolves Bowtie relative to the CWD; with -w /work it must be told where the image keeps it.
    '-e', 'GRNA_BOWTIE_BINARY=/app/bowtie/bowtie-align-s',
    '-e', 'GRNA_BOWTIE_INDEX=/app/bowtie/indexes/GRCh38_noalt_as',
    '--entrypoint', 'grna')
if ($env:GRNA_NCBI_API_KEY) { $runArgs += @('-e', 'GRNA_NCBI_API_KEY') }
if ($env:GRNA_NCBI_CONTACT) { $runArgs += @('-e', 'GRNA_NCBI_CONTACT') }

docker @runArgs $image @args
exit $LASTEXITCODE
