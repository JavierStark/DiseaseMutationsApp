# Disease Mutations App

A web application for analyzing genetic mutations and generating guide RNA (gRNA) sequences for CRISPR applications. This tool allows researchers to input disease-related mutations in HGVS format and automatically generates optimized gRNA spacers for genome editing.

## 🧬 Overview

The Disease Mutations App is designed to streamline the process of designing CRISPR guide RNAs for therapeutic applications. It takes Human Genome Variation Society (HGVS) formatted mutation data, retrieves the relevant genomic sequences, and identifies optimal gRNA spacer sequences based on multiple quality metrics.

### Key Features

- **HGVS Format Support**: Parse and process mutations in standard HGVS notation
- **Automated Sequence Retrieval**: Fetch genomic sequences from NCBI databases
- **gRNA Optimization**: Generate and rank gRNA candidates based on:
  - GC content (optimal range: 40-60%)
  - Homopolymer runs (penalizes sequences with 4+ consecutive identical bases)
  - Off-target alignment analysis using Bowtie
- **Visual Comparison**: Side-by-side display of original and mutated sequences
- **Interactive Selection**: Easy-to-use interface for selecting and copying complete gRNA sequences
- **Real-time Analysis**: Asynchronous processing with loading indicators

## 🏗️ Architecture

The application consists of three main components:

### 1. Frontend (Blazor WebAssembly)
- **Technology**: ASP.NET Core Blazor WebAssembly
- **Language**: C# 9.0
- **UI Framework**: Bootstrap 5 with BlazorBootstrap components
- **Port**: 8080 (production)

### 2. Backend (ASP.NET Core Minimal API)
- **Technology**: ASP.NET Core 9.0 Minimal API
- **Language**: C# 9.0 with F# 8.0 libraries
- **Port**: 5000 (production)

### 3. gRNA Library (F# Module)
- **Language**: F# 8.0
- **Purpose**: Core bioinformatics logic and Bowtie integration
- **Key Modules**:
  - `SpacerFinder.fs`: gRNA candidate generation and ranking
  - `BowtieWrapper.fs`: Off-target alignment analysis
  - `RNAFoldWrapper.fs`: RNA secondary structure prediction (ViennaRNA)
  - `HGVS.fs`: HGVS notation parser
  - `Sequence.fs`: DNA sequence manipulation
  - `SequenceRepository.fs`: NCBI sequence retrieval
  - `SNP.fs`: SNP database integration

## 🐳 Docker Architecture

The application uses a multi-stage Docker build strategy for efficient deployment:

### Stage 1: Base Image (`Dockerfile.bowtie-base`)
Creates a reusable base image containing:
- .NET 9.0 ASP.NET runtime
- Bowtie alignment tool executable
- Pre-indexed GRCh38 reference genome (~4GB)

**Benefits**:
- **One-time build**: Large genome indexes only copied once
- **Faster rebuilds**: Application changes don't require re-copying genome data
- **Layer caching**: Docker efficiently caches the base image
- **Smaller incremental builds**: Only application code is rebuilt during development

### Stage 2: Application Image (`Dockerfile`)
Multi-stage build process:
1. **Build stage**: Compiles C# and F# code using .NET SDK
2. **Publish stage**: Creates optimized production artifacts
3. **Final stage**: 
   - Uses pre-built bowtie base image
   - Installs Python and ViennaRNA for RNA folding
   - Copies compiled application
   - Configures runtime environment

**Build Flow**:
```
Dockerfile.bowtie-base → disease-mutations-bowtie:latest
                                    ↓
                         Dockerfile (multi-stage)
                                    ↓
                    disease-mutations-backend:latest
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
- **CPU**: 2+ cores (8 cores recommended for optimal performance)
- **RAM**: 2GB minimum (4GB recommended)
- **Storage**: ~5GB for reference genome indexes

## 🚀 Getting Started

### Option 1: Docker Deployment (Recommended)

1. **Clone the repository**:
   ```bash
   git clone https://github.com/yourusername/DiseaseMutationsApp.git
   cd DiseaseMutationsApp
   ```

2. **Ensure Bowtie indexes are in place**:
   ```bash
   # Indexes should be located in:
   # ./bowtie/indexes/GCA_000001405.15_GRCh38_no_alt_analysis_set.*.ebwt
   ```

3. **Build and run with Docker Compose**:
   ```bash
   docker compose up -d
   ```

5. **Access the application**:
   - Frontend: http://localhost:8080
   - Backend API: http://localhost:5000

#### Quick Rebuild During Development

After the initial setup, you can rebuild just the application (without re-copying Bowtie indexes):

```bash
# Rebuild and restart only the backend
docker compose up -d --build backend

# View logs
docker compose logs -f backend
```

The bowtie base image remains cached, making subsequent builds much faster (seconds instead of minutes).

### Option 2: Local Development

1. **Clone the repository**:
   ```bash
   git clone https://github.com/yourusername/DiseaseMutationsApp.git
   cd DiseaseMutationsApp
   ```

2. **Restore dependencies**:
   ```bash
   dotnet restore
   ```

3. **Run the backend**:
   ```bash
   cd Backend
   dotnet run
   ```

4. **Run the frontend** (in a separate terminal):
   ```bash
   cd DiseaseMutationsApp
   dotnet run
   ```

5. **Access the application**:
   - Frontend: http://localhost:5000 (or the port shown in console)
   - Backend API: http://localhost:5000

## 📖 Usage Guide

### Basic Workflow

You can start from an HGVS notation directly, an rsID, or an OMIM identifier. The app guides you through resolving and selecting the variant you want to design gRNAs for.

1. **Provide an input (HGVS, rsID, or OMIM)**:
   - HGVS example: `NC_000017.11:g.7674220C>T`
   - rsID example: `rs12345` (will resolve to one or more HGVS notations)
   - OMIM example: Use the dedicated OMIM→rs page to fetch associated rsIDs.

2. **Resolve inputs when needed**:
   - For **OMIM IDs**, navigate to the independent OMIM→rs page (menu: "OMIM → rs") to fetch related rsIDs using the backend (`/getrsfromomim`). After selecting an rsID (e.g., `rs12345`), return to the main workflow and proceed with rs→HGVS.
   - For **rsID inputs**, the app retrieves HGVS notations using the backend (`/gethgvsfromsnp`). These HGVS options appear as **subtabs**; pick the specific variant you want to analyze.
   - Example HGVS candidates you may see in subtabs:
     - `NG_016465.4:g.98765C>T`
     - `NG_056131.3:g.755G>A`

3. **Set gRNA size**:
   - Default: 28 nucleotides
   - Adjust based on your CRISPR system requirements

4. **Fetch data**:
   - Click "Fetch Data" to:
     - Parse the HGVS notation
     - Retrieve the genomic sequence
     - Generate mutated sequence
     - Find optimal gRNA candidates
     - Analyze off-target binding

5. **Review results**:
   - **Original Sequence**: Reference genome sequence (mutation highlighted in bold)
   - **Mutated Sequence**: Sequence with the mutation applied (mutation highlighted in bold)
   - **gRNA Results Table**: Top 5 candidate spacers ranked by quality

6. **Select a gRNA**:
   - Click the 🔨 button next to your preferred spacer
   - The complete gRNA (scaffold + spacer) appears at the bottom

7. **Copy complete gRNA**:
   - Click the "📋 Copy" button to copy the full gRNA sequence to clipboard
   - Use this sequence for RNA synthesis or further analysis

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

## 🧪 API Endpoints

### GET `/`
Health check endpoint.

**Response**: `"Disease Mutations Backend is running."`

---

### GET `/getallignments`
Get Bowtie alignment results for a sequence.

**Parameters**:
- `sequence` (string): DNA sequence to align
- `mismatches` (int): Number of allowed mismatches
- `threads` (int): Number of CPU threads to use

**Response**: Array of alignment strings

---

### GET `/getbestgrnafromhgvs`
Complete workflow: Parse HGVS, retrieve sequences, and find best gRNAs.

**Parameters**:
- `hgvs` (string): HGVS notation of the mutation
- `window` (int): Length of the gRNA spacer

**Response**: Object containing gRNA results and sequence information

---

### GET `/gethgvsfromsnp`
Retrieve HGVS notations for a given SNP rsID.

**Parameters**:
- `rsid` (string): SNP reference ID (e.g., "rs123456")

**Response**: Array of HGVS notation strings

---

### GET `/getrsfromomim`
Retrieve SNP rsIDs associated with an OMIM entry.

**Parameters**:
- `omim` (int): OMIM numeric identifier

**Response**: Array or collection of rsIDs associated with the OMIM entry

---

### GET `/getrnafold`
Predict RNA secondary structure using ViennaRNA.

**Parameters**:
- `sequence` (string): RNA sequence to fold

**Response**: Object with structure and energy
```json
{
  "structure": "(((...)))........",
  "energy": -2.0999999046325684
}
```

**Notes**:
- Structure uses dot-bracket notation: `(` = paired, `.` = unpaired
- Energy is minimum free energy (MFE) in kcal/mol
- More negative energy indicates more stable structure

---

### GET `/getfornaurl`
Generate a FORNA visualization URL for a given RNA sequence and structure.

**Parameters**:
- `sequence` (string): RNA sequence
- `structure` (string): Dot-bracket structure

**Response**: URL string for FORNA visualization

## 🧬 Complete gRNA Structure

The application generates complete gRNA sequences consisting of two parts:

```
[Scaffold Sequence (37nt)] + [Spacer Sequence (20-28nt)]
```

**Scaffold (constant)**: `GAUUUAGACUACCCCAAAAACGAAGGGGACUAAAAC`
- Provides structural framework for Cas9 binding
- Universal sequence used in most CRISPR applications

**Spacer (variable)**: Selected from candidates
- Target-specific sequence (shown in blue in the UI)
- Guides Cas9 to the desired genomic location

## 🔬 Algorithm Details

### gRNA Candidate Generation

1. **Sliding Window Analysis**:
   - Generate all possible subsequences of specified length
   - Extract from the mutated sequence region

2. **Quality Scoring**:
   ```
   Score = (Alignments, -GC_Score, Homopolymer_Count)
   ```
   - Sorted by: fewest alignments → highest GC score → fewest homopolymers

3. **GC Content Calculation**:
   ```
   GC_Score = 1.0 if 40% ≤ GC% ≤ 60%
   GC_Score = GC% / 40% if GC% < 40%
   GC_Score = (100% - GC%) / 40% if GC% > 60%
   ```

4. **Off-Target Analysis**:
   - Uses Bowtie short-read aligner
   - Searches GRCh38 reference genome
   - Allows up to 2 mismatches
   - Reports top 6 alignments per candidate

5. **Ranking**:
   - Select top 5 candidates
   - Present in order of suitability

## 📁 Project Structure

```
DiseaseMutationsApp/
├── Backend/                      # ASP.NET Core Minimal API
│   ├── Program.cs               # API endpoints and configuration
│   ├── Backend.csproj           # C# project file
│   └── appsettings.json         # Configuration settings
│
├── DiseaseMutationsApp/         # Blazor WebAssembly Frontend
│   ├── Pages/
│   │   ├── Index.razor          # Main UI page (HGVS / rs workflow)
│   │   └── OmimToRs.razor       # Independent page for OMIM → rs resolution
│   ├── Services/
│   │   └── IDiseaseMutationsApi.cs  # API client interface (Refit)
│   ├── wwwroot/                 # Static assets
│   ├── Program.cs               # Frontend entry point
│   └── DiseaseMutationsApp.csproj
│
├── gRNA/                        # F# Core Library
│   ├── HGVS.fs                  # HGVS notation parser
│   ├── Sequence.fs              # DNA sequence manipulation
│   ├── SequenceRepository.fs    # NCBI API integration
│   ├── SpacerFinder.fs          # gRNA generation and scoring
│   ├── BowtieWrapper.fs         # Bowtie alignment wrapper
│   └── gRNA.fsproj              # F# project file
│
├── DiseaseMutationsAppTests/    # Unit tests
│   ├── HGVSTests.cs             # HGVS parser tests
│   ├── SequenceTests.cs         # Sequence manipulation tests
│   └── DiseaseMutationsAppTests.csproj
│
├── bowtie/                      # Bowtie aligner
│   ├── bowtie-align-s           # Bowtie executable (Linux)
│   ├── bowtie-align-s.exe       # Bowtie executable (Windows)
│   └── indexes/                 # GRCh38 reference indexes
│       ├── GCA_000001405.15_GRCh38_no_alt_analysis_set.*.ebwt
│       └── ...
│
├── docker-compose.yml           # Docker orchestration
├── Dockerfile                   # Backend container image
├── Dockerfile.bowtie-base       # Base image with Bowtie and indexes
└── DiseaseMutationsApp.sln      # Visual Studio solution
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

### Backend Configuration (`Backend/appsettings.json`)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

### CORS Configuration

The backend is configured to allow requests from:
- `http://localhost:8080` (production frontend)

To modify allowed origins, edit `Backend/Program.cs`:

```csharp
options.AddPolicy("AllowFrontend", policy =>
{
    policy.WithOrigins("http://localhost:8080", "https://yourdomain.com")
          .AllowAnyMethod()
          .AllowAnyHeader();
});
```

### Docker Resource Limits

Configure in `docker-compose.yml`:

```yaml
deploy:
  resources:
    limits:
      cpus: '8.0'
      memory: 4G
    reservations:
      cpus: '2.0'
      memory: 2G
```

## 🐛 Troubleshooting

### Common Issues

#### "Bowtie indexes not found"
**Solution**: Ensure Bowtie index files are in `bowtie/indexes/` directory with the correct naming convention.

#### "Cannot connect to backend"
**Solution**: 
- Verify backend is running on port 5000
- Check CORS configuration
- Ensure firewall allows connections

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

#### Docker build fails
**Solution**:
- Ensure Docker has sufficient memory allocated (4GB+)
- Check that all required files are in the build context
- Verify Bowtie indexes are accessible

## 🔒 Security Considerations

### Production Deployment

1. **HTTPS Configuration**: Enable HTTPS in production
2. **API Rate Limiting**: Implement rate limiting to prevent abuse
3. **Input Validation**: HGVS inputs are validated, but consider additional sanitization
4. **CORS Restrictions**: Update allowed origins to match your production domain
5. **Resource Limits**: Configure appropriate CPU/memory limits based on expected load

## 🚧 Future Enhancements

Potential improvements and features:

- [ ] Support for PAM site analysis and filtering
- [ ] Export results to CSV/JSON
- [ ] Batch processing of multiple mutations
- [ ] Additional genome assemblies (GRCh37, T2T-CHM13)
- [ ] Prime editing gRNA design
- [ ] Integration with ClinVar database
- [ ] Visualization of gRNA binding sites
- [ ] Machine learning-based efficiency prediction
- [ ] Authentication and user accounts
- [ ] Save and share gRNA designs

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

- Your Name - Initial work

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

## 📞 Support

For issues, questions, or suggestions:
- Open an issue on GitHub
- Contact: javiertorralbocortes@gmail.com

---

**Last Updated**: December 2025  
**Version**: 1.0.0
