using gRNA;

namespace DiseaseMutationsAppTests;

/// <summary>
/// The properties that make combinatorial pooling actually work. These hold for every model
/// and every (V, K), and are what a hand-built layout is most likely to get subtly wrong.
/// </summary>
public class PoolingInvariantTests
{
    private static readonly Pooling.PoolingModel[] Models = Pooling.allModels.ToArray();

    private static IEnumerable<TestCaseData> Cases()
    {
        (int V, int K)[] combos =
        {
            (1, 5), (4, 5), (5, 5), (12, 5), (25, 5), (26, 5), (100, 5), (137, 5),
            (100, 4), (100, 3), (100, 2), (100, 1), (60, 7), (200, 5), (5, 25)
        };

        foreach (var (v, k) in combos)
        {
            foreach (var model in Models)
            {
                yield return new TestCaseData(model, v, k)
                    .SetName($"{{m}}_{Pooling.modelName(model).Replace(' ', '_')}_V{v}_K{k}");
            }
        }
    }

    /// <summary>
    /// Every guide must land in exactly R wells. Too few and it is unscreened; too many and
    /// the model's own well-count formula no longer describes what was built.
    /// </summary>
    [TestCaseSource(nameof(Cases))]
    public void BuildPlan_EveryGuideAppearsExactlyRepetitionsTimes(Pooling.PoolingModel model, int v, int k)
    {
        var plan = Pooling.buildPlan(model, Pooling.plate96, v, k);
        var expected = Pooling.repetitions(model);

        var occurrences = new int[v + 1];
        foreach (var pool in plan.Pools)
        {
            foreach (var guide in pool.GuideIndices)
            {
                occurrences[guide]++;
            }
        }

        var wrong = Enumerable.Range(1, v).Where(g => occurrences[g] != expected).ToList();
        Assert.That(wrong, Is.Empty, $"Guides appearing other than {expected} times: {string.Join(", ", wrong.Take(10))}");
    }

    /// <summary>No pool may exceed K: that is the tandem-transcript limit, a hard constraint.</summary>
    [TestCaseSource(nameof(Cases))]
    public void BuildPlan_NoPoolExceedsWellCapacity(Pooling.PoolingModel model, int v, int k)
    {
        var plan = Pooling.buildPlan(model, Pooling.plate96, v, k);
        Assert.That(plan.Pools.Max(p => p.GuideIndices.Length), Is.LessThanOrEqualTo(k));
    }

    /// <summary>Only guides 1..V may appear, and none may be repeated inside a single pool.</summary>
    [TestCaseSource(nameof(Cases))]
    public void BuildPlan_PoolsContainOnlyValidDistinctGuides(Pooling.PoolingModel model, int v, int k)
    {
        var plan = Pooling.buildPlan(model, Pooling.plate96, v, k);

        Assert.Multiple(() =>
        {
            foreach (var pool in plan.Pools)
            {
                var guides = pool.GuideIndices.ToList();
                Assert.That(guides, Is.All.InRange(1, v), $"Pool {pool.Id} contains an out-of-range guide.");
                Assert.That(guides.Distinct().Count(), Is.EqualTo(guides.Count), $"Pool {pool.Id} repeats a guide.");
            }
        });
    }

    /// <summary>The built layout must cost exactly what the comparison table promised.</summary>
    [TestCaseSource(nameof(Cases))]
    public void BuildPlan_PoolCountMatchesWellsForModel(Pooling.PoolingModel model, int v, int k)
    {
        var plan = Pooling.buildPlan(model, Pooling.plate96, v, k);
        Assert.That(plan.TotalWells, Is.EqualTo(Pooling.wellsForModel(model, v, k)));
    }

    /// <summary>
    /// Decodability: no two guides may sit in the same set of wells, or a positive readout
    /// could never tell them apart and the whole scheme collapses.
    /// </summary>
    [TestCaseSource(nameof(Cases))]
    public void BuildPlan_EveryGuideHasAUniqueWellSignature(Pooling.PoolingModel model, int v, int k)
    {
        var plan = Pooling.buildPlan(model, Pooling.plate96, v, k);

        var signatures = new Dictionary<int, List<int>>();
        for (var g = 1; g <= v; g++)
        {
            signatures[g] = new List<int>();
        }

        foreach (var pool in plan.Pools)
        {
            foreach (var guide in pool.GuideIndices)
            {
                signatures[guide].Add(pool.Id);
            }
        }

        var distinct = signatures.Values.Select(s => string.Join(",", s)).Distinct().Count();
        Assert.That(distinct, Is.EqualTo(v), "Two guides share the same set of wells and cannot be told apart.");
    }

    /// <summary>
    /// For the two-dimensional models, two guides share at most one well: they can agree on
    /// their row or their column, never both. This does NOT hold in 3D, where two guides can
    /// agree on two of three coordinates, which is why 3D needs a third repetition to stay
    /// decodable — hence this test covers the 2D models only.
    /// </summary>
    [TestCase(100, 5)]
    [TestCase(137, 5)]
    [TestCase(100, 3)]
    [TestCase(60, 7)]
    public void BuildPlan_TwoDimensionalModels_NoGuidePairSharesMoreThanOneWell(int v, int k)
    {
        Pooling.PoolingModel[] twoDModels =
        {
            Pooling.PoolingModel.TwoDFragmented,
            Pooling.PoolingModel.TwoDMatrix
        };

        Assert.Multiple(() =>
        {
            foreach (var model in twoDModels)
            {
                var plan = Pooling.buildPlan(model, Pooling.plate96, v, k);
                var shared = new Dictionary<(int, int), int>();

                foreach (var pool in plan.Pools)
                {
                    var guides = pool.GuideIndices.ToList();
                    for (var i = 0; i < guides.Count; i++)
                    {
                        for (var j = i + 1; j < guides.Count; j++)
                        {
                            var key = (Math.Min(guides[i], guides[j]), Math.Max(guides[i], guides[j]));
                            shared[key] = shared.TryGetValue(key, out var count) ? count + 1 : 1;
                        }
                    }
                }

                var offenders = shared.Where(kv => kv.Value > 1).Select(kv => kv.Key).Take(5).ToList();
                Assert.That(offenders, Is.Empty, $"{Pooling.modelName(model)}: guide pairs sharing >1 well: {string.Join(", ", offenders)}");
            }
        });
    }

    /// <summary>Pools are numbered 1..N contiguously and each maps to its own well.</summary>
    [TestCaseSource(nameof(Cases))]
    public void BuildPlan_PoolIdsAreContiguousAndAddressed(Pooling.PoolingModel model, int v, int k)
    {
        var plan = Pooling.buildPlan(model, Pooling.plate96, v, k);
        var pools = plan.Pools.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(pools.Select(p => p.Id), Is.EqualTo(Enumerable.Range(1, pools.Count)));
            Assert.That(pools.Select(p => p.Well.Label).Distinct().Count(), Is.EqualTo(pools.Count));
            Assert.That(pools.Where(p => string.IsNullOrWhiteSpace(p.Name)), Is.Empty);
        });
    }

    /// <summary>NonEmptyWells is what the researcher actually has to prepare.</summary>
    [TestCaseSource(nameof(Cases))]
    public void BuildPlan_NonEmptyWellCountIsConsistent(Pooling.PoolingModel model, int v, int k)
    {
        var plan = Pooling.buildPlan(model, Pooling.plate96, v, k);
        var expected = plan.Pools.Count(p => p.GuideIndices.Length > 0);

        Assert.Multiple(() =>
        {
            Assert.That(plan.NonEmptyWells, Is.EqualTo(expected));
            Assert.That(plan.NonEmptyWells, Is.LessThanOrEqualTo(plan.TotalWells));
        });
    }
}
