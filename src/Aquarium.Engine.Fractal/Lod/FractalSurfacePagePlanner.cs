using Aquarium.Engine.Fractal.Grammar;

namespace Aquarium.Engine.Fractal.Lod;

public static class FractalSurfacePagePlanner
{
    public static AquariumFractalSurfacePage[] Plan(
        FractalOwnershipTree tree,
        IReadOnlyList<AquariumFractalSummary> summaries,
        IReadOnlyList<AquariumSelectedCut> selectedCut,
        AquariumFractalSurfacePageKind kind,
        int width,
        int height,
        int mipLevel = 0)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(summaries);
        ArgumentNullException.ThrowIfNull(selectedCut);
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Surface page width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Surface page height must be positive.");
        }

        if (mipLevel < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mipLevel), mipLevel, "Surface page mip level must not be negative.");
        }

        var summariesByKey = summaries.ToDictionary(summary => summary.NodeKey.Value, StringComparer.Ordinal);
        var nodesByKey = tree.Nodes.ToDictionary(node => node.Key.Value, StringComparer.Ordinal);
        var pages = new List<AquariumFractalSurfacePage>(selectedCut.Count);
        for (var index = 0; index < selectedCut.Count; index++)
        {
            var cut = selectedCut[index];
            if (!summariesByKey.TryGetValue(cut.NodeKey.Value, out var summary)
                || !nodesByKey.TryGetValue(cut.NodeKey.Value, out var node))
            {
                continue;
            }

            pages.Add(new AquariumFractalSurfacePage(
                new AquariumFractalSurfacePageKey(node.DomainKey, cut.NodeKey, kind, mipLevel),
                summary.BoundsMinMax,
                width,
                height,
                PayloadHandle: index,
                MaxError: MaxErrorForKind(summary, kind)));
        }

        return pages.ToArray();
    }

    private static float MaxErrorForKind(AquariumFractalSummary summary, AquariumFractalSurfacePageKind kind)
    {
        return kind switch
        {
            AquariumFractalSurfacePageKind.Height => MathF.Max(summary.MaxHeightError, 0.0f),
            AquariumFractalSurfacePageKind.SignedDistance2D => MathF.Max(summary.MaxHeightError, 0.0f),
            AquariumFractalSurfacePageKind.Material => MathF.Max(summary.MaxMaterialDelta, 0.0f),
            AquariumFractalSurfacePageKind.Confidence => MathF.Max(summary.MaxHeightError, summary.MaxMaterialDelta),
            _ => 0.0f,
        };
    }
}
