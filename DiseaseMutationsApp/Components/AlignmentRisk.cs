namespace DiseaseMutationsApp.Components;

/// <summary>
/// Labels a Bowtie alignment count. The count saturates at -k 6, so "6" really means "6 or more";
/// the label says so rather than letting 6 and 600 look alike. State is carried by text and glyph shape,
/// with colour only as reinforcement.
/// </summary>
public static class AlignmentRisk
{
    public const int SaturationLimit = 6;

    public static (string CssClass, string Label) Describe(int alignments) => alignments switch
    {
        <= 0 => ("risk-none", "0 none"),
        1 => ("risk-unique", "1 unique"),
        <= 3 => ("risk-caution", $"{alignments} caution"),
        < SaturationLimit => ("risk-high", $"{alignments} high"),
        _ => ("risk-saturated", $"{SaturationLimit}+ saturated")
    };
}
