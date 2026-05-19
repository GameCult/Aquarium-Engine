namespace Aquarium.Engine.Fractal.Lod;

public interface IFractalSurfacePageStore
{
    bool IsResident(AquariumFractalSurfacePageKey key);

    void Request(AquariumFractalSurfacePage page);

    void Evict(AquariumFractalSurfacePageKey key);
}
