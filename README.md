# Disease Mutations App

A web application for analyzing genetic mutations and generating guide RNA (gRNA) sequences for CRISPR applications. This tool lets researchers input disease-related mutations in HGVS format or rsIDs, and automatically generates optimized gRNA spacers for genome editing.

## 🧬 Overview

The Disease Mutations App is designed to streamline the process of designing CRISPR guide RNAs for therapeutic applications. It takes Human Genome Variation Society (HGVS) formatted mutation data or SNP reference IDs (rsIDs), retrieves the relevant genomic sequences, and identifies optimal gRNA spacer sequences based on multiple quality metrics — for both the mutated sequence and the original (wild-type) sequence.

### Key Features

- **Multiple Input Types**:
  - **HGVS Format**: Standard genome variation notation (e.g., `NC_000017.11:g.7674220C>T`)
  - **rsID Support**: SNP reference IDs (e.g., `rs12345`), resolved to their HGVS notations
  - Multiple inputs at once, comma-separated
- **Automated Sequence Retrieval**: Fetch genomic sequences from NCBI databases
- **Complement-Strand Analysis**: Each rsID-derived HGVS variant is analyzed on both strands — a normal tab and a complement (`(C)`) tab
- **Dual Result Tables**: Ranked gRNA candidates are computed for both the **mutated** sequence and the **original** (wild-type) sequence, shown side by side
- **gRNA Optimization**: Generate and rank gRNA candidates based on:
  - GC content and GC score (optimal range: 40-60%)
  - Homopolymer runs (penalizes runs of 4+ consecutive identical bases)
  - Off-target alignment analysis using Bowtie
  - RNA secondary structure prediction using ViennaRNA (folds the complete gRNA: scaffold + spacer)
- **Configurable Spacer Size and Seed Region**: Choose the spacer length and the inclusive seed-region range highlighted in the results and reported in the CSV export
- **Substitution Special Rule**: for substitution SNPs, automatically engineers a mismatch-optimized spacer near the 3' end when the mutation lands at the right position (see [BIOLOGICAL_REPORT.md](BIOLOGICAL_REPORT.md), section 4.4) — when it applies, that single spacer replaces the ranked candidate list for that variant, by design
- **Sortable Result Tables**: Click any column header to sort by sequence, GC score, GC content, homopolymer count, alignments, energy, or score
- **CSV Report Export**: Download a report per rsID or a combined report for all rsID tabs
- **Visual Comparison**: Side-by-side display of original and mutated sequences with highlighted mutations
- **Interactive Tabbed Interface**: Analyze multiple variants simultaneously with dynamic tab and subtab management
- **NCBI Source Link**: Quick link to the source accession on NCBI for each HGVS variant
- **State Persistence**: Navigate between pages without losing your work (within the same session)

> **Note on OMIM support:** the application includes code for converting an OMIM disease code to a list of associated rsIDs (`gRNA/Omim.fs`, an `/omim-to-rs` page, and a navigation menu entry), but this feature is currently **disabled**. OMIM's website added a Cloudflare CAPTCHA that blocks the scraper this feature relies on, so the OMIM source file is excluded from the F# build and the page/nav entry are commented out. See [BIOLOGICAL_REPORT.md](BIOLOGICAL_REPORT.md), section 8, for the intended design.

## 🏗️ Architecture

The application uses a modern **Blazor Server** architecture with a monolithic design optimized for local deployment:

### 1. Blazor Server Application

- **Technology**: ASP.NET Core 9.0 with Blazor Server
- **Language**: C# (implicit usings, nullable reference types enabled)
- **Render Mode**: Interactive Server (SignalR-based real-time communication)
- **UI Framework**: Bootstrap 5 with Blazor.Bootstrap components
- **Port**: 5000 (HTTP)

### 2. gRNA Library (F# Module)

- **Language**: F# (targeting .NET 9.0, FSharp.Core 10.0.101)
- **Purpose**: Core bioinformatics logic and external tool integration
- **Key Modules**:
  - `Main.fs`: Public API entry point for C# interop (`getBestgRNAFromHGVS`)
  - `SpacerFinder.fs`: gRNA candidate generation, scoring, and ranking
  - `BowtieWrapper.fs` / `BowtieService.fs`: Off-target alignment analysis (serialized via a semaphore)
  - `RNAFoldWrapper.fs`: RNA secondary structure prediction (ViennaRNA, via a Python subprocess)
  - `HGVS.fs`: HGVS notation parser
  - `Sequence.fs`: DNA sequence manipulation (mutation application, complementing)
  - `SequenceRepository.fs`: NCBI sequence retrieval (in-process cache by accession)
  - `SNP.fs`: SNP database integration (dbSNP)
  - `Omim.fs` / `LevenshteinDistance.fs`: OMIM disease database integration — **excluded from the build, disabled** (see above)

### 3. Services Layer

- **GrnaService**: C# wrapper for the F# library functions, mapping F# records to C# records for the UI
- **AppStateService**: Scoped state management for cross-page navigation and data persistence

## 🐳 Docker Architecture

The application is built as **two Docker images**:

### 1. Bowtie base image (`Dockerfile.bowtie-base`)

Starts from the ASP.NET runtime image and downloads the GRCh38 Bowtie2 indexes (`GRCh38_noalt_as.zip`, ~4 GB) from the public `genome-idx` S3 bucket using `s5cmd`, unpacking them into `/app/bowtie/indexes`. This image is tagged `disease-mutations-bowtie:latest` and only needs to be rebuilt when the reference indexes change.

### 2. Application image (`Dockerfile`)

Multi-stage build on top of the Bowtie base image:

1. **Build stage**: restores and builds the C# + F# solution with the .NET SDK image.
2. **Publish stage**: publishes the Blazor Server app.
3. **Final stage**: starts `FROM disease-mutations-bowtie:latest`, installs Python 3 + `viennarna` via pip, copies the published app and the `bowtie/bowtie-align-s` executable.

**Build Flow**:

```
Dockerfile.bowtie-base  →  disease-mutations-bowtie:latest
                                        │
Dockerfile (build + publish + final)   │  (FROM disease-mutations-bowtie:latest)
        ↓
disease-mutations-app:latest
```

### Deployment Architecture

The application runs as a **single container** with integrated server and client functionality:

```
┌─────────────────────────────────────┐
│  disease-mutations-app:latest       │
│  ┌─────────────────────────────┐   │
│  │   Blazor Server App         │   │
│  │   - HTTP Server              │   │
│  │   - SignalR Hub              │   │
│  │   - Razor Components         │   │
│  │   - Static Files             │   │
│  └──────────┬──────────────────┘   │
│             │                        │
│  ┌──────────▼──────────────────┐   │
│  │   GrnaService (C#)          │   │
│  │   - Direct F# library calls │   │
│  └──────────┬──────────────────┘   │
│             │                        │
│  ┌──────────▼──────────────────┐   │
│  │   F# gRNA Library           │   │
│  │   - HGVS parsing            │   │
│  │   - Sequence retrieval      │   │
│  │   - Bowtie alignment        │   │
│  │   - RNA folding             │   │
│  │   - SNP integration         │   │
│  └─────────────────────────────┘   │
│                                      │
│  External Dependencies:              │
│  - Bowtie (alignment)               │
│  - ViennaRNA (structure prediction) │
│  - GRCh38 Bowtie2 indexes (~4 GB)   │
└─────────────────────────────────────┘
```

## 📋 Prerequisites

### Required Software

- **.NET 9.0 SDK** or later
- **Docker** and **Docker Compose** (for containerized deployment)
- **Python 3** with **ViennaRNA** library (for RNA folding functionality, local development only)
  - Install: `pip install viennarna` or `pip3 install viennarna`

### Required Data

- **GRCh38 Reference Genome** Bowtie2 indexes, resolved at runtime from `bowtie/indexes/` (any `.bt2`/`.bt2l` files, per [BowtieWrapper.fs](gRNA/BowtieWrapper.fs))
  - The repository ships the `bowtie/bowtie-align-s` executable but **not** the index files themselves — they are downloaded during the Docker build (`Dockerfile.bowtie-base`) or must be provided manually for local (non-Docker) development.

### System Requirements

- **CPU**: 2+ cores (recommended for optimal performance)
- **RAM**: 2GB minimum (4GB recommended)
- **Storage**: ~5GB for the reference genome indexes
- **Network**: Internet connection required for NCBI API calls (sequence retrieval, SNP lookup) and for downloading the reference indexes during the Docker build

## 🚀 Getting Started

### ⚠️ Very Important for WSL Users

If you run Docker through WSL2 and do **not** set a memory limit, WSL can consume nearly **100% of system RAM**, which can slow down your computer and make downloads/builds significantly slower.

Recommended limit: **2GB**

Create or edit `~/.wslconfig` (Windows path: `C:\Users\<your-user>\.wslconfig`) with:

```ini
[wsl2]
memory=2GB
```

Then restart WSL:

```bash
wsl --shutdown
```

### Option 1: Docker Deployment with the Installer Script (Recommended)

1. **Clone the repository**:

   ```bash
   git clone https://github.com/JavierStark/DiseaseMutationsApp.git
   cd DiseaseMutationsApp
   ```

2. **Run the installer script**:

   ```bash
   chmod +x start.sh
   ./start.sh
   ```

   This script checks for Docker/Docker Compose, builds the Bowtie base image if it doesn't exist yet, builds the app image when needed, and starts the container.

   On Windows, `start.bat` runs the same script through Git Bash.

3. **Force a rebuild when needed**:

   ```bash
   ./start.sh --rebuild            # Force rebuild of the app image only
   ./start.sh --rebuild-bowtie     # Force rebuild of the Bowtie base image (re-downloads indexes)
   ./start.sh --rebuild --rebuild-bowtie
   ```

4. **Access the application**:
   - Application: http://localhost:5000

#### Useful script-driven operations

```bash
# Start (without rebuilding when images already exist)
./start.sh

# Rebuild the app image and start
./start.sh --rebuild

# Rebuild the Bowtie base image (re-downloads the ~4GB reference indexes)
./start.sh --rebuild-bowtie

# Show script usage
./start.sh --help
```

### Option 2: Manual Docker Compose

1. **Clone the repository**:

   ```bash
   git clone https://github.com/JavierStark/DiseaseMutationsApp.git
   cd DiseaseMutationsApp
   ```

2. **Build the Bowtie base image** (only needed once, or when the reference indexes need updating):

   ```bash
   docker build -f Dockerfile.bowtie-base -t disease-mutations-bowtie:latest .
   ```

3. **Build and run the app with Docker Compose**:

   ```bash
   docker compose up -d --build
   ```

4. **Access the application**:
   - Application: http://localhost:5000

#### Quick Rebuild During Development

```bash
# Rebuild and restart the application
./start.sh --rebuild

# Manual alternative
docker compose up -d --build

# View logs
docker compose logs -f app
```

### Option 3: Local Development (without Docker)

1. **Clone the repository**:

   ```bash
   git clone https://github.com/JavierStark/DiseaseMutationsApp.git
   cd DiseaseMutationsApp
   ```

2. **Install ViennaRNA** (required for RNA folding):

   ```bash
   pip install viennarna
   # or
   pip3 install viennarna
   ```

3. **Provide Bowtie2 indexes**: place GRCh38 `.bt2`/`.bt2l` index files under `bowtie/indexes/` (see Prerequisites above) and ensure `bowtie/bowtie-align-s` is executable.

4. **Restore dependencies**:

   ```bash
   dotnet restore
   ```

5. **Run the application**:

   ```bash
   cd DiseaseMutationsApp
   dotnet run
   ```

6. **Access the application**:
   - Application: http://localhost:5000 (or the port shown in the console)

## 📖 Usage Guide

### Application Pages

The application currently has a single page:

1. **gRNA Builder** (`/`): analyze HGVS notations and rsIDs, and generate ranked gRNA candidates

The `/omim-to-rs` page exists in the codebase but is disabled (see "Key Features" above).

### Basic Workflow

1. **Enter input in the text area**:
   - **rsID**: `rs12345` — resolved to one or more HGVS notations
   - **HGVS**: `NC_000017.11:g.7674220C>T` — analyzed directly
   - **Multiple inputs**: separate with commas (e.g., `rs12345, NG_016465.4:g.98765C>T`)

2. **Configure the spacer size and seed region**:
   - Spacer size default: 28 nucleotides
   - Seed region default: positions 10–17 (inclusive, 0-based within the spacer), used for highlighting and for the CSV `Seed Region` column

3. **Click "Fetch Data"**:
   - Each input creates a new top-level tab
   - rsIDs create parent tabs with one HGVS subtab pair per resolved variant — a normal subtab and a complement (`C`) subtab
   - HGVS inputs create a single tab with immediate analysis

4. **Navigate between tabs**:
   - Click tab headers to switch between different inputs
   - For rsID tabs, use the second-level subtabs to view each resolved HGVS variant (normal / complement)

5. **Review results** (for each HGVS variant):
   - **Original Sequence**: reference genome sequence (mutation underlined), with a link to the source accession on NCBI
   - **Mutated Sequence**: sequence with the mutation applied (mutation underlined)
   - **gRNA Spacer Results — Mutated Sequence**: ranked candidates from the mutated sequence
   - **gRNA Spacer Results — Original Sequence**: ranked candidates from the original (wild-type) sequence
   - Both tables are sortable by clicking any column header

6. **Select a gRNA**:
   - Review the quality metrics (GC Score, GC Content, Homopolymers, Alignments, Energy, Score)
   - Click the 🔨 button next to your preferred spacer
   - The complete gRNA (scaffold + spacer) appears at the bottom

7. **Copy the complete gRNA**:
   - Click the "📋 Copy" button to copy the full gRNA sequence to the clipboard

8. **Get the RNA secondary structure** (optional):
   - Click "🧬 Get Structure" to fold the complete gRNA with ViennaRNA and view the dot-bracket structure, energy, and a FORNA visualization link

9. **Download a CSV report**:
   - Per rsID tab: "Download Report"
   - For every rsID tab at once: "Download All Reports" (shown once at least one rsID tab exists)

### Understanding Tab Navigation

- **Main Tabs**: one tab per input (rsID or HGVS)
- **Subtabs** (rsID only): each resolved HGVS variant gets a normal subtab and a complement (`C`) subtab
- **Tab Indicators**:
  - ⏳ Loading data
  - ✓ Data loaded successfully (green)
  - ↕ Complement subtab, loaded successfully
  - ⚠️ Error occurred (red)

### State Persistence

The application keeps your inputs, tabs, and selections while you interact with it in the same browser session. Refreshing the page resets the SignalR circuit and clears this state.

#### Understanding gRNA Metrics

##### GC Score

- **1.0**: GC content strictly between the 40–60% range
- **< 1.0**: GC content outside that range (including exactly 40% or 60%), proportional to the distance from the ideal
- Higher is better for stability and efficiency

##### GC Content

- Raw GC percentage of the spacer sequence, rounded to two decimal places

##### Homopolymer Count

- Number of runs of 4 or more consecutive identical bases (`A`, `C`, `G`, or `U`)
- **Optimal**: 0
- Homopolymers can cause synthesis errors and reduced efficiency

##### Alignments

- Number of near-perfect matches of the raw DNA window in the genome (Bowtie, up to 2 mismatches, up to 6 reported alignments)
- **Optimal**: 1 (unique target)
- High numbers indicate potential off-target effects

##### Energy / RNA Structure Score

- Minimum free energy (MFE, kcal/mol) of the **complete gRNA's** (scaffold + spacer) secondary structure
- Closer to zero (less negative) is preferred — it indicates a less stable, more accessible structure
- Displayed with a FORNA visualization link once you fetch the structure for your selected spacer

##### Score

- Final normalized score from 0 to 1, based on the candidate's rank after sorting by (Alignments, Energy, GC Score, Homopolymer Count) in that priority order
- Candidates with an identical sort key share the same rank

See [BIOLOGICAL_REPORT.md](BIOLOGICAL_REPORT.md) for the full scientific rationale and exact algorithm.

### HGVS Format Examples

```
# Single nucleotide substitution
NC_000017.11:g.7674220C>T

# Deletion
NM_000546.6:c.215_217del

# Insertion
NM_000546.6:c.215_216insA

# Deletion-Insertion
NM_000546.6:c.215_217delinsAG

# Duplication
NM_000546.6:c.215_217dup

# Inversion
NM_000546.6:c.215_217inv

# No change
NM_000546.6:c.215=
```

## 🔬 Technical Details

### Server-Side Processing

The application uses Blazor Server's SignalR-based architecture for real-time communication:

- **Interactive Components**: pages use `@rendermode InteractiveServerRenderMode`
- **SignalR Circuit**: maintains a persistent connection between client and server
- **State Management**: `AppStateService` (scoped per SignalR circuit) preserves state across page navigations
- **Long-Running Operations**: Kestrel is configured with extended timeouts (10 minutes) for bioinformatics processing

### Service Architecture

#### GrnaService

C# wrapper providing access to the F# library:

**Key Methods**:

- `GetBestgRNAFromHgvs(hgvs, window, seedStart, seedEnd, complement, cancellationToken)`: complete workflow from HGVS to ranked gRNA candidates (mutated and original)
- `GetHgvsFromSnp(rsid)`: resolve an rsID to its HGVS notations
- `GetRnaFold(sequence)`: predict RNA secondary structure for an arbitrary sequence
- `GetFornaUrl(sequence, structure)`: build a FORNA visualization URL
- `GetNcbiNuccoreUrl(hgvs)`: build the NCBI Nucleotide URL for an HGVS accession
- `Scaffold`: the constant 36 nt Cas13 scaffold sequence, shared with the F# layer as the single source of truth

`GetRsFromOmim` also exists but is commented out (disabled OMIM feature).

**Return Types**: all methods return C# records converted from F# types for interop

#### AppStateService

Scoped service that maintains state across page navigations:

**Index Page State**:

- `IndexHgvsInput`: current input text
- `IndexGRnaSize`: selected spacer size (default: 28)
- `IndexSeedStart` / `IndexSeedEnd`: selected seed region bounds (defaults: 10 / 17)
- `IndexInputTabs`: list of all tabs and their data
- `IndexActiveTabIndex`: currently selected top-level tab
- `IndexActiveChildTabIndices`: active subtab for each rsID tab

**Benefits**:

- Seamless navigation within the app without data loss
- Preserved tab selections and user inputs
- Event-based notification for state changes

## 🧬 Complete gRNA Structure

The application generates complete gRNA sequences consisting of two parts:

```
[Scaffold Sequence (36 nt)] + [Spacer Sequence (configurable, 20-28 nt typical)]
```

**Scaffold (constant, 36 nt)**: `GAUUUAGACUACCCCAAAAACGAAGGGGACUAAAAC`

- Provides the structural framework for Cas13 binding
- Universal sequence used in most CRISPR applications
- Displayed in red in the UI

**Spacer (variable)**: selected from ranked candidates

- Target-specific sequence, length configurable in the UI (typically 20-28 nucleotides)
- Displayed in blue in the UI
- The reverse complement of the target DNA window, transcribed to RNA (`T`→`U`) — guides Cas13 to the desired genomic location

**Example Complete gRNA** (28 nt spacer):

```
GAUUUAGACUACCCCAAAAACGAAGGGGACUAAAAC AUCGAUCGAUCGAUCGAUCGAUCGAUCG
└──────────────────────────────────┘ └──────────────────────────┘
          Scaffold (36 nt)                 Spacer (28 nt example)
```

## 🔬 Algorithm Details

See [BIOLOGICAL_REPORT.md](BIOLOGICAL_REPORT.md) for the full, section-by-section mapping between the biology and the code, including the substitution special rule. Summary:

1. **Sliding window**: generate every subsequence of the configured spacer length from the mutated (and separately, the original) sequence.
2. **Spacer derivation**: each DNA window is complemented, reversed, and transcribed to RNA (`T`→`U`) to produce the actual gRNA spacer.
3. **Scoring**: GC score (40-60% ideal range), homopolymer count, off-target alignment count (Bowtie, on the raw DNA window), and RNA folding energy (ViennaRNA, on the complete `scaffold + spacer` RNA).
4. **Ranking**: `(Alignments ASC, -Energy DESC, -GCScore DESC, HomopolymerCount ASC)`; ties share a rank; a normalized 0-1 score is derived from the rank.
5. **Substitution special rule**: for substitution HGVS variants, if a candidate's mutation lands at the right position near the 3' end, it is replaced by a single mismatch-engineered variant with `Rank = 1` and `Score = 1.0` — and that single candidate **replaces the entire ranked list** for that variant. This is intentional (see BIOLOGICAL_REPORT.md, section 4.4), not a bug: the substitution-variant tables in the UI may show a single row instead of the full candidate list.
6. **Result presentation**: all candidates are shown (not limited to a top N), grouped and ranked by tied scores, for both the mutated and the original sequence.

## 📁 Project Structure

```
DiseaseMutationsApp/
├── DiseaseMutationsApp/            # Blazor Server Application
│   ├── Pages/
│   │   ├── Index.razor             # Main gRNA Builder page (multi-tab interface)
│   │   ├── Index.razor.cs          # Code-behind for the Index page
│   │   ├── IndexModels.cs          # Data models for the Index page
│   │   └── OmimToRs.razor          # OMIM → RS converter page (disabled)
│   ├── Components/
│   │   ├── HgvsDetailPanel.razor   # Reusable component for a single HGVS variant's results
│   │   ├── GrnaResultsTable.razor  # Sortable gRNA candidate table with highlighting
│   │   └── Routes.razor            # Route configuration
│   ├── Services/
│   │   ├── GrnaService.cs          # C# wrapper for the F# library
│   │   └── AppStateService.cs      # Scoped state management service
│   ├── Shared/
│   │   ├── MainLayout.razor        # Main application layout
│   │   ├── MainLayout.razor.css    # Layout styling
│   │   ├── NavMenu.razor           # Navigation menu component
│   │   └── NavMenu.razor.css       # Navigation styling
│   ├── wwwroot/                    # Static assets
│   │   ├── css/                    # Stylesheets (Bootstrap, app.css, open-iconic)
│   │   ├── js/file-download.js     # Client-side CSV download helper
│   │   ├── favicon.png             # App icon
│   │   └── 404.html                # Error page
│   ├── App.razor                   # App root component
│   ├── _Imports.razor              # Global using statements
│   ├── Program.cs                  # Application entry point
│   └── DiseaseMutationsApp.csproj  # C# project file
│
├── gRNA/                           # F# Core Library
│   ├── Main.fs                     # Public API for C# interop
│   ├── HGVS.fs                     # HGVS notation parser
│   ├── Sequence.fs                 # DNA sequence manipulation
│   ├── SequenceRepository.fs       # NCBI API integration
│   ├── SpacerFinder.fs             # gRNA generation and scoring
│   ├── BowtieWrapper.fs            # Bowtie alignment wrapper
│   ├── BowtieService.fs            # Semaphore-serialized Bowtie service
│   ├── RNAFoldWrapper.fs           # ViennaRNA integration
│   ├── SNP.fs                      # SNP/rsID database integration
│   ├── Omim.fs                     # OMIM database integration (excluded from build)
│   ├── LevenshteinDistance.fs      # Sequence similarity calculations (excluded from build)
│   ├── LibraryTesting.fsx          # F# interactive testing script
│   └── gRNA.fsproj                 # F# project file
│
├── DiseaseMutationsAppTests/       # Unit tests (NUnit)
│   ├── HGVSTests.cs                # HGVS parser tests
│   ├── SequenceTests.cs            # Sequence manipulation and complement tests
│   ├── SpacerFinderTests.cs        # Scoring, seed region, sliding window, sort order
│   ├── SpacerFinderHighlightTests.cs  # Mutation highlighting and the substitution special rule
│   ├── MainTests.cs                # Mutation span clamping near sequence boundaries
│   ├── GrnaServiceTests.cs         # Pure GrnaService helpers (URLs, scaffold)
│   ├── GlobalUsings.cs             # Test project global usings
│   └── DiseaseMutationsAppTests.csproj
│
├── bowtie/                         # Bowtie aligner
│   └── bowtie-align-s              # Bowtie executable (Linux); indexes are provisioned by Dockerfile.bowtie-base, not committed to the repo
│
├── docker-compose.yml              # Docker orchestration for the app image
├── Dockerfile                      # Application container image
├── Dockerfile.bowtie-base          # Base image with the GRCh38 Bowtie2 indexes
├── start.sh                        # Install/run helper for the Docker workflow
├── start.bat                       # Windows launcher for start.sh (via Git Bash)
├── DiseaseMutationsApp.sln         # Visual Studio solution
├── BIOLOGICAL_REPORT.md            # Biology-to-code mapping, written for a biology audience
└── README.md                       # This file
```

## 🧪 Testing

Unit tests only — no integration tests are included, since the bioinformatics external dependencies (Bowtie, ViennaRNA/Python, NCBI/dbSNP APIs) are not available in a plain build/test environment. All tests exercise pure functions.

### Run Unit Tests

```bash
# Run all tests
dotnet test

# Run with detailed output
dotnet test --verbosity normal

# Run a specific test project
cd DiseaseMutationsAppTests
dotnet test
```

### Test Coverage

- **`HGVSTests.cs`**: HGVS parsing for every supported mutation type, genomic vs. coding notations, mutation length, and malformed-input error handling
- **`SequenceTests.cs`**: mutation application for every supported mutation type, padding/context clamping at sequence boundaries, and DNA complementing
- **`SpacerFinderTests.cs`**: sliding window generation, GC content/score, homopolymer counting, seed-region extraction (including the short-spacer clamping fix), default `gRNAResult` values, and the sort-order priority (alignments → energy → GC score → homopolymers)
- **`SpacerFinderHighlightTests.cs`**: mutation-highlight span computation for substitutions/deletions/insertions, and the substitution special rule — including a test that locks in its intentional "replaces the whole candidate list with a single result" behavior
- **`MainTests.cs`**: mutation-span clamping when a variant sits near the start or end of the fetched sequence
- **`GrnaServiceTests.cs`**: the dependency-free `GrnaService` helpers (NCBI URL building, FORNA URL building, scaffold constant)

Not covered by unit tests (require Bowtie, ViennaRNA/Python, or live NCBI/dbSNP network access): `SpacerFinder.getOrderedgRna`, `SequenceRepository`, `SNP.getHgvsNotationsAsync`, `BowtieWrapper`, `RNAFoldWrapper`.

## 🛠️ Configuration

### Application Configuration

The application is configured in `DiseaseMutationsApp/Program.cs`:

```csharp
// Service registration
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddScoped<AppStateService>();
builder.Services.AddSingleton<gRNA.Services.BowtieService>();
builder.Services.AddScoped<GrnaService>();

// Configure Kestrel for long-running operations
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10);
    serverOptions.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(10);
});
```

### Launch Settings (`Properties/launchSettings.json`)

```json
{
  "profiles": {
    "http": {
      "commandName": "Project",
      "launchBrowser": true,
      "applicationUrl": "http://localhost:5000"
    },
    "https": {
      "commandName": "Project",
      "launchBrowser": true,
      "applicationUrl": "https://localhost:5001;http://localhost:5000"
    }
  }
}
```

### Docker Resource Limits

Configure in `docker-compose.yml`:

```yaml
deploy:
  resources:
    limits:
      cpus: "1.5"
      memory: 2G
```

Adjust these limits based on your system capabilities and workload requirements.

## 🐛 Troubleshooting

### Common Issues

#### "No Bowtie index base found in bowtie/indexes"

**Solution**: ensure Bowtie2 index files (`.bt2` or `.bt2l`) are present under `bowtie/indexes/`. When running via Docker, these come from the `disease-mutations-bowtie:latest` base image — rebuild it with `./start.sh --rebuild-bowtie` if it's missing or stale. For local (non-Docker) development, you must provide them manually.

#### "SignalR circuit disconnected"

**Solution**:

- Check that the server is running and accessible
- Verify network connectivity
- Check the browser console for connection errors
- Ensure the Kestrel timeout settings are sufficient for your workload

#### "HGVS parsing failed"

**Solution**:

- Verify the HGVS format is correct
- Check that the accession version exists in the NCBI database
- Review the supported mutation types in `HGVS.fs`

#### "No gRNA candidates found"

**Solution**:

- Increase the spacer size parameter
- Check that the sequence length is sufficient for the requested spacer size
- Verify the mutation region is valid

#### "RNA folding failed"

**Solution**:

- Ensure ViennaRNA is installed: `pip install viennarna`
- Check that Python 3 (or `python`) is accessible from the application's process
- Verify the sequence is a valid RNA sequence

#### Docker build fails

**Solution**:

- Ensure Docker has sufficient memory allocated (4GB+)
- Check that all required files are in the build context
- Ensure outbound network access to download the Bowtie2 indexes during the base-image build
- Rebuild from scratch if the cache was corrupted: `docker compose build --no-cache app`
- Use the installer rebuild path if needed: `./start.sh --rebuild` (or `--rebuild-bowtie` for the base image)

#### Navigation state lost

**Solution**:

- This is expected if you refresh the page (the SignalR circuit resets)
- Use the navigation menu instead of the browser's back/forward buttons
- `AppStateService` maintains state only within an active session

## 🔒 Security Considerations

### Production Deployment

1. **HTTPS Configuration**: enable HTTPS in production (configured by default in `launchSettings.json` for local development)
2. **SignalR Security**: implement authentication if deploying publicly
3. **Input Validation**: HGVS inputs are validated, but consider additional sanitization for public deployments
4. **Resource Limits**: configure appropriate CPU/memory limits based on expected load
5. **API Rate Limiting**: consider implementing rate limiting for NCBI API calls to prevent abuse
6. **Network Security**: restrict network access if running locally or in a controlled environment

### Local Deployment Considerations

This application is designed for **local deployment** and includes features suitable for a trusted environment:

- CORS is configured to allow any origin (for development convenience)
- No authentication is required by default
- SignalR circuits are not encrypted by default (use HTTPS in production)

For public or multi-user deployments, implement:

- User authentication and authorization
- HTTPS with valid certificates
- SignalR connection authentication
- API rate limiting
- Input sanitization and validation

## 🚧 Future Enhancements

Potential improvements and features:

- [ ] Re-enable OMIM → rsID conversion once a captcha-resistant approach is available
- [ ] gRNA assembly system support
- [ ] Batch processing of multiple mutations from file upload
- [ ] Additional genome assemblies
- [ ] Integration with ClinVar database for pathogenicity information
- [ ] Visualization of gRNA binding sites in genomic context
- [ ] Authentication and user accounts for saved designs
- [ ] Save and share gRNA designs with persistent storage
- [ ] Advanced filtering and sorting options for gRNA candidates
- [ ] Integration with other variant databases

## 🤝 Contributing

Contributions are welcome! Please follow these guidelines:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

### Coding Standards

- **C#**: follow Microsoft's C# coding conventions
- **F#**: follow the F# style guide
- **Tests**: add unit tests for new features (no integration tests — see the Testing section)
- **Documentation**: update `README.md` and, for biology/algorithm changes, `BIOLOGICAL_REPORT.md`

## 👥 Authors

- Javier Torralbo - Initial work

## 🙏 Acknowledgments

- **Bowtie**: fast short-read aligner by Langmead et al.
- **NCBI**: for providing genomic sequence data
- **HGVS Nomenclature**: standard mutation notation system
- **CRISPR Community**: for advancing genome editing research

## 📚 References

1. **Bowtie**: Langmead, B., Trapnell, C., Pop, M., & Salzberg, S. L. (2009). Ultrafast and memory-efficient alignment of short DNA sequences to the human genome. Genome Biology, 10(3), Article R25. https://doi.org/10.1186/gb-2009-10-3-r25
2. **HGVS Nomenclature**: Human Genome Variation Society. HGVS nomenclature. http://varnomen.hgvs.org/
3. **GRCh38 Reference**: Genome Reference Consortium. (2013). Human Build 38 (GRCh38) [Data set]. National Center for Biotechnology Information. https://www.ncbi.nlm.nih.gov/grc/human
4. **CRISPR Design Guidelines**:
   - Doench, J. G., Fusi, N., Sullender, M., Hegde, M., Vaimberg, E. W., Berman, K. F., DeWeirdt, B., Baranzini, S. E., Smith, Z. D., Warrior, T. H., Leary, S. J., Mikkelsen, T. S., Abbas, N., & Root, D. E. (2016). Optimized sgRNA design to maximize activity and minimize off-target effects of CRISPR-Cas9. Nature Biotechnology, 34(2), 184–191. https://doi.org/10.1038/nbt.3437
   - Bryson, J. W. (2025). Array Assembler Provides Greatly Simplified crRNA Array Design for CRISPR Cas12 and Cas13 Variants. ACS Synthetic Biology. https://doi.org/10.1021/acssynbio.5c00100
   - Gruber, A. R., Lorenz, R., Bernhart, S. H., Neuböck, R., & Hofacker, I. L. (2008). The Vienna RNA websuite. Nucleic Acids Research, 36(Web Server issue). https://doi.org/10.1093/nar/gkn188
   - Karimi, M., Ghorbani, A., Niazi, A., Rostami, M., & Tahmasebi, A. (2025). Integrating AI and CRISPR Cas13a for rapid detection of tomato brown rugose fruit virus. Scientific Reports, 15(1). https://doi.org/10.1038/s41598-025-11405-z
   - Lorenz, R., Bernhart, S. H., Höner Zu Siederdissen, C., Tafer, H., Flamm, C., Stadler, P. F., & Hofacker, I. L. (2011). ViennaRNA Package 2.0. http://www.tbi.univie.ac.at/RNA
   - Mathews, D. H., Disney, M. D., Childs, J. L., Schroeder, S. J., Zuker, M., & Turner, D. H. (2004). Incorporating chemical modification constraints into a dynamic programming algorithm for prediction of RNA secondary structure. PNAS, 101. www.pnas.org/cgi/doi/10.1073/pnas.0401799101

## 🔄 Migration Notes

### From Blazor WebAssembly to Blazor Server

The application was migrated from a client-side Blazor WebAssembly architecture to Blazor Server for the following reasons:

**Why the migration?**

- **Simplified Deployment**: single container instead of separate frontend/backend services
- **Better Performance**: no need to download the .NET runtime to the browser
- **Direct Library Access**: the F# library is called directly without HTTP overhead
- **Improved State Management**: SignalR circuits provide natural state persistence
- **Local Deployment Focus**: designed for local/institutional deployment rather than public hosting

**What Changed:**

- ❌ Removed: separate backend API service (Minimal API)
- ❌ Removed: Refit HTTP client library
- ❌ Removed: CORS configuration for cross-origin requests
- ✅ Added: Blazor Server with Interactive Server render mode
- ✅ Added: direct C# to F# library integration via `GrnaService`
- ✅ Added: `AppStateService` for cross-page state management
- ✅ Improved: real-time updates via SignalR (no polling needed)

**Trade-offs:**

- Requires a persistent server connection (SignalR circuit)
- Not suitable for static hosting (GitHub Pages, CDN)
- State resets on page refresh (can be mitigated with browser storage if needed)

## 📞 Support

For issues, questions, or suggestions:

- Open an issue on GitHub
- Contact: javiertorralbocortes@gmail.com

---

**Last Updated**: August 2026
