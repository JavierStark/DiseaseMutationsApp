using gRNA;

namespace DiseaseMutationsApp.Services;

/// <summary>The slice of the gRNA pipeline the analysis runner depends on; lets tests substitute a fake.</summary>
public interface IGrnaAnalysis
{
    Task<ResultFromHGVS> GetBestgRNAFromHgvs(string hgvs, int window, int seedStart, int seedEnd, bool complement = false, CancellationToken cancellationToken = default);
    Task<List<string>> GetHgvsFromSnp(string rsid, CancellationToken cancellationToken = default);
    string? GetNcbiNuccoreUrl(string hgvs);

    /// <summary>
    /// Same as <see cref="GetBestgRNAFromHgvs"/> with the substitution rule's positions, both counted from the spacer's
    /// 3' end (1 = last letter): the variant at <paramref name="snpPos"/>, the engineered mismatch at <paramref name="mismatchPos"/>.
    /// The default implementation ignores the positions.
    /// </summary>
    Task<ResultFromHGVS> GetBestgRNAFromHgvsWithRule(string hgvs, int window, int seedStart, int seedEnd, int snpPos, int mismatchPos, bool complement = false, CancellationToken cancellationToken = default) =>
        GetBestgRNAFromHgvs(hgvs, window, seedStart, seedEnd, complement, cancellationToken);

    /// <summary>
    /// The notations of an rsID with their context (gene, orientation, significance). By default only the relevant allele(s)
    /// on the main RefSeqGene record; <paramref name="allAlleles"/> lists everything dbSNP has. The default implementation
    /// wraps <see cref="GetHgvsFromSnp"/> and knows nothing about genes.
    /// </summary>
    async Task<List<SnpVariantInfo>> GetSnpVariants(string rsid, bool allAlleles, CancellationToken cancellationToken = default) =>
        (await GetHgvsFromSnp(rsid, cancellationToken)).Select(h => new SnpVariantInfo(h, null, null, true)).ToList();
}

/// <summary>One notation of an rsID. <see cref="GeneOnPlusStrand"/> says whether the gene is read along the + strand of the accession.</summary>
public record SnpVariantInfo(string Hgvs, string? Gene, bool? GeneOnPlusStrand, bool Preferred);

/// <summary>
/// Service that provides direct access to the F# gRNA library functionality.
/// Replaces the HTTP-based IDiseaseMutationsApi.
/// </summary>
public class GrnaService : IGrnaAnalysis
{
    private readonly ILogger<GrnaService> _logger;
    private readonly gRNA.Services.BowtieService _bowtieService;

    public GrnaService(ILogger<GrnaService> logger, gRNA.Services.BowtieService bowtieService)
    {
        _logger = logger;
        _bowtieService = bowtieService;
    }

    /// <summary>
    /// The constant Cas13 scaffold (36 nt) that precedes the spacer in a complete gRNA.
    /// </summary>
    public static string Scaffold => gRNA.SpacerFinder.scaffold;

    public Task<ResultFromHGVS> GetBestgRNAFromHgvs(string hgvs, int window, int seedStart, int seedEnd, bool complement = false, CancellationToken cancellationToken = default) =>
        GetBestgRNAFromHgvsWithRule(hgvs, window, seedStart, seedEnd, SpacerFinder.DefaultSnpPosition, SpacerFinder.DefaultMismatchPosition, complement, cancellationToken);

    public async Task<ResultFromHGVS> GetBestgRNAFromHgvsWithRule(string hgvs, int window, int seedStart, int seedEnd, int snpPos, int mismatchPos, bool complement = false, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting best gRNA from HGVS: {Hgvs}, Window: {Window}, Seed: [{SeedStart}, {SeedEnd}], Rule: variant {SnpPos}, mismatch {MismatchPos}, Complement: {Complement}", hgvs, window, seedStart, seedEnd, snpPos, mismatchPos, complement);

            var rule = new Main.SpecialRuleOptions(snpPos, mismatchPos);
            var fsharpResult = await Main.getBestgRNAFromHGVSWithRule(rule, hgvs, window, seedStart, seedEnd, _bowtieService, cancellationToken, complement);

            return new ResultFromHGVS
            {
                gRNA = MapGrnaResults(fsharpResult.gRNA),
                OriginalGRNA = MapGrnaResults(fsharpResult.originalGRNA),
                MutatedSequence = fsharpResult.mutatedSequence,
                OriginalSequence = fsharpResult.originalSequence,
                ExtraNucleotids = fsharpResult.extraNucleotids,
                WindowStart = fsharpResult.windowStart,
                WindowEnd = fsharpResult.windowEnd,
                Notices = fsharpResult.notices.ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting best gRNA from HGVS: {Hgvs}", hgvs);
            throw;
        }
    }

    private static List<GRNAResult> MapGrnaResults(IEnumerable<gRNA.SpacerFinder.gRNAResult> results)
    {
        return results
            .Select(g => new GRNAResult
            {
                Sequence = g.Sequence,
                GCScore = (float)g.GCScore,
                GCContent = (float)g.GCContent,
                HomopolymerCount = g.HomopolymerCount,
                SeedRegion = g.SeedRegion,
                Allignments = g.Allignments,
                RnaFoldResult = new RNAFoldResult
                {
                    Structure = g.RnaFoldResult.Structure,
                    Energy = g.RnaFoldResult.Energy
                },
                Rank = g.Rank,
                Score = g.Score,
                MutationHighlightStart = g.MutationHighlightStart,
                MutationHighlightLength = g.MutationHighlightLength,
                WtMismatches = g.WtMismatches,
                Engineered = g.Engineered,
                ScaffoldIntact = g.ScaffoldIntact,
                SpacerUnpaired = g.SpacerUnpaired
            })
            .ToList();
    }

    public async Task<List<SnpVariantInfo>> GetSnpVariants(string rsid, bool allAlleles, CancellationToken cancellationToken = default)
    {
        var variants = await SNP.getVariantsAsync(rsid, cancellationToken);
        var chosen = allAlleles ? variants.ToList() : variants.Where(v => v.Preferred).ToList();
        if (chosen.Count == 0 && !allAlleles) chosen = variants.ToList();   // never hide everything
        return chosen
            .Select(v => new SnpVariantInfo(
                v.Hgvs,
                Microsoft.FSharp.Core.FSharpOption<string>.get_IsSome(v.Gene) ? v.Gene.Value : null,
                Microsoft.FSharp.Core.FSharpOption<bool>.get_IsSome(v.GeneOnPlusStrand) ? v.GeneOnPlusStrand.Value : null,
                v.Preferred))
            .ToList();
    }

    public async Task<List<string>> GetHgvsFromSnp(string rsid, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting HGVS notations from SNP: {RsId}", rsid);

            var fsharpList = await SNP.getHgvsNotationsAsync(rsid, cancellationToken);
            return new List<string>(fsharpList);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting HGVS from SNP: {RsId}", rsid);
            throw;
        }
    }


    public async Task<RNAFoldResult> GetRnaFold(string sequence, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Getting RNA fold for sequence of length: {Length}", sequence.Length);

            var fsharpResult = await RNAFoldWrapper.fold(sequence, cancellationToken);

            return new RNAFoldResult
            {
                Structure = fsharpResult.Structure,
                Energy = fsharpResult.Energy
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting RNA fold for sequence");
            throw;
        }
    }

    /// <summary>Maximum alignments requested when listing off-target loci (the batch path caps at 6).</summary>
    public const int MaxLociHits = 50;

    /// <summary>The DNA window a spacer was derived from: spacer = reverse(complement(window)) with T to U.</summary>
    public static string WindowFromSpacer(string spacer) =>
        gRNA.Sequence.complementary(new string(spacer.Replace('U', 'T').Reverse().ToArray()));

    /// <summary>Lists where a spacer's target window aligns in the genome (chromosome, position, strand, mismatches).</summary>
    public async Task<OffTargetReport> GetOffTargets(string spacer, CancellationToken cancellationToken = default)
    {
        var hits = await _bowtieService.FindOffTargetsAsync(WindowFromSpacer(spacer), 2, MaxLociHits, cancellationToken);
        var loci = hits
            .Select(h => new OffTargetLocus(h.Reference, h.Offset + 1, h.Strand == '-' ? "-" : "+", h.MismatchCount, h.MismatchDetail))
            .OrderBy(l => l.Mismatches).ThenBy(l => l.Reference, StringComparer.Ordinal).ThenBy(l => l.Position)
            .ToList();
        return new OffTargetReport(loci, loci.Count >= MaxLociHits);
    }

    private static readonly System.Text.RegularExpressions.Regex FornaSequence = new("^[ACGUTacgut]+$");
    private static readonly System.Text.RegularExpressions.Regex FornaStructure = new(@"^[.()\[\]]+$");

    /// <summary>
    /// FORNA link. Two quirks of that host, both verified: it refuses HTTPS (connection refused), and its page script splits
    /// the query string itself WITHOUT percent-decoding, so encoded values (e.g. %28 for a bracket) reach its API verbatim
    /// and are rejected with HTTP 400. Sequence and dot-bracket characters are all URL-safe, so they go in raw; anything
    /// else is refused rather than allowed to inject extra query parameters. Browsers block the http frame inside an https
    /// page, so the UI draws the structure itself (see StructureDiagram) and offers this URL only as an opt-in new-tab link.
    /// </summary>
    public string GetFornaUrl(string sequence, string structure)
    {
        if (!FornaSequence.IsMatch(sequence)) throw new ArgumentException("Sequence must contain only A, C, G, U/T.", nameof(sequence));
        if (!FornaStructure.IsMatch(structure)) throw new ArgumentException("Structure must be dot-bracket notation.", nameof(structure));
        return $"http://nibiru.tbi.univie.ac.at/forna/forna.html?id=url/name&sequence={sequence}&structure={structure}";
    }

    public string? GetNcbiNuccoreUrl(string hgvs)
    {
        if (string.IsNullOrWhiteSpace(hgvs))
        {
            return null;
        }

        var accession = hgvs.Split(':', 2)[0].Trim();
        if (string.IsNullOrWhiteSpace(accession))
        {
            return null;
        }

        return $"https://www.ncbi.nlm.nih.gov/nuccore/{Uri.EscapeDataString(accession)}";
    }
}

// Model classes previously defined in IDiseaseMutationsApi.cs
public record GRNAResult
{
    public required string Sequence { get; init; }
    public float GCScore { get; init; }
    public float GCContent { get; init; }
    public int HomopolymerCount { get; init; }
    public required string SeedRegion { get; init; }
    public int Allignments { get; init; }
    public required RNAFoldResult RnaFoldResult { get; init; }
    public int Rank { get; init; }
    public double Score { get; init; }
    public int MutationHighlightStart { get; init; }
    public int MutationHighlightLength { get; init; }
    /// <summary>Fewest letters in which the spacer's DNA target differs from the other allele (-1 = not computed). 0-1 means the guide cannot tell the alleles apart.</summary>
    public int WtMismatches { get; init; } = -1;
    /// <summary>True for the spacer built by the substitution rule (a deliberate extra mismatch next to the variant).</summary>
    public bool Engineered { get; init; }
    /// <summary>False when the spacer pulls the scaffold's own hairpin apart in the fold of the complete gRNA.</summary>
    public bool ScaffoldIntact { get; init; } = true;
    /// <summary>Share (0-1) of the spacer left unpaired in that fold; -1 = not computed.</summary>
    public double SpacerUnpaired { get; init; } = -1;
}

public record ResultFromHGVS
{
    public required List<GRNAResult> gRNA { get; init; }
    public required List<GRNAResult> OriginalGRNA { get; init; }
    public required string MutatedSequence { get; init; }
    public required string OriginalSequence { get; init; }
    public int ExtraNucleotids { get; init; }
    /// <summary>1-based inclusive window of the accession the sequences were cut from (0 when unknown).</summary>
    public int WindowStart { get; init; }
    public int WindowEnd { get; init; }
    /// <summary>Things the user should know about this result, shown as warnings (for example that the substitution rule did not apply).</summary>
    public List<string> Notices { get; init; } = new();
}

public record RNAFoldResult
{
    public required string Structure { get; init; }
    public double Energy { get; init; }
}

/// <summary>One genomic alignment of a spacer's target window (1-based position).</summary>
public record OffTargetLocus(string Reference, long Position, string Strand, int Mismatches, string Detail);

/// <summary>Loci for one spacer; <see cref="Truncated"/> means the cap was reached and more may exist.</summary>
public record OffTargetReport(List<OffTargetLocus> Loci, bool Truncated);
