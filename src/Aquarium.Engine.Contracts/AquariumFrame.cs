using System.Numerics;

namespace Aquarium.Engine;

public readonly record struct AquariumFrame(GridFrame Grid, Vector3 CameraPosition, float TimeSeconds, Vector2 CursorWorld)
{
    public AquariumFrame(GridFrame grid, Vector3 cameraPosition, float timeSeconds)
        : this(grid, cameraPosition, timeSeconds, grid.Center)
    {
    }
}

