using Vortice.Direct3D12;

namespace Aquarium.Engine.Render;

internal sealed class D3D12DescriptorArena : IDisposable
{
    private readonly int descriptorSize;
    private int used;

    public D3D12DescriptorArena(ID3D12Device device, DescriptorHeapType type, int capacity, DescriptorHeapFlags flags, string name)
    {
        Type = type;
        Capacity = capacity;
        Name = name;
        Heap = device.CreateDescriptorHeap(new DescriptorHeapDescription(type, (uint)capacity, flags));
        Heap.Name = name;
        descriptorSize = (int)device.GetDescriptorHandleIncrementSize(type);
    }

    public ID3D12DescriptorHeap Heap { get; }

    public DescriptorHeapType Type { get; }

    public int Capacity { get; }

    public int Used => used;

    public int Available => Capacity - used;

    public string Name { get; }

    public D3D12DescriptorSlot Allocate()
    {
        if (used >= Capacity)
        {
            throw new InvalidOperationException($"D3D12 descriptor arena '{Name}' exhausted ({Capacity} {Type} descriptors).");
        }

        var index = used++;
        return new D3D12DescriptorSlot(
            Heap.GetCPUDescriptorHandleForHeapStart() + (index * descriptorSize),
            Heap.GetGPUDescriptorHandleForHeapStart() + (index * descriptorSize));
    }

    public D3D12DescriptorSlot AllocateRange(int count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Descriptor range count must be positive.");
        }

        if (used + count > Capacity)
        {
            throw new InvalidOperationException($"D3D12 descriptor arena '{Name}' exhausted ({Capacity} {Type} descriptors; requested range {count}).");
        }

        var index = used;
        used += count;
        return new D3D12DescriptorSlot(
            Heap.GetCPUDescriptorHandleForHeapStart() + (index * descriptorSize),
            Heap.GetGPUDescriptorHandleForHeapStart() + (index * descriptorSize));
    }

    public D3D12DescriptorSlot Offset(D3D12DescriptorSlot start, int offset)
    {
        return new D3D12DescriptorSlot(
            start.Cpu + (offset * descriptorSize),
            start.Gpu + (offset * descriptorSize));
    }

    public void Reset()
    {
        used = 0;
    }

    public string Describe()
    {
        return $"{Name}: {used}/{Capacity} {Type} descriptors";
    }

    public void Dispose()
    {
        Heap.Dispose();
    }
}

internal readonly record struct D3D12DescriptorSlot(CpuDescriptorHandle Cpu, GpuDescriptorHandle Gpu);
