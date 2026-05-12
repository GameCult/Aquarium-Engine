namespace Aquarium.Engine.Render.Graph;

internal static class D3D12RenderGraphCompiler
{
    public static CompiledRenderGraph Compile(AquariumRenderPlan plan)
    {
        var resourceNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in plan.Graph.RenderTargets)
        {
            if (string.IsNullOrWhiteSpace(target.Handle.Name))
            {
                throw new InvalidOperationException("Render target names must not be empty.");
            }

            if (!resourceNames.Add(target.Handle.Name))
            {
                throw new InvalidOperationException($"Render target declared more than once: {target.Handle.Name}");
            }
        }

        var passNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pass in plan.Graph.Passes)
        {
            if (string.IsNullOrWhiteSpace(pass.Handle.Name))
            {
                throw new InvalidOperationException("Render pass names must not be empty.");
            }

            if (!passNames.Add(pass.Handle.Name))
            {
                throw new InvalidOperationException($"Render pass declared more than once: {pass.Handle.Name}");
            }
        }

        return new CompiledRenderGraph(
            plan.Graph.RenderTargets.ToArray(),
            plan.Graph.Cameras.ToArray(),
            plan.Graph.Passes.ToArray(),
            plan.Graph.DebugViews.ToArray());
    }
}

internal sealed record CompiledRenderGraph(
    IReadOnlyList<AquariumRenderTargetDescription> RenderTargets,
    IReadOnlyList<AquariumCameraDescription> Cameras,
    IReadOnlyList<AquariumPassDescription> Passes,
    IReadOnlyList<AquariumDebugViewDescription> DebugViews)
{
    public string Describe()
    {
        return $"targets={RenderTargets.Count}, cameras={Cameras.Count}, passes={Passes.Count}, debugViews={DebugViews.Count}";
    }
}
