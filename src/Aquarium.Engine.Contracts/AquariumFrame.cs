using System.Numerics;
using Aquarium.Engine.Render;

namespace Aquarium.Engine;

public readonly record struct AquariumFrame(ViewFrame View, Vector3 CameraPosition, Vector3 CameraTarget, float TimeSeconds, Vector2 CursorWorld, AquariumSceneState Scene)
{
    public AquariumFrame(ViewFrame view, Vector3 cameraPosition, float timeSeconds)
        : this(view, cameraPosition, new Vector3(view.Center, 0.0f), timeSeconds, view.Center, AquariumSceneState.Empty)
    {
    }

    public AquariumFrame(ViewFrame view, Vector3 cameraPosition, float timeSeconds, Vector2 cursorWorld)
        : this(view, cameraPosition, new Vector3(view.Center, 0.0f), timeSeconds, cursorWorld, AquariumSceneState.Empty)
    {
    }

    public AquariumFrame(ViewFrame view, Vector3 cameraPosition, float timeSeconds, Vector2 cursorWorld, AquariumSceneState scene)
        : this(view, cameraPosition, new Vector3(view.Center, 0.0f), timeSeconds, cursorWorld, scene)
    {
    }
}

public readonly record struct AquariumFrameInput(Vector2 MousePosition, int Width, int Height);

