using System.Numerics;
using Aquarium.Engine.Render;

namespace Aquarium.Engine;

public readonly record struct AquariumFrame(GridFrame Grid, Vector3 CameraPosition, float TimeSeconds, Vector2 CursorWorld, AquariumSceneState Scene)
{
    public AquariumFrame(GridFrame grid, Vector3 cameraPosition, float timeSeconds)
        : this(grid, cameraPosition, timeSeconds, grid.Center, AquariumSceneState.Empty)
    {
    }

    public AquariumFrame(GridFrame grid, Vector3 cameraPosition, float timeSeconds, Vector2 cursorWorld)
        : this(grid, cameraPosition, timeSeconds, cursorWorld, AquariumSceneState.Empty)
    {
    }
}

public readonly record struct AquariumFrameInput(Vector2 MousePosition, int Width, int Height);

