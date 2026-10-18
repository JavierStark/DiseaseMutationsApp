using System.Text;
using gRNA;

namespace DiseaseMutationsApp.Services;

public enum OligoStrand { Sense, Antisense }

/// <summary>
/// Turns chosen spacers into what a researcher orders: DNA, not RNA. The complete gRNA (scaffold + spacer) is
/// reverse-transcribed to DNA, optionally behind a T7 promoter for in-vitro transcription, and emitted as a
/// top/bottom oligo pair (annealed to make the template) with plate wells addressed like the pooling plan.
/// </summary>
public static class OligoExport
{
    /// <summary>
    /// T7 class III promoter up to (not including) the +1 base. The Cas13 scaffold begins with G, which becomes the
    /// +1 transcription start, so the promoter is followed directly by the gRNA DNA.
    /// </summary>
    public const string T7Promoter = "TAATACGACTCACTATA";

    public record OligoPair(string Name, string Top, string Bottom);

    /// <summary>RNA to DNA: uppercase, U to T.</summary>
    public static string ToDna(string rna) => rna.Trim().ToUpperInvariant().Replace('U', 'T');

    public static string ReverseComplement(string dna) =>
        new(Sequence.complementary(dna).Reverse().ToArray());

    /// <summary>The DNA read 5' to 3' for the chosen strand: sense equals the transcript, antisense is its reverse complement.</summary>
    public static string Template(string spacerRna, bool includeT7, OligoStrand strand, ConstructOptions? construct = null)
    {
        var sense = (includeT7 ? T7Promoter : "") + ToDna(GuideConstruct.Build(spacerRna, construct));
        return strand == OligoStrand.Sense ? sense : ReverseComplement(sense);
    }

    /// <summary>Top and bottom oligo of the annealed pair for one guide; top is always the sense strand.</summary>
    public static OligoPair Pair(string name, string spacerRna, bool includeT7, ConstructOptions? construct = null)
    {
        var top = Template(spacerRna, includeT7, OligoStrand.Sense, construct);
        return new OligoPair(name, top, ReverseComplement(top));
    }

    /// <summary>
    /// FASTA of the complete (built) guides, one record per shortlist item. The header carries the variant, the strand,
    /// the bare spacer and the construct recipe so the file stands alone. DNA (T) by default, RNA (U) on request. No T7 promoter.
    /// </summary>
    public static string ShortlistFasta(IEnumerable<ShortlistItem> items, ConstructOptions? construct = null, bool rna = false)
    {
        var sb = new StringBuilder();
        foreach (var item in items)
            sb.Append(FastaRecord(item.Hgvs, item.IsComplement, item.Spacer, construct, rna));
        return sb.ToString();
    }

    /// <summary>One FASTA record for a single guide (ends with a newline).</summary>
    public static string FastaRecord(string hgvs, bool isComplement, string spacerRna, ConstructOptions? construct = null, bool rna = false)
    {
        var built = GuideConstruct.Build(spacerRna, construct);
        var seq = rna ? built : ToDna(built);
        var name = $"{hgvs}{(isComplement ? "_C" : "")}".Replace(' ', '_');
        return $">{name}|spacer={spacerRna}|construct={GuideConstruct.Describe(construct)}|{(rna ? "RNA" : "DNA")}|{seq.Length}nt\n{seq}\n";
    }

    /// <summary>
    /// Plate-format order CSV: one row per oligo, wells filled row-major from A1 across plates, using the same
    /// well addressing as the pooling plan. With <paramref name="bothOligos"/> each guide gets a top and a bottom row.
    /// </summary>
    public static string ShortlistCsv(IEnumerable<ShortlistItem> items, bool includeT7, bool bothOligos, PlateKind plate, ConstructOptions? construct = null)
    {
        var format = plate == PlateKind.Plate384 ? Pooling.plate384 : Pooling.plate96;
        var sb = new StringBuilder();
        sb.AppendLine("Well,Name,Oligo,Sequence 5'-3',Length,Restriction sites");
        var well = 1;
        foreach (var item in items)
        {
            var baseName = $"{item.Hgvs}{(item.IsComplement ? "_C" : "")}".Replace(' ', '_');
            var pair = Pair(baseName, item.Spacer, includeT7, construct);
            var sites = string.Join(";", RestrictionSites.ForGuide(item.Spacer, includeT7, construct));
            var rows = bothOligos
                ? new[] { ("top", pair.Top), ("bottom", pair.Bottom) }
                : new[] { ("top", pair.Top) };
            foreach (var (label, seq) in rows)
            {
                var address = Pooling.wellAddress(format, well++);
                sb.AppendLine(string.Join(",", GrnaCsvSchema.Csv(address.Label), GrnaCsvSchema.Csv($"{pair.Name}_{label}"), label, seq, seq.Length, sites));
            }
        }

        return sb.ToString();
    }
}
