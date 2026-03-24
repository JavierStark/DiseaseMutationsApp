# Disease Mutations App

A web application for analyzing genetic mutations and generating guide RNA (gRNA) sequences for CRISPR applications. This tool allows researchers to input disease-related mutations in HGVS format, rsIDs, or OMIM codes, and automatically generates optimized gRNA spacers for genome editing.

## 🧬 Overview

The Disease Mutations App is designed to streamline the process of designing CRISPR guide RNAs for therapeutic applications. It takes Human Genome Variation Society (HGVS) formatted mutation data, SNP reference IDs (rsIDs), or OMIM disease identifiers, retrieves the relevant genomic sequences, and identifies optimal gRNA spacer sequences based on multiple quality metrics.

### Key Features

- **Multiple Input Types**:
  - **HGVS Format**: Standard genome variation notation (e.g., `NC_000017.11:g.7674220C>T`)
  - **rsID Support**: SNP reference IDs (e.g., `rs12345`)
  - **OMIM Integration**: Disease-to-variant lookup (e.g., `605543`)
- **Automated Sequence Retrieval**: Fetch genomic sequences from NCBI databases
- **gRNA Optimization**: Generate and rank gRNA candidates based on:
  - GC content (optimal range: 40-60%)
  - Homopolymer runs (penalizes sequences with 4+ consecutive identical bases)
  - Off-target alignment analysis using Bowtie
  - RNA secondary structure prediction using ViennaRNA
- **Visual Comparison**: Side-by-side display of original and mutated sequences with highlighted mutations
- **Interactive Tabbed Interface**: Analyze multiple variants simultaneously with dynamic tab management
- **State Persistence**: Navigate between pages without losing your work
- **Real-time Analysis**: Server-side processing with real-time SignalR updates

## 🏗️ Architecture

The application uses a modern **Blazor Server** architecture with a monolithic design optimized for local deployment:

### 1. Blazor Server Application

- **Technology**: ASP.NET Core 9.0 with Blazor Server
- **Language**: C# 9.0
- **Render Mode**: Interactive Server (SignalR-based real-time communication)
- **UI Framework**: Bootstrap 5 with BlazorBootstrap components
- **Port**: 5000 (HTTP), 5001 (HTTPS in development)

### 2. gRNA Library (F# Module)

- **Language**: F# 8.0
- **Purpose**: Core bioinformatics logic and external tool integration
- **Key Modules**:
  - `Main.fs`: Public API entry points for C# interop
  - `SpacerFinder.fs`: gRNA candidate generation and ranking
  - `BowtieWrapper.fs`: Off-target alignment analysis
  - `RNAFoldWrapper.fs`: RNA secondary structure prediction (ViennaRNA)
  - `HGVS.fs`: HGVS notation parser
  - `Sequence.fs`: DNA sequence manipulation
  - `SequenceRepository.fs`: NCBI sequence retrieval
  - `SNP.fs`: SNP database integration (dbSNP)
  - `Omim.fs`: OMIM disease database integration
  - `LevenshteinDistance.fs`: Sequence similarity calculations

### 3. Services Layer

- **GrnaService**: Direct C# wrapper for F# library functions
- **AppStateService**: Scoped state management for cross-page navigation and data persistence

## 🐳 Docker Architecture

The application uses a multi-stage Docker build strategy for efficient deployment:

### Single Image Build (`Dockerfile`)

Multi-stage build process:

1. **Indices stage**: Downloads and unzips GRCh38 Bowtie indexes
2. **Build stage**: Restores/publishes C# + F# app artifacts
3. **Final stage**: Installs Python/ViennaRNA and assembles runtime image

The Dockerfile uses BuildKit cache mounts for apt, NuGet, pip, and index download cache to avoid slow rebuilds.

**Build Flow**:

```
Dockerfile (indices + build + runtime stages)
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
│  │   - HTTP/HTTPS Server       │   │
│  │   - SignalR Hub             │   │
│  │   - Razor Components        │   │
│  │   - Static Files            │   │
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
│  │   - OMIM/SNP integration    │   │
│  └─────────────────────────────┘   │
│                                      │
│  External Dependencies:              │
│  - Bowtie (alignment)               │
│  - ViennaRNA (structure prediction) │
│  - GRCh38 indexes (~4GB)            │
└─────────────────────────────────────┘
```

## 📋 Prerequisites

### Required Software

- **.NET 9.0 SDK** or later
- **Docker** and **Docker Compose** (for containerized deployment)
- **Bowtie** alignment tool (included in the repository)
- **Python 3** with **ViennaRNA** library (for RNA folding functionality)
  - Install: `pip install viennarna` or `pip3 install viennarna`

### Required Data

- **GRCh38 Reference Genome** Bowtie indexes (included in `bowtie/indexes/`)
  - `GCA_000001405.15_GRCh38_no_alt_analysis_set.*.ebwt`

### System Requirements

- **CPU**: 2+ cores (recommended for optimal performance)
- **RAM**: 2GB minimum (4GB recommended)
- **Storage**: ~5GB for reference genome indexes
- **Network**: Internet connection required for NCBI API calls (sequence retrieval, SNP lookup, OMIM queries)

## 🚀 Getting Started

### Option 1: Docker Deployment with Installer Script (Recommended)

1. **Clone the repository**:

   ```bash
   git clone https://github.com/yourusername/DiseaseMutationsApp.git
   cd DiseaseMutationsApp
   ```

2. **Run the installer script**:

   ```bash
   chmod +x DiseaseMutationApp.sh
   ./DiseaseMutationApp.sh
   ```

   This script validates Docker/Docker Compose, builds the app image when needed, and starts the container.

3. **Force rebuild when needed**:

   ```bash
   ./DiseaseMutationApp.sh --rebuild
   ```

4. **Access the application**:
   - Application: http://localhost:5000

#### Useful script-driven operations

```bash
# Start (without rebuilding when image already exists)
./DiseaseMutationApp.sh

# Rebuild image and start
./DiseaseMutationApp.sh --rebuild

# Show script usage
./DiseaseMutationApp.sh --help
```

### Option 2: Manual Docker Compose

1. **Clone the repository**:

   ```bash
   git clone https://github.com/yourusername/DiseaseMutationsApp.git
   cd DiseaseMutationsApp
   ```

2. **Build and run with Docker Compose**:

   ```bash
   docker compose up -d
   ```

3. **Access the application**:
   - Application: http://localhost:5000

#### Quick Rebuild During Development

You can rebuild quickly using cached Docker layers and BuildKit cache mounts:

```bash
# Rebuild and restart the application
./DiseaseMutationApp.sh --rebuild

# Manual alternative
docker compose up -d --build

# View logs
docker compose logs -f app
```

The single Dockerfile keeps build speed high by caching dependency and data download layers.

### Option 2: Local Development

1. **Clone the repository**:

   ```bash
   git clone https://github.com/yourusername/DiseaseMutationsApp.git
   cd DiseaseMutationsApp
   ```

2. **Install ViennaRNA** (required for RNA folding):

   ```bash
   pip install viennarna
   # or
   pip3 install viennarna
   ```

3. **Restore dependencies**:

   ```bash
   dotnet restore
   ```

4. **Run the application**:

   ```bash
   cd DiseaseMutationsApp
   dotnet run
   ```

5. **Access the application**:
   - Application: http://localhost:5000 (or the port shown in console)
   - HTTPS (development): https://localhost:5001

## 📖 Usage Guide

### Application Pages

The application consists of two main pages:

1. **gRNA Builder** (`/`): Main page for analyzing HGVS notations and rsIDs, generating gRNA candidates
2. **OMIM to RS Converter** (`/omim-to-rs`): Convert OMIM disease codes to rsIDs

### Basic Workflow

You can start from an HGVS notation directly, an rsID, or an OMIM identifier. The app guides you through resolving and selecting the variant you want to design gRNAs for.

#### Starting from OMIM

1. **Navigate to OMIM → RS page** (from navigation menu)
2. **Enter OMIM code** (e.g., `605543`)
3. **Click "Fetch RS IDs"** to retrieve associated rsIDs
4. **Select desired rsIDs** using checkboxes
5. **Click "Open selected in gRNA Builder"** to transfer them to the main workflow

#### Starting from rsID or HGVS

1. **Navigate to gRNA Builder** (home page)
2. **Enter input in the text area**:
   - **rsID**: `rs12345` - will be resolved to HGVS notations
   - **HGVS**: `NC_000017.11:g.7674220C>T` - direct analysis
   - **Multiple inputs**: Separate with commas (e.g., `rs12345, NG_016465.4:g.98765C>T`)

3. **Configure gRNA size**:
   - Default: 28 nucleotides
   - Adjust based on your CRISPR system requirements (typically 20-28)

4. **Click "Fetch Data"**:
   - Each input creates a new tab
   - rsIDs create parent tabs with HGVS subtabs (one for each variant)
   - HGVS inputs create single tabs with immediate analysis

5. **Navigate between tabs**:
   - Click tab headers to switch between different inputs
   - For rsID tabs, use subtabs to view different HGVS variants

6. **Review results** (for each HGVS variant):
   - **Original Sequence**: Reference genome sequence (mutation highlighted in bold)
   - **Mutated Sequence**: Sequence with the mutation applied (mutation highlighted in bold)
   - **gRNA Results Table**: Top ranked candidate spacers

7. **Select a gRNA**:
   - Review the quality metrics (GC Score, Homopolymers, Alignments)
   - Click the 🔨 button next to your preferred spacer
   - The complete gRNA (scaffold + spacer) appears at the bottom

8. **Copy complete gRNA**:
   - Click the "📋 Copy" button to copy the full gRNA sequence to clipboard
   - Use this sequence for RNA synthesis or further analysis

### Understanding Tab Navigation

The application uses a sophisticated tabbed interface:

- **Main Tabs**: One tab per input (rsID or HGVS)
- **Subtabs** (rsID only): When an rsID resolves to multiple HGVS variants, each variant gets its own subtab
- **Tab Indicators**:
  - ⏳ Loading data
  - ✓ Data loaded successfully (green)
  - ⚠️ Error occurred (red)

### State Persistence

The application maintains your state as you navigate between pages:

- Switch between "gRNA Builder" and "OMIM → RS" pages without losing data
- All tabs, selections, and inputs are preserved
- Use the navigation menu to move between workflows

#### Understanding gRNA Metrics

##### GC Score (0.0 - 1.0)

- **Optimal**: 1.0 (GC content between 40-60%)
- **Suboptimal**: <1.0 (GC content outside ideal range)
- Higher is better for stability and efficiency

##### Homopolymer Count

- Number of homopolymer runs (4+ consecutive identical bases)
- **Optimal**: 0
- Homopolymers can cause synthesis errors and reduced efficiency

##### Alignments

- Number of near-perfect matches in the genome
- **Optimal**: Low numbers (1-2)
- High numbers indicate potential off-target effects

##### RNA Structure Score

- Minimum free energy (MFE) of the gRNA secondary structure
- More negative values indicate more stable structures
- Displayed with FORNA visualization link

### HGVS Format Examples

```
# Single nucleotide substitution
NC_000017.11:g.7674220C>T

# Deletion
NM_000546.6:c.215_217del

# Insertion
NM_000546.6:c.215_217insAGC

# Deletion-Insertion (delins)
NM_000546.6:c.215_217delinsAGC

# Range mutation
NC_000001.11:g.12345_12350del
```

## 🔬 Technical Details

### Server-Side Processing

The application uses Blazor Server's SignalR-based architecture for real-time communication:

- **Interactive Components**: All pages use `@rendermode InteractiveServerRenderMode`
- **SignalR Circuit**: Maintains persistent connection between client and server
- **State Management**: `AppStateService` (scoped per circuit) preserves state across page navigations
- **Long-Running Operations**: Kestrel configured with extended timeouts (10 minutes) for bioinformatics processing

### Service Architecture

#### GrnaService

Direct C# wrapper providing access to F# library functionality:

**Key Methods**:

- `GetBestgRNAFromHgvs(hgvs, window)`: Complete workflow from HGVS to gRNA candidates
- `GetHgvsFromSnp(rsid)`: Resolve rsID to HGVS notations
- `GetRsFromOmim(omim)`: Retrieve rsIDs associated with OMIM code
- `GetRnaFold(sequence)`: Predict RNA secondary structure
- `GetFornaUrl(sequence, structure)`: Generate FORNA visualization URL
- `GetAlignments(sequence, mismatches, threads)`: Get Bowtie alignment results

**Return Types**: All methods return C# records converted from F# types for seamless interop

#### AppStateService

Scoped service that maintains state across page navigations:

**Index Page State**:

- `IndexHgvsInput`: Current input text
- `IndexGRnaSize`: Selected gRNA size (default: 28)
- `IndexInputTabs`: List of all tabs and their data
- `IndexActiveTabIndex`: Currently selected tab
- `IndexActiveChildTabIndices`: Active subtab for each rsID tab

**OmimToRs Page State**:

- `OmimCode`: Entered OMIM identifier
- `OmimRsList`: Retrieved rsIDs
- `OmimSelectedRs`: User-selected rsIDs for batch processing
- `OmimErrorMessage`: Error information if fetch fails

**Benefits**:

- Seamless navigation between pages without data loss
- Preserved tab selections and user inputs
- Event-based notification for state changes

## 🧬 Complete gRNA Structure

The application generates complete gRNA sequences consisting of two parts:

```
[Scaffold Sequence (37nt)] + [Spacer Sequence (20-28nt)]
```

**Scaffold (constant)**: `GAUUUAGACUACCCCAAAAACGAAGGGGACUAAAAC`

- Provides structural framework for Cas9 binding
- Universal sequence used in most CRISPR applications
- Displayed in red in the UI

**Spacer (variable)**: Selected from candidates

- Target-specific sequence (20-28 nucleotides depending on configuration)
- Displayed in blue in the UI
- Guides Cas9 to the desired genomic location

**Example Complete gRNA**:

```
GAUUUAGACUACCCCAAAAACGAAGGGGACUAAAACAUCGAUCGAUCGAUCGAUCGAUCG
└──────────────────────────────┘└───────────────────────────────────────┘
         Scaffold (37nt)              Spacer (28nt example)
```

## 🔬 Algorithm Details

### gRNA Candidate Generation

1. **Sliding Window Analysis**:
   - Generate all possible subsequences of specified length (20-28nt)
   - Extract from the mutated sequence region
   - Each subsequence becomes a potential spacer

2. **Quality Metrics Calculation**:

   **GC Content Score** (0.0 - 1.0):

   ```
   If 40% ≤ GC% ≤ 60%: GC_Score = 1.0
   If GC% < 40%:        GC_Score = GC% / 40%
   If GC% > 60%:        GC_Score = (100% - GC%) / 40%
   ```

   **Homopolymer Count**:
   - Counts runs of 4 or more consecutive identical bases (e.g., `AAAA`, `GGGG`)
   - Uses regex pattern: `(A{4,}|C{4,}|G{4,}|T{4,})`

   **Off-Target Alignments**:
   - Uses Bowtie aligner against GRCh38 reference genome
   - Allows up to 2 mismatches
   - Reports number of near-perfect genomic matches
   - Runs with 2 threads for parallel processing

   **RNA Secondary Structure**:
   - Complete gRNA (spacer + scaffold) is analyzed with ViennaRNA
   - Calculates minimum free energy (MFE) in kcal/mol
   - More negative energy = more stable structure

3. **Ranking Algorithm**:

   ```
   Sort by: (Alignments ASC, -RNA_Energy DESC, -GC_Score DESC, Homopolymers ASC)
   ```

   **Priority order**:
   1. **Fewest off-target alignments** (most specific)
   2. **Most stable RNA structure** (lowest/most negative energy)
   3. **Highest GC score** (optimal GC content)
   4. **Fewest homopolymers** (better synthesis quality)

4. **Result Presentation**:
   - Candidates are grouped by identical scores
   - Each group receives a rank (1, 2, 3, etc.)
   - All candidates are presented (not limited to top 5)
   - User selects preferred candidate based on metrics

## 📁 Project Structure

```
DiseaseMutationsApp/
├── DiseaseMutationsApp/         # Blazor Server Application
│   ├── Pages/
│   │   ├── Index.razor          # Main gRNA Builder page (multi-tab interface)
│   │   ├── Index.razor.cs       # Code-behind for Index page
│   │   ├── IndexModels.cs       # Data models for Index page
│   │   └── OmimToRs.razor       # OMIM → RS converter page
│   ├── Components/
│   │   ├── HgvsDetailPanel.razor    # Reusable component for HGVS variant display
│   │   └── Routes.razor         # Route configuration
│   ├── Services/
│   │   ├── GrnaService.cs       # C# wrapper for F# library
│   │   └── AppStateService.cs   # Scoped state management service
│   ├── Shared/
│   │   ├── MainLayout.razor     # Main application layout
│   │   ├── MainLayout.razor.css # Layout styling
│   │   ├── NavMenu.razor        # Navigation menu component
│   │   └── NavMenu.razor.css    # Navigation styling
│   ├── wwwroot/                 # Static assets
│   │   ├── css/                 # Stylesheets
│   │   ├── favicon.png          # App icon
│   │   └── 404.html             # Error page
│   ├── App.razor                # App root component
│   ├── _Imports.razor           # Global using statements
│   ├── Program.cs               # Application entry point
│   └── DiseaseMutationsApp.csproj # C# project file
│
├── gRNA/                        # F# Core Library
│   ├── Main.fs                  # Public API for C# interop
│   ├── HGVS.fs                  # HGVS notation parser
│   ├── Sequence.fs              # DNA sequence manipulation
│   ├── SequenceRepository.fs    # NCBI API integration
│   ├── SpacerFinder.fs          # gRNA generation and scoring
│   ├── BowtieWrapper.fs         # Bowtie alignment wrapper
│   ├── RNAFoldWrapper.fs        # ViennaRNA integration
│   ├── SNP.fs                   # SNP/rsID database integration
│   ├── Omim.fs                  # OMIM database integration
│   ├── LevenshteinDistance.fs   # Sequence similarity calculations
│   ├── LibraryTesting.fsx       # F# interactive testing script
│   └── gRNA.fsproj              # F# project file
│
├── DiseaseMutationsAppTests/    # Unit tests
│   ├── HGVSTests.cs             # HGVS parser tests
│   ├── SequenceTests.cs         # Sequence manipulation tests
│   ├── GlobalUsings.cs          # Test project global usings
│   └── DiseaseMutationsAppTests.csproj
│
├── bowtie/                      # Bowtie aligner
│   ├── bowtie-align-s           # Bowtie executable (Linux)
│   └── indexes/                 # GRCh38 reference indexes
│       ├── GCA_000001405.15_GRCh38_no_alt_analysis_set.*.ebwt
│       └── ...
│
├── docker-compose.yml           # Docker orchestration
├── Dockerfile                   # Application container image
├── DiseaseMutationApp.sh        # Install/run helper for Docker workflow
├── DiseaseMutationsApp.sln      # Visual Studio solution
└── README.md                    # This file
```

## 🧪 Testing

### Run Unit Tests

```bash
# Run all tests
dotnet test

# Run with detailed output
dotnet test --verbosity normal

# Run specific test project
cd DiseaseMutationsAppTests
dotnet test
```

### Test Coverage

- **HGVS Parser Tests**: Validates parsing of various HGVS notation formats
- **Sequence Tests**: Tests mutation application and sequence manipulation
- **Integration Tests**: (Add your own as needed)

## 🛠️ Configuration

### Application Configuration

The application is configured in `DiseaseMutationsApp/Program.cs`:

```csharp
// Service registration
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddScoped<AppStateService>();
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
      cpus: "3.0"
      memory: 2G
    reservations:
      cpus: "1.0"
      memory: 1G
```

Adjust these limits based on your system capabilities and workload requirements.

## 🐛 Troubleshooting

### Common Issues

#### "Bowtie indexes not found"

**Solution**: Ensure Bowtie index files are in `bowtie/indexes/` directory with the correct naming convention.

#### "SignalR circuit disconnected"

**Solution**:

- Check that the server is running and accessible
- Verify network connectivity
- Check browser console for connection errors
- Ensure Kestrel timeout settings are sufficient for your workload

#### "HGVS parsing failed"

**Solution**:

- Verify HGVS format is correct
- Check that accession version exists in NCBI database
- Review supported mutation types in HGVS.fs

#### "No gRNA candidates found"

**Solution**:

- Increase gRNA size parameter
- Check that sequence length is sufficient
- Verify mutation region is valid

#### "RNA folding failed"

**Solution**:

- Ensure ViennaRNA is installed: `pip install viennarna`
- Check that Python 3 is accessible from the application
- Verify sequence is valid RNA format

#### Docker build fails

**Solution**:

- Ensure Docker has sufficient memory allocated (4GB+)
- Check that all required files are in the build context
- Ensure outbound network access to download Bowtie indexes during build
- Rebuild from scratch if cache was corrupted: `docker compose build --no-cache app`
- Use installer rebuild path if needed: `./DiseaseMutationApp.sh --rebuild`

#### Navigation state lost

**Solution**:

- This is expected if you refresh the page (SignalR circuit resets)
- Use the navigation menu instead of browser back/forward buttons
- AppStateService maintains state only within an active session

## 🔒 Security Considerations

### Production Deployment

1. **HTTPS Configuration**: Enable HTTPS in production (configured by default in launchSettings.json)
2. **SignalR Security**: Implement authentication if deploying publicly
3. **Input Validation**: HGVS inputs are validated, but consider additional sanitization for public deployments
4. **Resource Limits**: Configure appropriate CPU/memory limits based on expected load
5. **API Rate Limiting**: Consider implementing rate limiting for NCBI API calls to prevent abuse
6. **Network Security**: Restrict network access if running locally or in a controlled environment

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

- [ ] Support for PAM site analysis and filtering
- [ ] Export results to CSV/JSON
- [ ] Batch processing of multiple mutations from file upload
- [ ] Additional genome assemblies (GRCh37, T2T-CHM13)
- [ ] Prime editing gRNA design
- [ ] Integration with ClinVar database for pathogenicity information
- [ ] Visualization of gRNA binding sites in genomic context
- [ ] Machine learning-based efficiency prediction
- [ ] Authentication and user accounts for saved designs
- [ ] Save and share gRNA designs with persistent storage
- [ ] Advanced filtering and sorting options for gRNA candidates
- [ ] Integration with other variant databases (ClinGen, gnomAD)

## 🤝 Contributing

Contributions are welcome! Please follow these guidelines:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

### Coding Standards

- **C#**: Follow Microsoft's C# coding conventions
- **F#**: Follow F# style guide
- **Tests**: Add unit tests for new features
- **Documentation**: Update README for significant changes

## 📄 License

This project is licensed under the MIT License - see the LICENSE file for details.

## 👥 Authors

- Javier Torralbo - Initial work

## 🙏 Acknowledgments

- **Bowtie**: Fast short-read aligner by Langmead et al.
- **NCBI**: For providing genomic sequence data
- **HGVS Nomenclature**: Standard mutation notation system
- **CRISPR Community**: For advancing genome editing research

## 📚 References

1. **Bowtie**: Langmead B, et al. (2009) Ultrafast and memory-efficient alignment of short DNA sequences. Genome Biology.
2. **HGVS Nomenclature**: http://varnomen.hgvs.org/
3. **GRCh38 Reference**: Genome Reference Consortium Human Build 38
4. **CRISPR Design Guidelines**: Doench JG, et al. (2016) Optimized sgRNA design. Nature Biotechnology.

## 🔄 Migration Notes

### From Blazor WebAssembly to Blazor Server (v2.0.0)

The application was migrated from a client-side Blazor WebAssembly architecture to Blazor Server for the following reasons:

**Why the migration?**

- **Simplified Deployment**: Single container instead of separate frontend/backend services
- **Better Performance**: No need to download .NET runtime to the browser
- **Direct Library Access**: F# library can be called directly without HTTP overhead
- **Improved State Management**: SignalR circuits provide natural state persistence
- **Local Deployment Focus**: Designed for local/institutional deployment rather than public hosting

**What Changed:**

- ❌ Removed: Separate backend API service (Minimal API)
- ❌ Removed: Refit HTTP client library
- ❌ Removed: CORS configuration for cross-origin requests
- ✅ Added: Blazor Server with Interactive Server render mode
- ✅ Added: Direct C# to F# library integration via `GrnaService`
- ✅ Added: `AppStateService` for cross-page state management
- ✅ Improved: Real-time updates via SignalR (no polling needed)

**Benefits:**

- Faster initial load times
- Reduced complexity (one process instead of two)
- Better resource utilization
- Simplified Docker deployment
- Direct access to F# library without serialization overhead

**Trade-offs:**

- Requires persistent server connection (SignalR circuit)
- Not suitable for static hosting (GitHub Pages, CDN)
- State resets on page refresh (can be mitigated with browser storage if needed)

## 📞 Support

For issues, questions, or suggestions:

- Open an issue on GitHub
- Contact: javiertorralbocortes@gmail.com

---

**Last Updated**: February 2026  
**Version**: 2.0.0  
**Architecture**: Blazor Server (migrated from WebAssembly)
