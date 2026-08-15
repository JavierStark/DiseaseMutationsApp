using gRNA;

namespace DiseaseMutationsAppTests;

/// <summary>
/// Regression fixtures for the well-count formulas. Every expected value below is taken
/// from Combinatoria.xlsx, which is the authoritative specification for this feature:
/// sheet "Esquema_softwareCompuestos_(V)" cells D2:F2, and sheet "Pruebas_manuales" row 4.
/// </summary>
public class PoolingMathTests
{
    // V, K, 2D fragmented, 2D matrix, 3D
    [TestCase(100, 5, 40, 40, 75, TestName = "WellsForModel_V100_K5_MatchesWorkbook")]
    [TestCase(5, 5, 10, 6, 6, TestName = "WellsForModel_V5_K5_MatchesWorkbook")]
    [TestCase(5, 4, 8, 6, 6, TestName = "WellsForModel_V5_K4_MatchesWorkbook")]
    [TestCase(5, 3, 6, 6, 12, TestName = "WellsForModel_V5_K3_MatchesWorkbook")]
    [TestCase(5, 2, 8, 12, 12, TestName = "WellsForModel_V5_K2_MatchesWorkbook")]
    [TestCase(5, 25, 50, 6, 6, TestName = "WellsForModel_V5_K25_MatchesWorkbook")]
    public void WellsForModel_MatchesWorkbook(int v, int k, int twoDFrag, int twoDMatrix, int threeD)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Pooling.wellsForModel(Pooling.PoolingModel.TwoDFragmented, v, k), Is.EqualTo(twoDFrag));
            Assert.That(Pooling.wellsForModel(Pooling.PoolingModel.TwoDMatrix, v, k), Is.EqualTo(twoDMatrix));
            Assert.That(Pooling.wellsForModel(Pooling.PoolingModel.ThreeD, v, k), Is.EqualTo(threeD));
        });
    }

    [Test]
    public void CeilSqrt_PerfectSquares_DoesNotOvershoot()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Pooling.ceilSqrt(1), Is.EqualTo(1));
            Assert.That(Pooling.ceilSqrt(4), Is.EqualTo(2));
            Assert.That(Pooling.ceilSqrt(100), Is.EqualTo(10));
            Assert.That(Pooling.ceilSqrt(10000), Is.EqualTo(100));
        });
    }

    [Test]
    public void CeilSqrt_NonSquares_RoundsUp()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Pooling.ceilSqrt(2), Is.EqualTo(2));
            Assert.That(Pooling.ceilSqrt(5), Is.EqualTo(3));
            Assert.That(Pooling.ceilSqrt(101), Is.EqualTo(11));
        });
    }

    /// <summary>
    /// The workbook uses ROUNDUP(V^(1/3),0). Math.Pow puts 64^(1/3) at 3.9999999999999996,
    /// so the float route only happens to work because the error falls the right way.
    /// These assertions pin the exact behaviour.
    /// </summary>
    [Test]
    public void CeilCbrt_PerfectCubes_DoesNotOvershoot()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Pooling.ceilCbrt(1), Is.EqualTo(1));
            Assert.That(Pooling.ceilCbrt(8), Is.EqualTo(2));
            Assert.That(Pooling.ceilCbrt(27), Is.EqualTo(3));
            Assert.That(Pooling.ceilCbrt(64), Is.EqualTo(4));
            Assert.That(Pooling.ceilCbrt(125), Is.EqualTo(5));
            Assert.That(Pooling.ceilCbrt(1000), Is.EqualTo(10));
            Assert.That(Pooling.ceilCbrt(27000), Is.EqualTo(30));
        });
    }

    [Test]
    public void CeilCbrt_NonCubes_RoundsUp()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Pooling.ceilCbrt(5), Is.EqualTo(2));
            Assert.That(Pooling.ceilCbrt(9), Is.EqualTo(3));
            Assert.That(Pooling.ceilCbrt(1001), Is.EqualTo(11));
        });
    }

    [Test]
    public void CeilDiv_RoundsUpAndHandlesExactDivision()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Pooling.ceilDiv(10, 5), Is.EqualTo(2));
            Assert.That(Pooling.ceilDiv(11, 5), Is.EqualTo(3));
            Assert.That(Pooling.ceilDiv(1, 5), Is.EqualTo(1));
        });
    }

    [Test]
    public void CompareModels_OrdersByWellsThenByModelPreference()
    {
        var estimates = Pooling.compareModels(100, 5).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(estimates, Has.Count.EqualTo(3));
            // 2D Fragmented and 2D Matrix both cost 40; the tie must break towards 2D Fragmented,
            // matching the nested IF in the workbook's "Modelo + optimo" cell.
            Assert.That(estimates[0].Model, Is.EqualTo(Pooling.PoolingModel.TwoDFragmented));
            Assert.That(estimates[0].Wells, Is.EqualTo(40));
            Assert.That(estimates[1].Model, Is.EqualTo(Pooling.PoolingModel.TwoDMatrix));
            Assert.That(estimates[1].Wells, Is.EqualTo(40));
            Assert.That(estimates[2].Model, Is.EqualTo(Pooling.PoolingModel.ThreeD));
            Assert.That(estimates[2].Wells, Is.EqualTo(75));
        });
    }

    [Test]
    public void CompareModels_ReportsRepetitionsPerModel()
    {
        var byModel = Pooling.compareModels(100, 5).ToDictionary(e => e.Model, e => e.Repetitions);

        Assert.Multiple(() =>
        {
            Assert.That(byModel[Pooling.PoolingModel.TwoDFragmented], Is.EqualTo(2));
            Assert.That(byModel[Pooling.PoolingModel.TwoDMatrix], Is.EqualTo(2));
            Assert.That(byModel[Pooling.PoolingModel.ThreeD], Is.EqualTo(3));
        });
    }

    [Test]
    public void BestModel_V100_K5_PicksTwoDFragmented()
    {
        Assert.That(Pooling.bestModel(100, 5), Is.EqualTo(Pooling.PoolingModel.TwoDFragmented));
    }

    /// <summary>The partner's document notes 2D Fragmented also wins at K=4 and K=3.</summary>
    [TestCase(100, 4)]
    [TestCase(100, 3)]
    public void BestModel_SmallK_PrefersTwoDFragmented(int v, int k)
    {
        Assert.That(Pooling.bestModel(v, k), Is.EqualTo(Pooling.PoolingModel.TwoDFragmented));
    }

    /// <summary>At a large K the fragmented model wastes whole blocks and loses.</summary>
    [Test]
    public void BestModel_LargeK_PrefersAnotherModel()
    {
        Assert.That(Pooling.bestModel(5, 25), Is.Not.EqualTo(Pooling.PoolingModel.TwoDFragmented));
    }

    [TestCase(0, 5)]
    [TestCase(-1, 5)]
    [TestCase(10, 0)]
    [TestCase(10, -3)]
    [TestCase(200_000, 5)]
    public void Validate_RejectsOutOfRangeInput(int v, int k)
    {
        Assert.Throws<ArgumentException>(() => Pooling.validate(v, k));
    }

    [Test]
    public void Validate_AcceptsTypicalInput()
    {
        Assert.DoesNotThrow(() => Pooling.validate(1200, 5));
    }
}
