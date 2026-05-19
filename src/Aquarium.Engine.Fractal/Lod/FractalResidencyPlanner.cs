using Aquarium.Engine.Fractal;

namespace Aquarium.Engine.Fractal.Lod;

public static class FractalResidencyPlanner
{
    public static FractalResidencyPlan Plan(IReadOnlyList<AquariumSelectedCut> selectedCut, IFractalPayloadStore payloadStore)
    {
        return Plan(selectedCut, payloadStore, maxRequests: int.MaxValue, maxResidentNodes: int.MaxValue);
    }

    public static FractalResidencyPlan Plan(
        IReadOnlyList<AquariumSelectedCut> selectedCut,
        IFractalPayloadStore payloadStore,
        int maxRequests,
        int maxResidentNodes)
    {
        ArgumentNullException.ThrowIfNull(selectedCut);
        ArgumentNullException.ThrowIfNull(payloadStore);

        if (maxRequests < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRequests), maxRequests, "Request budget must not be negative.");
        }

        if (maxResidentNodes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResidentNodes), maxResidentNodes, "Resident budget must not be negative.");
        }

        var resident = new List<AquariumFractalKey>();
        var fallback = new List<AquariumFractalKey>();
        var requested = new List<AquariumFractalKey>();

        foreach (var cut in selectedCut)
        {
            if (payloadStore.IsResident(cut.NodeKey))
            {
                if (resident.Count < maxResidentNodes)
                {
                    resident.Add(cut.NodeKey);
                }
                else
                {
                    fallback.Add(cut.NodeKey);
                }

                continue;
            }

            fallback.Add(cut.NodeKey);
            if (cut.RequestedChildren && requested.Count < maxRequests)
            {
                payloadStore.Request(cut.NodeKey);
                requested.Add(cut.NodeKey);
            }
        }

        return new FractalResidencyPlan(resident, fallback, requested);
    }
}
