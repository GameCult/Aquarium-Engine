using Vortice.Direct3D12;

namespace Aquarium.Engine.Render;

internal sealed class D3D12DescriptorArena : IDisposable
{
    private readonly int descriptorSize;
    private int used;

    public D3D12DescriptorArena(ID3D12Device device, DescriptorHeapType type, int capacity, DescriptorHeapFlags flags)
    {
        Type = type;
        Capacity = capacity;
        Heap = device.CreateDescriptorHeap(new DescriptorHeapDescription(type, (uint)capacity, flags));
        descriptorSize = (int)device.GetDescriptorHandleIncrementSize(type);
    }

    public ID3D12DescriptorHeap Heap { get; }

    public DescriptorHeapType Type { get; }

    public int Capacity { get; }

    public D3D12DescriptorSlot Allocate()
    {
        if (used >= Capacity)
        {
            throw new InvalidOperationException($"D3D12 {Type} descriptor arena exhausted ({Capacity} descriptors).");
        }

        var index = used++;
        return new D3D12DescriptorSlot(
            Heap.GetCPUDescriptorHandleForHeapStart() + (index * descriptorSize),
            Heap.GetGPUDescriptorHandleForHeapStart() + (index * descriptorSize));
    }

    public void Dispose()
    {
        Heap.Dispose();
    }
}

internal readonly record struct D3D12DescriptorSlot(CpuDescriptorHandle Cpu, GpuDescriptorHandle Gpu);
