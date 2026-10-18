namespace DiseaseMutationsApp.Services;

public record RestrictionEnzyme(string Name, string Site);

/// <summary>A cloning standard and the enzymes whose sites must be absent from the DNA for it to be compatible.</summary>
public record AssemblyStandard(string Rfc, string Name, IReadOnlyList<string> Enzymes);

/// <summary>Whether a sequence is compatible with an assembly standard, and which of its enzymes cut it if not.</summary>
public record AssemblyCompatibility(AssemblyStandard Standard, IReadOnlyList<string> Blocking)
{
    public bool IsCompatible => Blocking.Count == 0;
}

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

    public static readonly IReadOnlyList<AssemblyStandard> Standards = new[]
    {
        new AssemblyStandard("10", "BioBrick RFC[10]", new[] { "EcoRI", "NotI", "XbaI", "SpeI", "PstI" }),
        new AssemblyStandard("1000", "iGEM Type IIS Assembly RFC[1000]", new[] { "BsaI", "SapI" }),
    };

    /// <summary>Compatibility of the DNA ordered for this spacer (scaffold + spacer) with each assembly standard.</summary>
    public static IReadOnlyList<AssemblyCompatibility> CompatibilityForGuide(string spacerRna, bool includeT7 = false, ConstructOptions? construct = null) =>
        Compatibility(ForGuide(spacerRna, includeT7, construct));

    /// <summary>Given the enzymes found in a sequence, which standards it is compatible with.</summary>
    public static IReadOnlyList<AssemblyCompatibility> Compatibility(IReadOnlyList<string> foundEnzymes) =>
        Standards.Select(st => new AssemblyCompatibility(st, st.Enzymes.Where(foundEnzymes.Contains).ToList())).ToList();

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

    /// <summary>
    /// Sites in the DNA that would be ordered for this spacer: the built construct (DR, adapters, custom flanks), behind
    /// the T7 promoter if requested. The engineered adapters carry their own enzyme sites on purpose (they are what the
    /// assembly cuts), so only sites beyond those the adapters bring are reported.
    /// </summary>
    public static IReadOnlyList<string> ForGuide(string spacerRna, bool includeT7, ConstructOptions? construct = null)
    {
        var full = OligoExport.Template(spacerRna, includeT7, OligoStrand.Sense, construct);
        // Each side's adapters joined as built (Frida inside BioBricks): junctions between adapters are part of the design.
        var adapters = GuideConstruct.Parts("", construct)
            .Where(p => p.Kind is ConstructPartKind.BioBricks or ConstructPartKind.Frida)
            .ToList();
        var expected = new[] { "5'", "3'" }
            .Select(side => OligoExport.ToDna(string.Concat(adapters.Where(p => p.Label.EndsWith(side)).Select(p => p.Rna))))
            .ToList();
        return Enzymes.Where(e => CountSites(full, e.Site) > expected.Sum(a => CountSites(a, e.Site)))
            .Select(e => e.Name)
            .ToList();
    }

    /// <summary>Occurrences of a site on both strands of the DNA (overlaps counted).</summary>
    private static int CountSites(string dna, string site)
    {
        static int Count(string s, string site)
        {
            var n = 0;
            for (var i = s.IndexOf(site, StringComparison.Ordinal); i >= 0; i = s.IndexOf(site, i + 1, StringComparison.Ordinal)) n++;
            return n;
        }

        return Count(dna, site) + Count(OligoExport.ReverseComplement(dna), site);
    }

    public static string Warning(string enzyme) =>
        $"Warning: This DNA sequence includes a restriction site for the enzyme: '{enzyme}'";
}
