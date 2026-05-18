using System.Numerics;

namespace Aquarium.Engine.Fractal.Projection;

public interface ICubeSphereProjection
{
    string Name { get; }

    Vector3 Project(CubeFacePosition position);
}
