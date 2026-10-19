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
    public const string LwaId = gRNA.Construct.LwaId;
    public const string NoneId = gRNA.Construct.NoneId;

    public const string Frida5 = gRNA.Construct.Frida5;
    public const string Frida3 = gRNA.Construct.Frida3;
    public const string BioBricks5 = gRNA.Construct.BioBricks5;
    public const string BioBricks3 = gRNA.Construct.BioBricks3;

    /// <summary>Add further Cas13 direct repeats in <c>gRNA/Construct.fs</c> (data only) once their sequence is verified.</summary>
    public static readonly IReadOnlyList<DrPreset> DrPresets =
        gRNA.Construct.drPresets.Select(p => new DrPreset(p.Id, p.Name, p.Sequence)).ToArray();

    public static ConstructOptions Default { get; } = new();

    public static string ToRna(string s) => gRNA.Construct.toRna(s);

    /// <summary>Uppercase, whitespace removed.</summary>
    public static string Clean(string? s) => gRNA.Construct.clean(s ?? "");

    /// <summary>True when the custom text is empty or only A, C, G, T, U (case and spaces ignored).</summary>
    public static bool IsValidCustom(string? s) => gRNA.Construct.isValidCustom(s ?? "");

    public static DrPreset Dr(ConstructOptions o) =>
        DrPresets.FirstOrDefault(p => p.Id == o.DrId) ?? DrPresets[0];

    /// <summary>The F# options the shared library works with.</summary>
    public static gRNA.Construct.ConstructOptions ToLibrary(ConstructOptions? options)
    {
        var o = options ?? Default;
        return new gRNA.Construct.ConstructOptions(o.DrId, o.Frida, o.BioBricks, o.Custom5 ?? "", o.Custom3 ?? "");
    }

    /// <summary>The ordered pieces of the complete guide; invalid custom text is left out.</summary>
    public static IReadOnlyList<ConstructPart> Parts(string spacerRna, ConstructOptions? options = null) =>
        gRNA.Construct.parts(spacerRna, ToLibrary(options))
            .Select(p => new ConstructPart(Enum.Parse<ConstructPartKind>(p.Kind.ToString()), p.Label, p.Rna))
            .ToList();

    /// <summary>The complete guide as RNA letters.</summary>
    public static string Build(string spacerRna, ConstructOptions? options = null) =>
        gRNA.Construct.build(spacerRna, ToLibrary(options));

    /// <summary>Only the engineered adapters (Frida, BioBricks) around an empty spacer, for expected-site accounting.</summary>
    public static string AdaptersOnly(ConstructOptions? options = null) =>
        string.Concat(Parts("", options).Where(p => p.Kind is ConstructPartKind.BioBricks or ConstructPartKind.Frida).Select(p => p.Rna));

    /// <summary>Short description for file headers, e.g. "LwaCas13a DR + Frida + BioBricks".</summary>
    public static string Describe(ConstructOptions? options = null) =>
        gRNA.Construct.describe(ToLibrary(options));
}
