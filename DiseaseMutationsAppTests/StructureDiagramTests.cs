using DiseaseMutationsApp.Services;

namespace DiseaseMutationsAppTests;

public class StructureDiagramTests
{
    [Test]
    public void BasePairs_MatchesNestedBrackets()
    {
        var pairs = StructureDiagram.BasePairs("((.(..)))");
        Assert.That(pairs, Is.EquivalentTo(new[] { (0, 8), (1, 7), (3, 6) }));
    }

    [Test]
    public void BasePairs_IgnoresUnmatchedBrackets()
    {
        Assert.That(StructureDiagram.BasePairs(".(.))(."), Is.EqualTo(new[] { (1, 3) }));
    }

    [Test]
    public void ArcPath_JoinsPairWithSemicircleOnBaseline()
    {
        var x0 = StructureDiagram.X(0);
        var x2 = StructureDiagram.X(2);
        var y = StructureDiagram.Baseline(3);
        var r = (x2 - x0) / 2;
        var expected = $"M{x0:0.##} {y:0.##} A{r:0.##} {r:0.##} 0 0 1 {x2:0.##} {y:0.##}";
        Assert.That(StructureDiagram.ArcPath((0, 2), 3), Is.EqualTo(expected));
    }
}
