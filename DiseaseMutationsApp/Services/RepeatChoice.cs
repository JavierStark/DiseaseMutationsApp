using gRNA;

namespace DiseaseMutationsApp.Services;

/// <summary>Which 5' context the spacers are folded with while screening.</summary>
public enum RepeatMode { Default, None, Custom }

/// <summary>
/// The screening's direct-repeat choice, kept as one string wherever it travels: <c>null</c> = the default Cas13 scaffold,
/// <c>""</c> = no repeat (the spacer alone), anything else = a normalised RNA repeat. The same convention is used by
/// <see cref="RunParameters"/>, <see cref="IGrnaAnalysis"/> and the F# library.
/// </summary>
public static class RepeatChoice
{
    public const string DefaultToken = "default";
    public const string NoneToken = "none";

    /// <summary>The value to pass on for a form choice. Throws <see cref="GrnaInputException"/> for an unusable custom repeat.</summary>
    public static string? Resolve(RepeatMode mode, string? custom) => mode switch
    {
        RepeatMode.None => "",
        RepeatMode.Custom => RequireCustom(custom),
        _ => null
    };

    /// <summary>Why the form choice cannot be used, or null when it can.</summary>
    public static string? Validate(RepeatMode mode, string? custom)
    {
        try { Resolve(mode, custom); return null; }
        catch (GrnaInputException ex) { return ex.Message; }
    }

    private static string RequireCustom(string? custom)
    {
        var normalised = SpacerFinder.normaliseRepeat(custom ?? "");
        if (normalised.Length == 0) throw new GrnaInputException("Type your direct repeat, or pick another option.", Microsoft.FSharp.Core.FSharpOption<Exception>.None);
        return normalised;
    }

    /// <summary>Compact text for the session files, the permalink and the CSV: "default", "none" or the sequence.</summary>
    public static string ToToken(RepeatMode mode, string? custom) => mode switch
    {
        RepeatMode.None => NoneToken,
        RepeatMode.Custom => Validate(mode, custom) is null ? SpacerFinder.normaliseRepeat(custom ?? "") : DefaultToken,
        _ => DefaultToken
    };

    /// <summary>Reads a token back into a form choice; anything unusable becomes the default. Never throws.</summary>
    public static (RepeatMode Mode, string? Custom) FromToken(string? token)
    {
        var t = (token ?? "").Trim();
        if (t.Length == 0 || t.Equals(DefaultToken, StringComparison.OrdinalIgnoreCase)) return (RepeatMode.Default, null);
        if (t.Equals(NoneToken, StringComparison.OrdinalIgnoreCase)) return (RepeatMode.None, null);
        try
        {
            var normalised = SpacerFinder.normaliseRepeat(t);
            return normalised.Length == 0 ? (RepeatMode.Default, null) : (RepeatMode.Custom, normalised);
        }
        catch (GrnaInputException)
        {
            return (RepeatMode.Default, null);
        }
    }

    /// <summary>Short words for the UI: what the screening fold was made with.</summary>
    public static string Describe(string? repeat) => repeat switch
    {
        null => $"the LwaCas13a DR ({SpacerFinder.scaffold.Length} nt)",
        "" => "no direct repeat (the spacer alone)",
        _ => $"your custom repeat ({repeat.Length} nt)"
    };
}
