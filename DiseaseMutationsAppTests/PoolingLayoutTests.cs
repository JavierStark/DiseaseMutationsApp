using gRNA;

namespace DiseaseMutationsAppTests;

/// <summary>
/// Exact layout fixtures. The 2D fragmented expectations are decoded from the LET array
/// formula in Combinatoria.xlsx, sheet "Distribucion_2D_frag" cell C5, and cross-checked
/// against the mixture names in column B and the well labels in column D.
/// </summary>
public class PoolingLayoutTests
{
    private static List<Pooling.Pool> Build(Pooling.PoolingModel model, int v, int k) =>
        Pooling.buildPlan(model, Pooling.plate96, v, k).Pools.ToList();

    /// <summary>
    /// NUnit compares an FSharpList by structural equality against the expected array's type
    /// rather than element-wise, so materialise the guides before asserting on them.
    /// </summary>
    private static List<int> Guides(Pooling.Pool pool) => pool.GuideIndices.ToList();

    [Test]
    public void BuildPlan_TwoDFragmented_V100_K5_MatchesWorkbookRows()
    {
        var pools = Build(Pooling.PoolingModel.TwoDFragmented, 100, 5);

        Assert.Multiple(() =>
        {
            Assert.That(pools, Has.Count.EqualTo(40));

            // Workbook row 5: tube 1, "Bloque 1 - Fila 1", guides 1..5, well Placa 1 - A1.
            Assert.That(pools[0].Id, Is.EqualTo(1));
            Assert.That(pools[0].Name, Is.EqualTo("Block 1 - Row 1"));
            Assert.That(Guides(pools[0]), Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
            Assert.That(pools[0].Well.Label, Is.EqualTo("Plate 1 - A1"));

            // Workbook row 10: tube 6, first column pool of block 1.
            Assert.That(pools[5].Name, Is.EqualTo("Block 1 - Column 1"));
            Assert.That(Guides(pools[5]), Is.EqualTo(new[] { 1, 6, 11, 16, 21 }));
            Assert.That(pools[5].Well.Label, Is.EqualTo("Plate 1 - A6"));

            // Workbook row 15: tube 11, block 2 starts.
            Assert.That(pools[10].Name, Is.EqualTo("Block 2 - Row 1"));
            Assert.That(Guides(pools[10]), Is.EqualTo(new[] { 26, 27, 28, 29, 30 }));
            Assert.That(pools[10].Well.Label, Is.EqualTo("Plate 1 - A11"));

            // Last block ends on guide 100 exactly.
            Assert.That(pools[39].Name, Is.EqualTo("Block 4 - Column 5"));
            Assert.That(Guides(pools[39]), Is.EqualTo(new[] { 80, 85, 90, 95, 100 }));
        });
    }

    /// <summary>
    /// A ragged block leaves reserved-but-empty tubes: the workbook prints
    /// "Vacio - No preparar" for these, and the plan must expose them as empty pools
    /// rather than silently dropping them, or the tube numbering would shift.
    /// </summary>
    [Test]
    public void BuildPlan_TwoDFragmented_RaggedBlock_KeepsEmptyPools()
    {
        var pools = Build(Pooling.PoolingModel.TwoDFragmented, 12, 5);

        Assert.Multiple(() =>
        {
            Assert.That(pools, Has.Count.EqualTo(10));
            Assert.That(Guides(pools[0]), Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
            Assert.That(Guides(pools[1]), Is.EqualTo(new[] { 6, 7, 8, 9, 10 }));
            Assert.That(Guides(pools[2]), Is.EqualTo(new[] { 11, 12 }));
            Assert.That(Guides(pools[3]), Is.Empty);
            Assert.That(Guides(pools[4]), Is.Empty);
            Assert.That(Guides(pools[5]), Is.EqualTo(new[] { 1, 6, 11 }));
            Assert.That(Guides(pools[6]), Is.EqualTo(new[] { 2, 7, 12 }));
            Assert.That(Guides(pools[7]), Is.EqualTo(new[] { 3, 8 }));
        });
    }

    [Test]
    public void BuildPlan_ReportsWellCounts()
    {
        var plan = Pooling.buildPlan(Pooling.PoolingModel.TwoDFragmented, Pooling.plate96, 12, 5);

        Assert.Multiple(() =>
        {
            Assert.That(plan.TotalWells, Is.EqualTo(10));
            Assert.That(plan.NonEmptyWells, Is.EqualTo(8));
            Assert.That(plan.MaxPoolSize, Is.EqualTo(5));
            Assert.That(plan.GuideCount, Is.EqualTo(12));
            Assert.That(plan.WellCapacity, Is.EqualTo(5));
        });
    }

    [Test]
    public void BuildPlan_TwoDMatrix_V9_K3_LaysOutSquareGrid()
    {
        var pools = Build(Pooling.PoolingModel.TwoDMatrix, 9, 3);

        Assert.Multiple(() =>
        {
            // s = 3, one chunk per row/column, so 3 rows + 3 columns = 6 pools.
            Assert.That(pools, Has.Count.EqualTo(6));
            Assert.That(pools[0].Name, Is.EqualTo("Row 1"));
            Assert.That(Guides(pools[0]), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(Guides(pools[2]), Is.EqualTo(new[] { 7, 8, 9 }));
            Assert.That(pools[3].Name, Is.EqualTo("Column 1"));
            Assert.That(Guides(pools[3]), Is.EqualTo(new[] { 1, 4, 7 }));
            Assert.That(Guides(pools[5]), Is.EqualTo(new[] { 3, 6, 9 }));
        });
    }

    /// <summary>When a row is longer than K it must be split, and the name must say so.</summary>
    [Test]
    public void BuildPlan_TwoDMatrix_RowLongerThanCapacity_SplitsIntoNamedParts()
    {
        var pools = Build(Pooling.PoolingModel.TwoDMatrix, 100, 5);

        Assert.Multiple(() =>
        {
            // s = 10, ceil(10/5) = 2 parts per row and per column: 2 * 10 * 2 = 40.
            Assert.That(pools, Has.Count.EqualTo(40));
            Assert.That(pools[0].Name, Is.EqualTo("Row 1 (part 1/2)"));
            Assert.That(Guides(pools[0]), Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
            Assert.That(pools[1].Name, Is.EqualTo("Row 1 (part 2/2)"));
            Assert.That(Guides(pools[1]), Is.EqualTo(new[] { 6, 7, 8, 9, 10 }));
            Assert.That(pools[20].Name, Is.EqualTo("Column 1 (part 1/2)"));
            Assert.That(Guides(pools[20]), Is.EqualTo(new[] { 1, 11, 21, 31, 41 }));
        });
    }

    [Test]
    public void BuildPlan_ThreeD_V8_K4_SlicesCubeOnThreeAxes()
    {
        var pools = Build(Pooling.PoolingModel.ThreeD, 8, 4);

        Assert.Multiple(() =>
        {
            // c = 2, slice size 4, one chunk per slice: 3 axes * 2 slices = 6 pools.
            Assert.That(pools, Has.Count.EqualTo(6));
            Assert.That(pools[0].Name, Is.EqualTo("X-slice 1"));
            Assert.That(Guides(pools[0]), Is.EqualTo(new[] { 1, 2, 3, 4 }));
            Assert.That(pools[1].Name, Is.EqualTo("X-slice 2"));
            Assert.That(Guides(pools[1]), Is.EqualTo(new[] { 5, 6, 7, 8 }));
            Assert.That(pools[2].Name, Is.EqualTo("Y-slice 1"));
            Assert.That(Guides(pools[2]), Is.EqualTo(new[] { 1, 2, 5, 6 }));
            Assert.That(pools[4].Name, Is.EqualTo("Z-slice 1"));
            Assert.That(Guides(pools[4]), Is.EqualTo(new[] { 1, 3, 5, 7 }));
        });
    }

    [Test]
    public void BuildPlan_RejectsInvalidInput()
    {
        Assert.Throws<ArgumentException>(
            () => Pooling.buildPlan(Pooling.PoolingModel.TwoDFragmented, Pooling.plate96, 0, 5));
    }
}

