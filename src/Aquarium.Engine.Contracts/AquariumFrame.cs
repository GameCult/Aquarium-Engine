using System.Numerics;
using Aquarium.Engine.Render;

namespace Aquarium.Engine;

public readonly record struct AquariumFrame(ViewFrame View, Vector3 CameraPosition, float TimeSeconds, Vector2 CursorWorld, AquariumSceneState Scene)
{
    public AquariumFrame(ViewFrame view, Vector3 cameraPosition, float timeSeconds)
        : this(view, cameraPosition, timeSeconds, view.Center, AquariumSceneState.Empty)
    {
    }

    public AquariumFrame(ViewFrame view, Vector3 cameraPosition, float timeSeconds, Vector2 cursorWorld)
        : this(view, cameraPosition, timeSeconds, cursorWorld, AquariumSceneState.Empty)
    {
    }
}

public readonly record struct AquariumFrameInput(Vector2 MousePosition, int Width, int Height);

