namespace Aquarium.Engine.Fractal.Lod;

public sealed class FractalSurfacePageResidencyPlan
{
    public FractalSurfacePageResidencyPlan(
        IReadOnlyList<AquariumFractalSurfacePage> residentPages,
        IReadOnlyList<AquariumFractalSurfacePage> missingPages,
        IReadOnlyList<AquariumFractalSurfacePage> requestedPages,
        IReadOnlyList<AquariumFractalSurfacePage> evictedPages,
        long residentBytes)
    {
        ResidentPages = residentPages;
        MissingPages = missingPages;
        RequestedPages = requestedPages;
        EvictedPages = evictedPages;
        ResidentBytes = residentBytes;
    }

    public IReadOnlyList<AquariumFractalSurfacePage> ResidentPages { get; }

    public IReadOnlyList<AquariumFractalSurfacePage> MissingPages { get; }

    public IReadOnlyList<AquariumFractalSurfacePage> RequestedPages { get; }

    public IReadOnlyList<AquariumFractalSurfacePage> EvictedPages { get; }

    public long ResidentBytes { get; }
}
