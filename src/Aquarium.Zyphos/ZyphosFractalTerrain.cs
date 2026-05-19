using Aquarium.Engine.Fractal;
using Aquarium.Engine.Fractal.Brushes;
using Aquarium.Engine.Fractal.Debug;
using Aquarium.Engine.Fractal.Grammar;
using Aquarium.Engine.Fractal.Lod;
using Aquarium.Engine.Render;

namespace Aquarium.Zyphos;

public static class ZyphosFractalTerrain
{
    private const string PatchRelativePath = "Worlds/zyphos-first-fractal-terrain.aquageo";
    private const int SurfacePageDimension = 128;
    private const long MaxResidentSurfacePageBytes = 2L * 1024L * 1024L;
    private const int MaxSurfacePageRequests = 8;

    private static readonly Lazy<string> PatchSource = new(() => File.ReadAllText(PatchPath));
    private static readonly Lazy<FractalOwnershipTree> Tree = new(() => FractalDslCompiler.Compile(PatchSource.Value));
    private static readonly Lazy<AquariumFractalSummary[]> Summaries = new(() => FractalSummaryBuilder.Build(Tree.Value));
    private static readonly Lazy<AquariumSelectedCut[]> SelectedCut = new(() => FractalSelectedCutBuilder.Build(Summaries.Value, _ => 8.0f, maxEstimatedCost: 64.0f));
    private static readonly Lazy<AquariumHeightFieldBrush[]> Brushes = new(() => FractalHeightBrushCompiler.CompileSelectedTree(Tree.Value, SelectedCut.Value));
    private static readonly FractalContributionCache ContributionCache = new();
    private static readonly object PlanCacheLock = new();
    private static readonly Dictionary<string, AquariumHeightFieldBrush[]> BrushCache = [];
    private static readonly Dictionary<string, FractalSurfacePagePayload> SurfacePagePayloadCache = [];
    private static readonly FractalResourceBudget DefaultBudget = new(
        MaxCpuUpdates: 2,
        MaxGpuEstimatedCost: 64.0f,
        MaxResidentPayloads: 64,
        MaxSsdRequests: 2);
    private static readonly AquariumFractalSurfacePageKind[] PlannedSurfacePageKinds =
    [
        AquariumFractalSurfacePageKind.Height,
        AquariumFractalSurfacePageKind.SignedDistance2D,
        AquariumFractalSurfacePageKind.Material,
        AquariumFractalSurfacePageKind.Confidence,
    ];

    public static string PatchPath => Path.Combine(AppContext.BaseDirectory, PatchRelativePath);

    public static FractalOwnershipTree OwnershipTree => Tree.Value;

    public static AquariumHeightFieldBrush[] HeightBrushes => Brushes.Value;

    public static AquariumFractalSummary[] NodeSummaries => Summaries.Value;

    public static AquariumSelectedCut[] SelectedCuts => SelectedCut.Value;

    public static string DebugDump => FractalDebugDump.Build(OwnershipTree, Summaries.Value, SelectedCut.Value);

    public static ZyphosFractalRenderPlan BuildRenderPlan(ZyphosCameraShot shot)
    {
        var pixelsPerWorld = Math.Clamp(72.0f / MathF.Max(shot.EffectiveDistance, 0.001f), 0.25f, 48.0f);
        var bucket = (int)Math.Clamp(MathF.Round(pixelsPerWorld * 16.0f), 4.0f, 768.0f);
        var bucketPixelsPerWorld = bucket / 16.0f;
        FractalResourcePlan resourcePlan;
        lock (PlanCacheLock)
        {
            resourcePlan = ContributionCache.PlanFrame(
                Summaries.Value,
                _ => bucketPixelsPerWorld,
                DefaultBudget,
                new FractalXorShiftRandom((uint)bucket ^ 0x9E3779B9u),
                ResidentFractalPayloadStore.Instance);
        }

        var selectedCut = resourcePlan.SelectedCut;
        var structuralProbeReservoir = FractalStructuralProbeReservoir.Build(
            Tree.Value,
            Summaries.Value,
            selectedCut,
            bucketPixelsPerWorld,
            new FractalXorShiftRandom(((uint)bucket ^ 0xC2B2AE35u) + (uint)selectedCut.Length));
        var structuralSdfSplats = BuildStructuralSdfSplats(structuralProbeReservoir);
        var surfacePages = PlanSurfacePages(selectedCut);
        var surfacePageResidency = FractalSurfacePageResidencyPlanner.Plan(
            surfacePages,
            ResidentSurfacePageStore.Instance,
            MaxResidentSurfacePageBytes,
            MaxSurfacePageRequests);
        var surfacePagePayloads = BuildSurfacePagePayloads(surfacePageResidency.ResidentPages);
        var cutKey = CutCacheKey(selectedCut);
        AquariumHeightFieldBrush[] brushes;
        lock (PlanCacheLock)
        {
            if (!BrushCache.TryGetValue(cutKey, out brushes!))
            {
                brushes = OrderBrushesForGeometry(FractalHeightBrushCompiler.CompileSelectedTree(Tree.Value, selectedCut));
                BrushCache[cutKey] = brushes;
            }
        }

        return new ZyphosFractalRenderPlan(
            brushes,
            selectedCut,
            resourcePlan,
            structuralProbeReservoir,
            structuralSdfSplats,
            surfacePages,
            surfacePageResidency,
            surfacePagePayloads,
            bucketPixelsPerWorld,
            $"{selectedCut.Length}/{Summaries.Value.Length} cuts / {brushes.Length}/{OwnershipTree.Claims.Count} brushes / {resourcePlan.UpdateNodes.Length}/{DefaultBudget.MaxCpuUpdates} cpu updates / {resourcePlan.GpuEstimatedCost:0.0}/{DefaultBudget.MaxGpuEstimatedCost:0.0} gpu cost / {resourcePlan.Residency.ResidentNodes.Count}/{DefaultBudget.MaxResidentPayloads} resident / {resourcePlan.Residency.RequestedNodes.Count}/{DefaultBudget.MaxSsdRequests} ssd requests / {(structuralProbeReservoir.HasSample ? structuralProbeReservoir.CandidateCount : 0)} probe candidates / {surfacePagePayloads.Length}/{surfacePages.Length} surface payloads / {bucketPixelsPerWorld:0.00} px-wu");
    }

    private static AquariumFractalSurfacePage[] PlanSurfacePages(IReadOnlyList<AquariumSelectedCut> selectedCut)
    {
        return PlannedSurfacePageKinds
            .SelectMany(kind => FractalSurfacePagePlanner.Plan(
                Tree.Value,
                Summaries.Value,
                selectedCut,
                kind,
                SurfacePageDimension,
                SurfacePageDimension))
            .ToArray();
    }

    private static AquariumFractalSdfSplat3D[] BuildStructuralSdfSplats(FractalProbeReservoirSnapshot reservoir)
    {
        if (!reservoir.HasSample)
        {
            return [];
        }

        var thickness = MathF.Max(reservoir.BoundRadius * 0.25f, 0.001f);
        return [FractalSdfSplat3DCompiler.FromProbe(reservoir.ToProbeSample(), thickness)];
    }

    private static FractalSurfacePagePayload[] BuildSurfacePagePayloads(IReadOnlyList<AquariumFractalSurfacePage> residentPages)
    {
        var payloads = new FractalSurfacePagePayload[residentPages.Count];
        lock (PlanCacheLock)
        {
            for (var index = 0; index < residentPages.Count; index++)
            {
                var page = residentPages[index];
                if (!SurfacePagePayloadCache.TryGetValue(page.Key.Value, out var payload))
                {
                    payload = FractalSurfacePageRasterizer.Rasterize(Tree.Value, page);
                    SurfacePagePayloadCache[page.Key.Value] = payload;
                }

                payloads[index] = payload;
            }
        }

        return payloads;
    }

    private static string CutCacheKey(IReadOnlyList<AquariumSelectedCut> selectedCut)
    {
        return string.Join("|", selectedCut.Select(cut => cut.NodeKey.Value));
    }

    private static AquariumHeightFieldBrush[] OrderBrushesForGeometry(AquariumHeightFieldBrush[] brushes)
    {
        return brushes
            .OrderByDescending(brush => MathF.Abs(brush.Amplitude) * MathF.Max(brush.Radius, brush.RadiusY))
            .ThenBy(brush => brush.DomainFace)
            .ThenBy(brush => brush.DomainLevel)
            .ThenBy(brush => brush.Center.X)
            .ThenBy(brush => brush.Center.Y)
            .ToArray();
    }

    public static string Summary =>
        $"{Path.GetFileName(PatchPath)} / {OwnershipTree.Claims.Count} DSL claims / {OwnershipTree.Nodes.Count} tile nodes / {SelectedCut.Value.Length} cuts / {HeightBrushes.Length} shaped brushes";

    public static string BuildPlanDebugDump(ZyphosFractalRenderPlan plan)
    {
        var budget = plan.ResourcePlan.Budget;
        var lines = new List<string>
        {
            "Zyphos fractal resource plan",
            $"  pixelsPerWorld: {plan.PixelsPerWorld:0.000}",
            $"  selectedCut: {plan.SelectedCuts.Length}/{Summaries.Value.Length}",
            $"  brushes: {plan.HeightBrushes.Length}/{OwnershipTree.Claims.Count}",
            $"  cpuUpdates: {plan.ResourcePlan.UpdateNodes.Length}/{budget.MaxCpuUpdates}",
            $"  gpuEstimatedCost: {plan.ResourcePlan.GpuEstimatedCost:0.000}/{budget.MaxGpuEstimatedCost:0.000}",
            $"  ramResident: {plan.ResourcePlan.Residency.ResidentNodes.Count}/{budget.MaxResidentPayloads}",
            $"  ssdRequests: {plan.ResourcePlan.Residency.RequestedNodes.Count}/{budget.MaxSsdRequests}",
            $"  evictions: {plan.ResourcePlan.Residency.EvictedNodes.Count}",
            $"  structuralProbe: {(plan.StructuralProbeReservoir.HasSample ? plan.StructuralProbeReservoir.NodeKey.Value : "none")}",
            $"  structuralProbeWeight: {plan.StructuralProbeReservoir.WeightSum:0.000} candidates={plan.StructuralProbeReservoir.CandidateCount} contributionWeight={plan.StructuralProbeReservoir.ContributionWeight:0.000}",
            $"  structuralSdfSplats: {plan.StructuralSdfSplats.Length}",
            $"  surfacePages: {plan.SurfacePages.Length} {SurfacePageDimension}x{SurfacePageDimension}",
            $"  surfacePageResident: {plan.SurfacePageResidency.ResidentPages.Count}/{plan.SurfacePages.Length} bytes={plan.SurfacePageResidency.ResidentBytes}/{MaxResidentSurfacePageBytes}",
            $"  surfacePagePayloads: {plan.SurfacePagePayloads.Length}",
            $"  surfacePageRequests: {plan.SurfacePageResidency.RequestedPages.Count}/{MaxSurfacePageRequests}",
            $"  surfacePageEvictions: {plan.SurfacePageResidency.EvictedPages.Count}",
            "selected:",
        };

        foreach (var cut in plan.SelectedCuts)
        {
            lines.Add($"  {cut.NodeKey.Value} score={cut.Score:0.000} fade={cut.Fade:0.000} requestChildren={cut.RequestedChildren}");
        }

        if (plan.ResourcePlan.UpdateNodes.Length > 0)
        {
            lines.Add("updates:");
            foreach (var key in plan.ResourcePlan.UpdateNodes)
            {
                lines.Add($"  {key.Value}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private sealed class ResidentFractalPayloadStore : IFractalPayloadStore
    {
        public static ResidentFractalPayloadStore Instance { get; } = new();

        public bool IsResident(AquariumFractalKey nodeKey)
        {
            return true;
        }

        public void Request(AquariumFractalKey nodeKey)
        {
        }
    }

    private sealed class ResidentSurfacePageStore : IFractalSurfacePageStore
    {
        public static ResidentSurfacePageStore Instance { get; } = new();

        public bool IsResident(AquariumFractalSurfacePageKey key)
        {
            return true;
        }

        public void Request(AquariumFractalSurfacePage page)
        {
        }

        public void Evict(AquariumFractalSurfacePageKey key)
        {
        }
    }
}

public readonly record struct ZyphosFractalRenderPlan(
    AquariumHeightFieldBrush[] HeightBrushes,
    AquariumSelectedCut[] SelectedCuts,
    FractalResourcePlan ResourcePlan,
    FractalProbeReservoirSnapshot StructuralProbeReservoir,
    AquariumFractalSdfSplat3D[] StructuralSdfSplats,
    AquariumFractalSurfacePage[] SurfacePages,
    FractalSurfacePageResidencyPlan SurfacePageResidency,
    FractalSurfacePagePayload[] SurfacePagePayloads,
    float PixelsPerWorld,
    string Summary);
