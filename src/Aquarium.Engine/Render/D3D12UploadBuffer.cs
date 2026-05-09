using System.Runtime.CompilerServices;
using Vortice.Direct3D12;

namespace Aquarium.Engine.Render;

internal sealed unsafe class D3D12UploadBuffer<T> : IDisposable
    where T : unmanaged
{
    private readonly ID3D12Resource resource;
    private readonly T* mapped;

    public D3D12UploadBuffer(ID3D12Device device, int elementCount)
    {
        ElementCount = elementCount;
        ElementStride = Align256(Unsafe.SizeOf<T>());
        resource = device.CreateCommittedResource(
            HeapType.Upload,
            ResourceDescription.Buffer((ulong)(ElementStride * elementCount)),
            ResourceStates.GenericRead,
            null);
        mapped = resource.Map<T>(0);
    }

    public int ElementCount { get; }

    public int ElementStride { get; }

    public ID3D12Resource Resource => resource;

    public ulong GpuVirtualAddress => resource.GPUVirtualAddress;

    public void Write(int index, in T value)
    {
        if ((uint)index >= (uint)ElementCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        Unsafe.Write((byte*)mapped + (index * ElementStride), value);
    }

    public void Dispose()
    {
        resource.Unmap(0, null);
        resource.Dispose();
    }

    private static int Align256(int value)
    {
        return (value + 255) & ~255;
    }
}
