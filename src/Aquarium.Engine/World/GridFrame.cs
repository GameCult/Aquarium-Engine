using Stride.Core.Mathematics;

namespace Aquarium.Engine;

public readonly record struct GridFrame(Vector2 Center, float Radius)
{
    public static GridFrame FromCamera(OrbitCameraRig camera)
    {
        return new GridFrame(camera.Target, camera.Distance);
    }
}
