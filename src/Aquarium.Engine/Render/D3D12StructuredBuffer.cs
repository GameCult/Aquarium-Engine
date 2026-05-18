using System.Runtime.CompilerServices;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Aquarium.Engine.Render;

internal sealed class D3D12StructuredBuffer : IDisposable
{
    private readonly int elementCount;
    private readonly int strideBytes;

    public D3D12StructuredBuffer(ID3D12Device device, int elementCount, int strideBytes, string name, bool allowUnorderedAccess = false)
    {
        this.elementCount = elementCount;
        this.strideBytes = strideBytes;
        SizeBytes = elementCount * strideBytes;
        Resource = device.CreateCommittedResource(
            HeapType.Default,
            ResourceDescription.Buffer(
                (ulong)SizeBytes,
                allowUnorderedAccess ? ResourceFlags.AllowUnorderedAccess : ResourceFlags.None),
            ResourceStates.Common,
            null);
        Resource.Name = name;
    }

    public ID3D12Resource Resource { get; }

    public ResourceStates State { get; private set; } = ResourceStates.Common;

    public int SizeBytes { get; }

    public string Describe()
    {
        return $"{elementCount} elements x {strideBytes} bytes";
    }

    public void Upload<T>(ID3D12GraphicsCommandList commandList, D3D12UploadRing uploadRing, ReadOnlySpan<T> values)
        where T : unmanaged
    {
        if (values.Length != elementCount)
        {
            throw new ArgumentException($"Structured buffer expected {elementCount} elements but received {values.Length}.", nameof(values));
        }

        var actualStride = Unsafe.SizeOf<T>();
        if (actualStride != strideBytes)
        {
            throw new ArgumentException($"Structured buffer stride mismatch. Expected {strideBytes} bytes but received {actualStride}.", nameof(values));
        }

        var upload = uploadRing.WriteArray(values);
        Transition(commandList, ResourceStates.CopyDest);
        commandList.CopyBufferRegion(Resource, 0, uploadRing.Resource, (ulong)upload.OffsetBytes, (ulong)upload.DataBytes);
        Transition(commandList, ResourceStates.PixelShaderResource | ResourceStates.NonPixelShaderResource);
    }

    public void UploadPartial<T>(ID3D12GraphicsCommandList commandList, D3D12UploadRing uploadRing, ReadOnlySpan<T> values)
        where T : unmanaged
    {
        if (values.Length > elementCount)
        {
            throw new ArgumentException($"Structured buffer holds {elementCount} elements but received {values.Length}.", nameof(values));
        }

        var actualStride = Unsafe.SizeOf<T>();
        if (actualStride != strideBytes)
        {
            throw new ArgumentException($"Structured buffer stride mismatch. Expected {strideBytes} bytes but received {actualStride}.", nameof(values));
        }

        var upload = uploadRing.WriteArray(values);
        Transition(commandList, ResourceStates.CopyDest);
        if (upload.DataBytes > 0)
        {
            commandList.CopyBufferRegion(Resource, 0, uploadRing.Resource, (ulong)upload.OffsetBytes, (ulong)upload.DataBytes);
        }
        Transition(commandList, ResourceStates.PixelShaderResource | ResourceStates.NonPixelShaderResource);
    }

    public void CreateShaderResourceView(ID3D12Device device, D3D12DescriptorSlot descriptor)
    {
        device.CreateShaderResourceView(
            Resource,
            new ShaderResourceViewDescription
            {
                Format = Format.Unknown,
                ViewDimension = ShaderResourceViewDimension.Buffer,
                Shader4ComponentMapping = ShaderComponentMapping.Default,
                Buffer = new BufferShaderResourceView
                {
                    FirstElement = 0,
                    NumElements = (uint)elementCount,
                    StructureByteStride = (uint)strideBytes,
                    Flags = BufferShaderResourceViewFlags.None,
                },
            },
            descriptor.Cpu);
    }

    public void CreateUnorderedAccessView(ID3D12Device device, D3D12DescriptorSlot descriptor)
    {
        device.CreateUnorderedAccessView(
            Resource,
            null,
            new UnorderedAccessViewDescription
            {
                Format = Format.Unknown,
                ViewDimension = UnorderedAccessViewDimension.Buffer,
                Buffer = new BufferUnorderedAccessView
                {
                    FirstElement = 0,
                    NumElements = (uint)elementCount,
                    StructureByteStride = (uint)strideBytes,
                    CounterOffsetInBytes = 0,
                    Flags = BufferUnorderedAccessViewFlags.None,
                },
            },
            descriptor.Cpu);
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
