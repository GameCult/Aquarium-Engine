using Vortice.Direct3D12;

namespace Aquarium.Engine.Render;

internal sealed class D3D12ResourceRegistry
{
    private readonly Dictionary<string, D3D12TrackedResource> trackedResources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, D3D12RenderTarget> renderTargets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, D3D12StructuredBuffer> structuredBuffers = new(StringComparer.Ordinal);

    public void Add(string name, D3D12TrackedResource resource)
    {
        if (!trackedResources.TryAdd(name, resource))
        {
            throw new InvalidOperationException($"D3D12 resource already registered: {name}");
        }
    }

    public void Add(string name, D3D12RenderTarget renderTarget)
    {
        if (!renderTargets.TryAdd(name, renderTarget))
        {
            throw new InvalidOperationException($"D3D12 render target already registered: {name}");
        }
    }

    public void Add(string name, D3D12StructuredBuffer structuredBuffer)
    {
        if (!structuredBuffers.TryAdd(name, structuredBuffer))
        {
            throw new InvalidOperationException($"D3D12 structured buffer already registered: {name}");
        }
    }

    public D3D12TrackedResource GetResource(string name)
    {
        return trackedResources.TryGetValue(name, out var resource)
            ? resource
            : throw new KeyNotFoundException($"D3D12 resource was not registered: {name}");
    }

    public D3D12RenderTarget GetRenderTarget(string name)
    {
        return renderTargets.TryGetValue(name, out var renderTarget)
            ? renderTarget
            : throw new KeyNotFoundException($"D3D12 render target was not registered: {name}");
    }

    public void RemoveResource(string name)
    {
        trackedResources.Remove(name);
    }

    public void RemoveRenderTarget(string name)
    {
        renderTargets.Remove(name);
    }

    public void RemoveStructuredBuffer(string name)
    {
        structuredBuffers.Remove(name);
    }

    public string Describe()
    {
        return $"resources={trackedResources.Count}, renderTargets={renderTargets.Count}, structuredBuffers={structuredBuffers.Count}";
    }
}
