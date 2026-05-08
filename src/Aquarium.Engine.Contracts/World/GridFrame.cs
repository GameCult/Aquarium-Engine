using System.Numerics;

namespace Aquarium.Engine;

public readonly record struct GridFrame(Vector2 Center, float Radius)
{
    public static GridFrame FromCamera(Vector2 target, float distance)
    {
        return new GridFrame(target, distance);
    }
}
