using System.Numerics;

namespace Aquarium.Engine.Fractal;

public readonly record struct CubeFacePosition
{
    public CubeFacePosition(CubeFace face, double u, double v)
    {
        if (!double.IsFinite(u) || u is < -1.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(u), u, "Cube face U must be finite and in [-1, 1].");
        }

        if (!double.IsFinite(v) || v is < -1.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(v), v, "Cube face V must be finite and in [-1, 1].");
        }

        Face = face;
        U = u;
        V = v;
    }

    public CubeFace Face { get; }

    public double U { get; }

    public double V { get; }

    public Vector3 ToCubeVector()
    {
        return Face switch
        {
            CubeFace.PositiveX => new Vector3(1.0f, (float)V, (float)-U),
            CubeFace.NegativeX => new Vector3(-1.0f, (float)V, (float)U),
            CubeFace.PositiveY => new Vector3((float)U, 1.0f, (float)-V),
            CubeFace.NegativeY => new Vector3((float)U, -1.0f, (float)V),
            CubeFace.PositiveZ => new Vector3((float)U, (float)V, 1.0f),
            CubeFace.NegativeZ => new Vector3((float)-U, (float)V, -1.0f),
            _ => throw new ArgumentOutOfRangeException(nameof(Face), Face, "Unknown cube face."),
        };
    }
}
