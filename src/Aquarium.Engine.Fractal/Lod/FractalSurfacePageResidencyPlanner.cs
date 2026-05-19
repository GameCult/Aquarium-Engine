namespace Aquarium.Engine.Fractal.Lod;

public static class FractalSurfacePageResidencyPlanner
{
    public static FractalSurfacePageResidencyPlan Plan(
        IReadOnlyList<AquariumFractalSurfacePage> pages,
        IFractalSurfacePageStore store,
        long maxResidentBytes,
        int maxRequests)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(store);
        if (maxResidentBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResidentBytes), maxResidentBytes, "Resident byte budget must not be negative.");
        }

        if (maxRequests < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRequests), maxRequests, "Request budget must not be negative.");
        }

        var rows = pages
            .Select((page, index) => new PageCandidate(page, index, EstimatedBytes(page), store.IsResident(page.Key)))
            .ToArray();
        var retainedIndexes = new HashSet<int>();
        var retainedBytes = 0L;
        foreach (var candidate in rows
            .Where(candidate => candidate.IsResident)
            .OrderByDescending(PageValue)
            .ThenByDescending(candidate => candidate.Page.MaxError)
            .ThenBy(candidate => candidate.Page.Key.Value, StringComparer.Ordinal))
        {
            if (retainedBytes + candidate.EstimatedBytes > maxResidentBytes)
            {
                continue;
            }

            retainedIndexes.Add(candidate.Index);
            retainedBytes += candidate.EstimatedBytes;
        }

        var resident = new List<AquariumFractalSurfacePage>();
        var missing = new List<AquariumFractalSurfacePage>();
        var evicted = new List<AquariumFractalSurfacePage>();
        foreach (var candidate in rows)
        {
            if (candidate.IsResident && retainedIndexes.Contains(candidate.Index))
            {
                resident.Add(candidate.Page);
                continue;
            }

            missing.Add(candidate.Page);
            if (candidate.IsResident)
            {
                evicted.Add(candidate.Page);
                store.Evict(candidate.Page.Key);
            }
        }

        var requested = rows
            .Where(candidate => !candidate.IsResident)
            .Select(candidate => candidate.Page)
            .OrderByDescending(PageValue)
            .ThenByDescending(page => page.MaxError)
            .ThenBy(page => page.Key.Value, StringComparer.Ordinal)
            .Take(maxRequests)
            .ToArray();
        foreach (var page in requested)
        {
            store.Request(page);
        }

        return new FractalSurfacePageResidencyPlan(resident, missing, requested, evicted, retainedBytes);
    }

    public static long EstimatedBytes(AquariumFractalSurfacePage page)
    {
        if (page.Width <= 0 || page.Height <= 0)
        {
            return 0;
        }

        return (long)page.Width * page.Height * BytesPerPixel(page.Key.Kind);
    }

    private static float PageValue(PageCandidate candidate)
    {
        return PageValue(candidate.Page, candidate.EstimatedBytes);
    }

    private static float PageValue(AquariumFractalSurfacePage page)
    {
        return PageValue(page, EstimatedBytes(page));
    }

    private static float PageValue(AquariumFractalSurfacePage page, long estimatedBytes)
    {
        return MathF.Max(page.MaxError, 0.000001f) / Math.Max(estimatedBytes, 1L);
    }

    private static int BytesPerPixel(AquariumFractalSurfacePageKind kind)
    {
        return kind switch
        {
            AquariumFractalSurfacePageKind.Material => 4,
            AquariumFractalSurfacePageKind.Height => 2,
            AquariumFractalSurfacePageKind.SignedDistance2D => 2,
            AquariumFractalSurfacePageKind.Confidence => 2,
            _ => 4,
        };
    }

    private readonly record struct PageCandidate(
        AquariumFractalSurfacePage Page,
        int Index,
        long EstimatedBytes,
        bool IsResident);
}
