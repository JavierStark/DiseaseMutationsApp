namespace DiseaseMutationsApp.Services;

public record RestrictionEnzyme(string Name, string Site);

/// <summary>
/// Flags recognition sites of common cloning enzymes in DNA that will be ordered, since a site inside the construct
/// breaks Golden Gate or restriction cloning. Both strands are searched: the Type IIS enzymes are not palindromic.
/// </summary>
public static class RestrictionSites
{
    public static readonly IReadOnlyList<RestrictionEnzyme> Enzymes = new[]
    {
        new RestrictionEnzyme("BsaI", "GGTCTC"),
        new RestrictionEnzyme("BsmBI", "CGTCTC"),
        new RestrictionEnzyme("SapI", "GCTCTTC"),
        new RestrictionEnzyme("BbsI", "GAAGAC"),
        new RestrictionEnzyme("EcoRI", "GAATTC"),
        new RestrictionEnzyme("PstI", "CTGCAG"),
        new RestrictionEnzyme("XbaI", "TCTAGA"),
        new RestrictionEnzyme("SpeI", "ACTAGT"),
        new RestrictionEnzyme("EcoRV", "GATATC"),
        new RestrictionEnzyme("NotI", "GCGGCCGC"),
    };

    /// <summary>Names of the enzymes whose site occurs on either strand of the sequence (RNA is read as DNA), in list order.</summary>
    public static IReadOnlyList<string> Find(string sequence)
    {
        if (string.IsNullOrWhiteSpace(sequence)) return Array.Empty<string>();
        var top = OligoExport.ToDna(sequence);
        var bottom = OligoExport.ReverseComplement(top);
        return Enzymes
            .Where(e => top.Contains(e.Site, StringComparison.Ordinal) || bottom.Contains(e.Site, StringComparison.Ordinal))
            .Select(e => e.Name)
            .ToList();
    }

    /// <summary>Sites in the DNA that would be ordered for this spacer: scaffold + spacer, behind the T7 promoter if requested.</summary>
    public static IReadOnlyList<string> ForGuide(string spacerRna, bool includeT7) =>
        Find(OligoExport.Template(spacerRna, includeT7, OligoStrand.Sense));

    public static string Warning(string enzyme) =>
        $"Warning: This DNA sequence includes a restriction site for the enzyme: '{enzyme}'";
}
