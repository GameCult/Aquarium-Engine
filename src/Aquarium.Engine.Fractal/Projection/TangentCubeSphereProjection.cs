using System.Numerics;

namespace Aquarium.Engine.Fractal.Projection;

public sealed class TangentCubeSphereProjection : ICubeSphereProjection
{
    private const double QuarterPi = Math.PI * 0.25;

    public string Name => "tangent";

    public Vector3 Project(CubeFacePosition position)
    {
        var warped = new CubeFacePosition(position.Face, Math.Tan(position.U * QuarterPi), Math.Tan(position.V * QuarterPi));
        return Vector3.Normalize(warped.ToCubeVector());
    }
}
