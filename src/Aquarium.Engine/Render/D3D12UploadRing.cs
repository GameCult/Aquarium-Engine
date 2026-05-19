using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

    public ID3D12Resource Resource => resource;

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

    public D3D12UploadAllocation WriteArray<T>(ReadOnlySpan<T> values)
        where T : unmanaged
    {
        var byteCount = Unsafe.SizeOf<T>() * values.Length;
        var size = Align256(byteCount);
        if (cursor + size > CapacityBytes)
        {
            throw new InvalidOperationException($"D3D12 upload ring '{Name}' exhausted ({cursor}/{CapacityBytes} bytes used, requested {size} bytes).");
        }

        var offset = cursor;
        if (byteCount > 0)
        {
            var source = MemoryMarshal.AsBytes(values);
            source.CopyTo(new Span<byte>(mapped + offset, byteCount));
        }

        cursor += size;
        return new D3D12UploadAllocation(GpuVirtualAddress + (ulong)offset, (uint)size, offset, byteCount);
    }

    public D3D12UploadAllocation WriteBytes(IntPtr source, int byteCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
        if (byteCount > 0 && source == IntPtr.Zero)
        {
            throw new ArgumentException("Source pointer cannot be zero when byte count is non-zero.", nameof(source));
        }

        var size = Align256(byteCount);
        if (cursor + size > CapacityBytes)
        {
            throw new InvalidOperationException($"D3D12 upload ring '{Name}' exhausted ({cursor}/{CapacityBytes} bytes used, requested {size} bytes).");
        }

        var offset = cursor;
        if (byteCount > 0)
        {
            Buffer.MemoryCopy(source.ToPointer(), mapped + offset, byteCount, byteCount);
        }

        cursor += size;
        return new D3D12UploadAllocation(GpuVirtualAddress + (ulong)offset, (uint)size, offset, byteCount);
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

internal readonly record struct D3D12UploadAllocation(ulong GpuVirtualAddress, uint SizeInBytes, int OffsetBytes = 0, int DataBytes = 0);
