namespace DiseaseMutationsApp.Services;

/// <summary>A direct repeat (the constant handle before the spacer) the user can pick.</summary>
public record DrPreset(string Id, string Name, string Sequence);

/// <summary>What wraps the spacer: a direct repeat, the two engineered adapters and optional custom flanks.</summary>
/// <param name="DrId">Id of a <see cref="GuideConstruct.DrPresets"/> entry.</param>
/// <param name="Frida">Wrap in the Frida adapter.</param>
/// <param name="BioBricks">Wrap in the BioBricks assembly adapter (outside the Frida adapter when both are on).</param>
/// <param name="Custom5">Custom sequence placed 5' of the DR/spacer, inside the adapters.</param>
/// <param name="Custom3">Custom sequence placed 3' of the spacer, inside the adapters.</param>
public record ConstructOptions(
    string DrId = GuideConstruct.LwaId,
    bool Frida = false,
    bool BioBricks = false,
    string Custom5 = "",
    string Custom3 = "");

public enum ConstructPartKind { BioBricks, Frida, Custom, DirectRepeat, Spacer }

public record ConstructPart(ConstructPartKind Kind, string Label, string Rna);

/// <summary>
/// Builds the complete guide: <c>[BioBricks 5'][Frida 5'][custom 5'][DR] SPACER [custom 3'][Frida 3'][BioBricks 3']</c>.
/// Everything is held as RNA letters (U); adapters are DNA sequences by origin and are converted on use.
/// </summary>
public static class GuideConstruct
{
    public const string LwaId = "lwa";
    public const string NoneId = "none";

    public const string Frida5 = "AGAGCGTCTCTAAAC";
    public const string Frida3 = "GCTTAGAGACGTACT";
    public const string BioBricks5 = "GGCTAGCGAATTCGCGGCCGCTTCT";
    public const string BioBricks3 = "AGTAGCGGCCGCTGCAGCGATCGT";

    /// <summary>Add further Cas13 direct repeats here (data only) once their sequence is verified.</summary>
    public static readonly IReadOnlyList<DrPreset> DrPresets = new[]
    {
        new DrPreset(LwaId, "LwaCas13a", GrnaService.Scaffold),
        new DrPreset(NoneId, "None (no direct repeat)", ""),
    };

    public static ConstructOptions Default { get; } = new();

    public static string ToRna(string s) => Clean(s).Replace('T', 'U');

    /// <summary>Uppercase, whitespace removed.</summary>
    public static string Clean(string? s) =>
        new string((s ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();

    /// <summary>True when the custom text is empty or only A, C, G, T, U (case and spaces ignored).</summary>
    public static bool IsValidCustom(string? s) => Clean(s).All(c => "ACGTU".Contains(c));

    public static DrPreset Dr(ConstructOptions o) =>
        DrPresets.FirstOrDefault(p => p.Id == o.DrId) ?? DrPresets[0];

    /// <summary>The ordered pieces of the complete guide; invalid custom text is left out.</summary>
    public static IReadOnlyList<ConstructPart> Parts(string spacerRna, ConstructOptions? options = null)
    {
        var o = options ?? Default;
        var dr = Dr(o);
        var c5 = IsValidCustom(o.Custom5) ? ToRna(o.Custom5) : "";
        var c3 = IsValidCustom(o.Custom3) ? ToRna(o.Custom3) : "";

        var parts = new List<ConstructPart>();
        void Add(bool on, ConstructPartKind kind, string label, string rna)
        {
            if (on && rna.Length > 0) parts.Add(new ConstructPart(kind, label, rna));
        }

        Add(o.BioBricks, ConstructPartKind.BioBricks, "BioBricks adapter 5'", ToRna(BioBricks5));
        Add(o.Frida, ConstructPartKind.Frida, "Frida adapter 5'", ToRna(Frida5));
        Add(true, ConstructPartKind.Custom, "Custom 5'", c5);
        Add(true, ConstructPartKind.DirectRepeat, dr.Name + " DR", ToRna(dr.Sequence));
        parts.Add(new ConstructPart(ConstructPartKind.Spacer, "Spacer", ToRna(spacerRna)));
        Add(true, ConstructPartKind.Custom, "Custom 3'", c3);
        Add(o.Frida, ConstructPartKind.Frida, "Frida adapter 3'", ToRna(Frida3));
        Add(o.BioBricks, ConstructPartKind.BioBricks, "BioBricks adapter 3'", ToRna(BioBricks3));
        return parts;
    }

    /// <summary>The complete guide as RNA letters.</summary>
    public static string Build(string spacerRna, ConstructOptions? options = null) =>
        string.Concat(Parts(spacerRna, options).Select(p => p.Rna));

    /// <summary>Only the engineered adapters (Frida, BioBricks) around an empty spacer, for expected-site accounting.</summary>
    public static string AdaptersOnly(ConstructOptions? options = null)
    {
        var o = options ?? Default;
        return string.Concat(Parts("", o).Where(p => p.Kind is ConstructPartKind.BioBricks or ConstructPartKind.Frida).Select(p => p.Rna));
    }

    /// <summary>Short description for file headers, e.g. "LwaCas13a DR + Frida + BioBricks".</summary>
    public static string Describe(ConstructOptions? options = null)
    {
        var o = options ?? Default;
        var bits = new List<string>();
        var dr = Dr(o);
        bits.Add(dr.Sequence.Length > 0 ? dr.Name + " DR" : "no DR");
        if (o.Frida) bits.Add("Frida");
        if (o.BioBricks) bits.Add("BioBricks");
        if (Clean(o.Custom5).Length > 0 && IsValidCustom(o.Custom5)) bits.Add("custom 5'");
        if (Clean(o.Custom3).Length > 0 && IsValidCustom(o.Custom3)) bits.Add("custom 3'");
        return string.Join(" + ", bits);
    }
}
