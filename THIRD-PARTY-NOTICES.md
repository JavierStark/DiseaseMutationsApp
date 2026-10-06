# Third-party notices

This project's own source is MIT licensed (see [LICENSE](LICENSE)). The application image and repository
redistribute or install the components below under **their own** licenses. This file states facts so you
can make your own assessment; it is not legal advice. If this software is distributed beyond a research
lab, the Bowtie and ViennaRNA entries in particular deserve a proper review.

| Component | Where it comes from | License | Notes |
|---|---|---|---|
| **Bowtie 1.3.1** (`bowtie/bowtie-align-s`) | Committed binary, copied into the image. SHA256 `45c58c69a10577e84e459e6b76d6c8f17ab030a5510d4b6399e6434228a9eaca` matches the `bowtie-1.3.1-linux-x86_64` release asset. | **Artistic License 2.0** | Source and license: https://github.com/BenLangmead/bowtie (tag `v1.3.1`; the `LICENSE` file at that tag is the Artistic License 2.0, which Bowtie adopted from 1.2.2 on, replacing the GPL of earlier releases). The binary runs as a separate child process. Please cite Langmead et al. 2009 (see the README references). |
| **ViennaRNA 2.7.2** | `pip install viennarna==2.7.2` into the image | **ViennaRNA License** (custom, *not* MIT) | Free for educational, research and non-profit use; **commercial use requires a separate license from the ViennaRNA authors.** Read https://www.tbi.univie.ac.at/RNA/ViennaRNA/doc/html/license.html before any commercial deployment. |
| **GRCh38 no-alt analysis-set Bowtie index** | `s3://genome-idx/bt/GRCh38_noalt_as.zip` (3,749,245,718 bytes, SHA256 `f12495639adbc9bc676eba68044c6bfb1145e0ca587beaf6c7c41446f9d3c573`), baked into the base image | Reference data from the Genome Reference Consortium / NCBI | Governed by the NCBI and GRC data-use terms: https://www.ncbi.nlm.nih.gov/home/about/policies/ |
| **NCBI E-utilities / dbSNP APIs** | Runtime HTTPS calls | NCBI usage policy | Rate limit 3 requests/s without an API key. Set `GRNA_NCBI_API_KEY` and `GRNA_NCBI_CONTACT`. |
| **Bootstrap 5.3.3** (`wwwroot/css/bootstrap`) | Vendored CSS | MIT | Licence header retained in the file. https://github.com/twbs/bootstrap |
| **IBM Plex Sans 5.1.1** (`wwwroot/fonts`) | `@fontsource/ibm-plex-sans` | SIL OFL 1.1 | `wwwroot/fonts/LICENSE-IBM-Plex-Sans.txt`. SHA256: 400 `db71f8a2...1922`, 600 `31535a91...5eb6`. |
| **JetBrains Mono 5.1.1** (`wwwroot/fonts`) | `@fontsource/jetbrains-mono` | SIL OFL 1.1 | `wwwroot/fonts/LICENSE-JetBrains-Mono.txt`. SHA256: 400 `14425ba9...89eb`, 600 `400c6bfd...f147`. |
| **s5cmd 2.2.2** | Build-time only (downloads the index; not in the final image) | MIT | Checksum-verified in `Dockerfile.bowtie-base`. |
| **.NET 9 / ASP.NET Core / FSharp.Core 9.0.303** | NuGet + base images | MIT | Versions pinned in `Directory.Packages.props` and the Dockerfiles. |
| **NUnit, bUnit, coverlet, JunitXml.TestLogger** | Test-only NuGet packages | MIT / BSD-style | Not shipped in the image. |

Open Iconic (previously vendored) has been removed; icons are an original SVG sprite (`wwwroot/icons.svg`).
