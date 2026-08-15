using gRNA;

namespace DiseaseMutationsApp.Services;

/// <summary>
/// Service that provides access to the F# combinatorial pooling domain, mapping its records
/// onto C# DTOs so the UI never handles F# types directly.
/// </summary>
public class PoolingService
{
    private readonly ILogger<PoolingService> _logger;

    public PoolingService(ILogger<PoolingService> logger)
    {
        _logger = logger;
    }

    public int MaxGuideCount => Pooling.MaxGuideCount;

    /// <summary>Costs every model for this (V, K), cheapest first, flagging the winner.</summary>
    public List<PoolingModelEstimate> CompareModels(int guideCount, int wellCapacity)
    {
        try
        {
            var estimates = Pooling.compareModels(guideCount, wellCapacity).ToList();
            var cheapest = estimates.Count > 0 ? estimates[0].Wells : 0;

            return estimates
                .Select((e, index) => new PoolingModelEstimate
                {
                    Model = FromFsharp(e.Model),
                    Name = Pooling.modelName(e.Model),
                    Wells = e.Wells,
                    Repetitions = e.Repetitions,
                    Formula = e.Formula,
                    // Only the first of a tie is optimal: the domain has already applied the
                    // preference order, so a later model matching on wells is not the winner.
                    IsOptimal = index == 0 && e.Wells == cheapest
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error comparing pooling models for V={GuideCount}, K={WellCapacity}", guideCount, wellCapacity);
            throw;
        }
    }

    public PoolingModelKind BestModel(int guideCount, int wellCapacity) =>
        FromFsharp(Pooling.bestModel(guideCount, wellCapacity));

    /// <summary>
    /// Builds the full well-by-well distribution. <paramref name="guides"/> supplies the labels
    /// shown on the plate map; when it is null or short, guides fall back to "Guide n".
    /// </summary>
    public PoolingPlanDto BuildPlan(
        PoolingModelKind model,
        int guideCount,
        int wellCapacity,
        PlateKind plate,
        IReadOnlyList<GuideEntry>? guides = null)
    {
        try
        {
            _logger.LogInformation(
                "Building {Model} pooling plan for V={GuideCount}, K={WellCapacity}, plate={Plate}",
                model, guideCount, wellCapacity, plate);

            var format = ToFsharp(plate);
            var plan = Pooling.buildPlan(ToFsharp(model), format, guideCount, wellCapacity);

            var pools = plan.Pools
                .Select(p => new PoolWell
                {
                    Id = p.Id,
                    Name = p.Name,
                    GuideIndices = p.GuideIndices.ToList(),
                    GuideLabels = p.GuideIndices.Select(i => LabelFor(guides, i)).ToList(),
                    WellLabel = p.Well.Label,
                    Plate = p.Well.Plate,
                    Row = p.Well.Row,
                    Column = p.Well.Column
                })
                .ToList();

            return new PoolingPlanDto
            {
                Model = model,
                ModelName = Pooling.modelName(ToFsharp(model)),
                GuideCount = plan.GuideCount,
                WellCapacity = plan.WellCapacity,
                Repetitions = Pooling.repetitions(ToFsharp(model)),
                Pools = pools,
                TotalWells = plan.TotalWells,
                NonEmptyWells = plan.NonEmptyWells,
                MaxPoolSize = plan.MaxPoolSize,
                PlateRows = format.Rows,
                PlateColumns = format.Columns,
                PlateCount = pools.Count > 0 ? pools[^1].Plate : 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building pooling plan for V={GuideCount}, K={WellCapacity}", guideCount, wellCapacity);
            throw;
        }
    }

    private static string LabelFor(IReadOnlyList<GuideEntry>? guides, int oneBasedIndex)
    {
        if (guides is not null && oneBasedIndex >= 1 && oneBasedIndex <= guides.Count)
        {
            var label = guides[oneBasedIndex - 1].Label;
            if (!string.IsNullOrWhiteSpace(label))
            {
                return label;
            }
        }

        return $"Guide {oneBasedIndex}";
    }

    private static Pooling.PoolingModel ToFsharp(PoolingModelKind kind) => kind switch
    {
        PoolingModelKind.TwoDFragmented => Pooling.PoolingModel.TwoDFragmented,
        PoolingModelKind.TwoDMatrix => Pooling.PoolingModel.TwoDMatrix,
        PoolingModelKind.ThreeD => Pooling.PoolingModel.ThreeD,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown pooling model.")
    };

    private static PoolingModelKind FromFsharp(Pooling.PoolingModel model) => model.Tag switch
    {
        Pooling.PoolingModel.Tags.TwoDFragmented => PoolingModelKind.TwoDFragmented,
        Pooling.PoolingModel.Tags.TwoDMatrix => PoolingModelKind.TwoDMatrix,
        _ => PoolingModelKind.ThreeD
    };

    private static Pooling.PlateFormat ToFsharp(PlateKind plate) => plate switch
    {
        PlateKind.Plate96 => Pooling.plate96,
        PlateKind.Plate384 => Pooling.plate384,
        _ => throw new ArgumentOutOfRangeException(nameof(plate), plate, "Unknown plate format.")
    };
}

public enum PoolingModelKind
{
    TwoDFragmented,
    TwoDMatrix,
    ThreeD
}

public enum PlateKind
{
    Plate96,
    Plate384
}

public record PoolingModelEstimate
{
    public PoolingModelKind Model { get; init; }
    public required string Name { get; init; }

    /// <summary>N: wells the model needs.</summary>
    public int Wells { get; init; }

    /// <summary>R: wells each guide appears in.</summary>
    public int Repetitions { get; init; }

    public required string Formula { get; init; }
    public bool IsOptimal { get; init; }
}

public record PoolWell
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public required List<int> GuideIndices { get; init; }
    public required List<string> GuideLabels { get; init; }
    public required string WellLabel { get; init; }
    public int Plate { get; init; }
    public char Row { get; init; }
    public int Column { get; init; }

    /// <summary>A slot the layout reserves but that has nothing in it — do not prepare.</summary>
    public bool IsEmpty => GuideIndices.Count == 0;
}

public record PoolingPlanDto
{
    public PoolingModelKind Model { get; init; }
    public required string ModelName { get; init; }
    public int GuideCount { get; init; }
    public int WellCapacity { get; init; }
    public int Repetitions { get; init; }
    public required List<PoolWell> Pools { get; init; }
    public int TotalWells { get; init; }
    public int NonEmptyWells { get; init; }
    public int MaxPoolSize { get; init; }
    public int PlateRows { get; init; }
    public int PlateColumns { get; init; }
    public int PlateCount { get; init; }
}
