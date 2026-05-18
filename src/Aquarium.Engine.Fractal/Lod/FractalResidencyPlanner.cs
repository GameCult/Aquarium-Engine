using Aquarium.Engine.Fractal;

namespace Aquarium.Engine.Fractal.Lod;

public static class FractalResidencyPlanner
{
    public static FractalResidencyPlan Plan(IReadOnlyList<AquariumSelectedCut> selectedCut, IFractalPayloadStore payloadStore)
    {
        ArgumentNullException.ThrowIfNull(selectedCut);
        ArgumentNullException.ThrowIfNull(payloadStore);

        var resident = new List<AquariumFractalKey>();
        var fallback = new List<AquariumFractalKey>();
        var requested = new List<AquariumFractalKey>();

        foreach (var cut in selectedCut)
        {
            if (payloadStore.IsResident(cut.NodeKey))
            {
                resident.Add(cut.NodeKey);
                continue;
            }

            fallback.Add(cut.NodeKey);
            if (cut.RequestedChildren)
            {
                payloadStore.Request(cut.NodeKey);
                requested.Add(cut.NodeKey);
            }
        }

        return new FractalResidencyPlan(resident, fallback, requested);
    }
}
