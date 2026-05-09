using Vortice.Direct3D12;

namespace Aquarium.Engine.Render;

internal sealed class D3D12TrackedResource : IDisposable
{
    public D3D12TrackedResource(ID3D12Resource resource, ResourceStates state, string name, bool ownsResource)
    {
        Resource = resource;
        State = state;
        OwnsResource = ownsResource;
        Resource.Name = name;
    }

    public ID3D12Resource Resource { get; }

    public ResourceStates State { get; private set; }

    public bool OwnsResource { get; }

    public void Transition(ID3D12GraphicsCommandList commandList, ResourceStates nextState)
    {
        if (State == nextState)
        {
            return;
        }

        commandList.ResourceBarrier(ResourceBarrier.BarrierTransition(Resource, State, nextState));
        State = nextState;
    }

    public void MarkState(ResourceStates state)
    {
        State = state;
    }

    public void Dispose()
    {
        if (OwnsResource)
        {
            Resource.Dispose();
        }
    }
}
