using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Aquarium.Engine.Render;

internal sealed class D3D12RenderTarget : IDisposable
{
    public D3D12RenderTarget(
        ID3D12Device device,
        int width,
        int height,
        Format format,
        D3D12DescriptorSlot renderTargetView,
        D3D12DescriptorSlot? shaderResourceView,
        D3D12DescriptorSlot? unorderedAccessView,
        bool allowUnorderedAccess,
        Color4 clearColor,
        string name)
    {
        Width = width;
        Height = height;
        Format = format;
        RenderTargetView = renderTargetView;
        ShaderResourceView = shaderResourceView;
        UnorderedAccessView = unorderedAccessView;
        var resourceFlags = ResourceFlags.AllowRenderTarget;
        if (allowUnorderedAccess || unorderedAccessView.HasValue)
        {
            resourceFlags |= ResourceFlags.AllowUnorderedAccess;
        }

        var optimizedClear = new ClearValue(format, in clearColor);
        Resource = device.CreateCommittedResource(
            HeapType.Default,
            ResourceDescription.Texture2D(
                format,
                (uint)width,
                (uint)height,
                1,
                1,
                1,
                0,
                resourceFlags),
            ResourceStates.PixelShaderResource,
            optimizedClear);
        Resource.Name = name;
        device.CreateRenderTargetView(Resource, null, renderTargetView.Cpu);
        if (shaderResourceView.HasValue)
        {
            device.CreateShaderResourceView(
                Resource,
                new ShaderResourceViewDescription
                {
                    Format = format,
                    ViewDimension = ShaderResourceViewDimension.Texture2D,
                    Shader4ComponentMapping = ShaderComponentMapping.Default,
                    Texture2D = new Texture2DShaderResourceView { MipLevels = 1 },
                },
                shaderResourceView.Value.Cpu);
        }

        if (unorderedAccessView.HasValue)
        {
            CreateUnorderedAccessView(device, unorderedAccessView.Value);
        }
    }

    public ID3D12Resource Resource { get; }

    public int Width { get; }

    public int Height { get; }

    public Format Format { get; }

    public D3D12DescriptorSlot RenderTargetView { get; }

    public D3D12DescriptorSlot? ShaderResourceView { get; }

    public D3D12DescriptorSlot? UnorderedAccessView { get; }

    public ResourceStates State { get; private set; } = ResourceStates.PixelShaderResource;

    public void CreateUnorderedAccessView(ID3D12Device device, D3D12DescriptorSlot unorderedAccessView)
    {
        device.CreateUnorderedAccessView(
            Resource,
            null,
            new UnorderedAccessViewDescription
            {
                Format = Format,
                ViewDimension = UnorderedAccessViewDimension.Texture2D,
                Texture2D = new Texture2DUnorderedAccessView(),
            },
            unorderedAccessView.Cpu);
    }

    public void Transition(ID3D12GraphicsCommandList commandList, ResourceStates nextState)
    {
        if (State == nextState)
        {
            return;
        }

        commandList.ResourceBarrier(ResourceBarrier.BarrierTransition(Resource, State, nextState));
        State = nextState;
    }

    public void Dispose()
    {
        Resource.Dispose();
    }
}
