using System.Runtime.CompilerServices;
using Vortice.Direct3D12;

namespace Aquarium.Engine.Render;

internal sealed unsafe class D3D12UploadRing : IDisposable
{
    private readonly ID3D12Resource resource;
    private readonly byte* mapped;
    private int cursor;

    public D3D12UploadRing(ID3D12Device device, int capacityBytes, string name)
    {
        CapacityBytes = Align256(capacityBytes);
        Name = name;
        resource = device.CreateCommittedResource(
            HeapType.Upload,
            ResourceDescription.Buffer((ulong)CapacityBytes),
            ResourceStates.GenericRead,
            null);
        resource.Name = name;
        mapped = (byte*)resource.Map<byte>(0);
    }

    public int CapacityBytes { get; }

    public int UsedBytes => cursor;

    public string Name { get; }

    public ulong GpuVirtualAddress => resource.GPUVirtualAddress;

    public void Reset()
    {
        cursor = 0;
    }

    public D3D12UploadAllocation WriteConstant<T>(in T value)
        where T : unmanaged
    {
        var size = Align256(Unsafe.SizeOf<T>());
        if (cursor + size > CapacityBytes)
        {
            throw new InvalidOperationException($"D3D12 upload ring '{Name}' exhausted ({cursor}/{CapacityBytes} bytes used, requested {size} bytes).");
        }

        var offset = cursor;
        Unsafe.Write(mapped + offset, value);
        cursor += size;
        return new D3D12UploadAllocation(GpuVirtualAddress + (ulong)offset, (uint)size);
    }

    public string Describe()
    {
        return $"{Name}: {cursor}/{CapacityBytes} bytes";
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

internal readonly record struct D3D12UploadAllocation(ulong GpuVirtualAddress, uint SizeInBytes);
