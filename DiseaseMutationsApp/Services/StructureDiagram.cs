namespace DiseaseMutationsApp.Services;

/// <summary>Geometry for drawing an RNA secondary structure as an arc diagram, computed locally so no sequence leaves the page.</summary>
public static class StructureDiagram
{
    /// <summary>Horizontal distance between neighbouring nucleotides, in SVG user units.</summary>
    public const double Step = 14;
    private const double Margin = 12;
    private const double LabelGap = 16;

    /// <summary>Base pairs of a dot-bracket string as zero-based (5', 3') index pairs. Unmatched brackets are ignored.</summary>
    public static IReadOnlyList<(int Left, int Right)> BasePairs(string structure)
    {
        var pairs = new List<(int, int)>();
        var open = new Stack<int>();
        for (var i = 0; i < structure.Length; i++)
        {
            if (structure[i] == '(') open.Push(i);
            else if (structure[i] == ')' && open.Count > 0) pairs.Add((open.Pop(), i));
        }
        return pairs;
    }

    /// <summary>Horizontal centre of nucleotide <paramref name="index"/>.</summary>
    public static double X(int index) => Margin + index * Step + Step / 2;

    /// <summary>The viewBox width for a structure of <paramref name="length"/> nucleotides.</summary>
    public static double Width(int length) => 2 * Margin + Math.Max(1, length) * Step;

    /// <summary>Baseline y: the tallest possible arc (spanning the whole molecule) fits above it.</summary>
    public static double Baseline(int length) => Margin + Math.Max(0, length - 1) * Step / 2;

    /// <summary>The viewBox height; nucleotide letters sit below the baseline.</summary>
    public static double Height(int length) => Baseline(length) + LabelGap + Margin;

    /// <summary>SVG path for one base pair: a semicircular arc above the baseline joining its two nucleotides.</summary>
    public static string ArcPath((int Left, int Right) pair, int length)
    {
        var x1 = X(pair.Left);
        var x2 = X(pair.Right);
        var r = (x2 - x1) / 2;
        var y = Baseline(length);
        return FormattableString.Invariant($"M{x1:0.##} {y:0.##} A{r:0.##} {r:0.##} 0 0 1 {x2:0.##} {y:0.##}");
    }
}
