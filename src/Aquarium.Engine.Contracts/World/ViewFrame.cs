using System.Numerics;

namespace Aquarium.Engine;

public readonly record struct ViewFrame(Vector2 Center, float Radius)
{
    private const float MinimumRadius = 20.0f;
    private const float MaximumRadius = 80.0f;
    private const float CameraDistanceScale = 2.0f;

    public static ViewFrame FromCamera(Vector2 target, float distance)
    {
        return new ViewFrame(target, Math.Clamp(distance * CameraDistanceScale, MinimumRadius, MaximumRadius));
    }
}
