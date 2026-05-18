using Aquarium.Engine.Fractal;

namespace Aquarium.Engine.Fractal.Lod;

public interface IFractalPayloadStore
{
    bool IsResident(AquariumFractalKey nodeKey);

    void Request(AquariumFractalKey nodeKey);
}
