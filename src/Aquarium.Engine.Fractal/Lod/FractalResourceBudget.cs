using Aquarium.Engine.Fractal;

namespace Aquarium.Engine.Fractal.Lod;

public readonly record struct FractalResourceBudget(
    int MaxCpuUpdates,
    float MaxGpuEstimatedCost,
    int MaxResidentPayloads,
    int MaxSsdRequests)
{
    public static FractalResourceBudget Empty { get; } = new(0, 0.0f, 0, 0);

    public void Validate()
    {
        if (MaxCpuUpdates < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxCpuUpdates), MaxCpuUpdates, "CPU update budget must not be negative.");
        }

        if (MaxGpuEstimatedCost < 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxGpuEstimatedCost), MaxGpuEstimatedCost, "GPU packet budget must not be negative.");
        }

        if (MaxResidentPayloads < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxResidentPayloads), MaxResidentPayloads, "Resident payload budget must not be negative.");
        }

        if (MaxSsdRequests < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSsdRequests), MaxSsdRequests, "SSD request budget must not be negative.");
        }
    }
}

public sealed class FractalResourcePlan
{
    public FractalResourcePlan(
        AquariumSelectedCut[] selectedCut,
        AquariumFractalKey[] updateNodes,
        FractalResidencyPlan residency,
        float gpuEstimatedCost,
        FractalResourceBudget budget)
    {
        SelectedCut = selectedCut;
        UpdateNodes = updateNodes;
        Residency = residency;
        GpuEstimatedCost = gpuEstimatedCost;
        Budget = budget;
    }

    public AquariumSelectedCut[] SelectedCut { get; }

    public AquariumFractalKey[] UpdateNodes { get; }

    public FractalResidencyPlan Residency { get; }

    public float GpuEstimatedCost { get; }

    public FractalResourceBudget Budget { get; }
}
