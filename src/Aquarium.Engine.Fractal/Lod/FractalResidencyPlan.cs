using Aquarium.Engine.Fractal;

namespace Aquarium.Engine.Fractal.Lod;

public sealed class FractalResidencyPlan
{
    public FractalResidencyPlan(
        IReadOnlyList<AquariumFractalKey> residentNodes,
        IReadOnlyList<AquariumFractalKey> summaryFallbackNodes,
        IReadOnlyList<AquariumFractalKey> requestedNodes,
        IReadOnlyList<AquariumFractalKey>? evictedNodes = null)
    {
        ResidentNodes = residentNodes;
        SummaryFallbackNodes = summaryFallbackNodes;
        RequestedNodes = requestedNodes;
        EvictedNodes = evictedNodes ?? [];
    }

    public IReadOnlyList<AquariumFractalKey> ResidentNodes { get; }

    public IReadOnlyList<AquariumFractalKey> SummaryFallbackNodes { get; }

    public IReadOnlyList<AquariumFractalKey> RequestedNodes { get; }

    public IReadOnlyList<AquariumFractalKey> EvictedNodes { get; }
}
