using System.Numerics;

namespace Aquarium.Engine.Fractal.Projection;

public sealed class NormalizeCubeSphereProjection : ICubeSphereProjection
{
    public string Name => "normalize";

    public Vector3 Project(CubeFacePosition position)
    {
        return Vector3.Normalize(position.ToCubeVector());
    }
}
