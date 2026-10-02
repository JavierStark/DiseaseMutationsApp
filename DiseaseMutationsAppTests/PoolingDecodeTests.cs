using DiseaseMutationsApp.Services;
using Microsoft.FSharp.Collections;
using gRNA;

namespace DiseaseMutationsAppTests;

public class PoolingDecodeTests
{
    private static readonly Pooling.PoolingModel[] Models = Pooling.allModels.ToArray();

    private static IEnumerable<TestCaseData> Cases()
    {
        foreach (var (v, k) in new[] { (1, 5), (12, 5), (26, 5), (100, 5), (137, 5), (100, 1), (60, 7) })
            foreach (var m in Models)
                yield return new TestCaseData(m, v, k).SetName($"{{m}}_{Pooling.modelName(m).Replace(' ', '_')}_V{v}_K{k}");
    }

    private static int[] PoolsOf(Pooling.PoolingPlan plan, int guide) =>
        plan.Pools.Where(p => p.GuideIndices.Contains(guide)).Select(p => p.Id).ToArray();

    private static FSharpList<int> L(IEnumerable<int> xs) => ListModule.OfSeq(xs);

    [TestCaseSource(nameof(Cases))]
    public void Decode_SingleGuide_IsRecoveredExactly(Pooling.PoolingModel model, int v, int k)
    {
        var plan = Pooling.buildPlan(model, Pooling.plate384, v, k);
        for (var g = 1; g <= v; g++)
        {
            var result = Pooling.decode(plan, L(PoolsOf(plan, g)));
            Assert.Multiple(() =>
            {
                Assert.That(result.Implicated.ToArray(), Is.EqualTo(new[] { g }), $"guide {g}");
                Assert.That(result.Definite.ToArray(), Is.EqualTo(new[] { g }));
                Assert.That(result.IsAmbiguous, Is.False);
                Assert.That(result.UnexplainedPools, Is.Empty);
            });
        }
    }

    [Test]
    public void Decode_TwoHits_ReportsCollisionAmbiguity()
    {
        // 25 guides, K=5, 2D fragmented: one 5x5 block, guide = row*5+col. Hits at (0,0) and (1,1)
        // light rows 0,1 and cols 0,1, which also implicate the innocent guides (0,1) and (1,0).
        var plan = Pooling.buildPlan(Pooling.PoolingModel.TwoDFragmented, Pooling.plate96, 25, 5);
        var hits = new[] { 1, 7 }; // 1-based indices of cells (0,0) and (1,1)
        var positives = hits.SelectMany(h => PoolsOf(plan, h)).Distinct();

        var result = Pooling.decode(plan, L(positives));

        Assert.Multiple(() =>
        {
            Assert.That(result.Implicated.Count(), Is.EqualTo(4));
            Assert.That(result.Implicated.ToArray(), Is.SupersetOf(hits));
            Assert.That(result.IsAmbiguous, Is.True);
            Assert.That(result.Ambiguous.Count(), Is.EqualTo(4));
        });
    }

    [Test]
    public void Decode_PartialPattern_ReportsPartialSupport()
    {
        var plan = Pooling.buildPlan(Pooling.PoolingModel.TwoDMatrix, Pooling.plate96, 25, 5);
        var pools = PoolsOf(plan, 1);
        var result = Pooling.decode(plan, L(pools.Take(1)));

        Assert.Multiple(() =>
        {
            Assert.That(result.Implicated, Is.Empty);
            Assert.That(result.PartiallySupported.ToArray(), Does.Contain(1));
            Assert.That(result.UnexplainedPools.ToArray(), Is.EqualTo(pools.Take(1).ToArray()));
        });
    }

    [Test]
    public void Decode_UnknownAndDuplicatePools_AreHandled()
    {
        var plan = Pooling.buildPlan(Pooling.PoolingModel.TwoDFragmented, Pooling.plate96, 10, 5);
        var pools = PoolsOf(plan, 1);
        var result = Pooling.decode(plan, L(pools.Concat(pools).Concat(new[] { 9999 })));

        Assert.Multiple(() =>
        {
            Assert.That(result.UnknownPools.ToArray(), Is.EqualTo(new[] { 9999 }));
            Assert.That(result.Implicated.ToArray(), Is.EqualTo(new[] { 1 }));
        });
    }

    [Test]
    public void Decode_NoPositives_ImplicatesNothing()
    {
        var plan = Pooling.buildPlan(Pooling.PoolingModel.ThreeD, Pooling.plate96, 30, 5);
        var result = Pooling.decode(plan, FSharpList<int>.Empty);
        Assert.Multiple(() =>
        {
            Assert.That(result.Implicated, Is.Empty);
            Assert.That(result.IsAmbiguous, Is.False);
        });
    }
}

public class PoolingServiceDecodeTests
{
    private static PoolingService Svc() =>
        new(Microsoft.Extensions.Logging.Abstractions.NullLogger<PoolingService>.Instance);

    [Test]
    public void Decode_WellLabels_IdentifyTheGuide()
    {
        var svc = Svc();
        var plan = svc.BuildPlan(PoolingModelKind.TwoDFragmented, 25, 5, PlateKind.Plate96);
        var wells = plan.Pools.Where(p => p.GuideIndices.Contains(7)).Select(p => p.WellLabel).ToList();
        // Mix label styles: full label and short form.
        var shortForm = $"{plan.Pools.First(p => p.WellLabel == wells[0]).Row}{plan.Pools.First(p => p.WellLabel == wells[0]).Column}";
        var text = shortForm + ", " + wells[1];

        var outcome = svc.Decode(plan, text);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Implicated.Select(g => g.Index), Is.EqualTo(new[] { 7 }));
            Assert.That(outcome.Unrecognised, Is.Empty);
            Assert.That(outcome.IsAmbiguous, Is.False);
        });
    }

    [Test]
    public void Decode_TubeIds_AndGarbage()
    {
        var svc = Svc();
        var plan = svc.BuildPlan(PoolingModelKind.TwoDMatrix, 25, 5, PlateKind.Plate96);
        var ids = plan.Pools.Where(p => p.GuideIndices.Contains(1)).Select(p => p.Id).ToList();

        var outcome = svc.Decode(plan, $"#{ids[0]}; {ids[1]}\nnonsense");

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Implicated.Select(g => g.Index), Is.EqualTo(new[] { 1 }));
            Assert.That(outcome.Unrecognised, Is.EqualTo(new[] { "nonsense" }));
        });
    }

    [Test]
    public void Decode_EmptyInput_ImplicatesNothing()
    {
        var svc = Svc();
        var plan = svc.BuildPlan(PoolingModelKind.ThreeD, 30, 5, PlateKind.Plate96);
        Assert.That(svc.Decode(plan, null).Implicated, Is.Empty);
    }
}
