using System.Numerics;

namespace Aquarium.Engine.Fractal.Brushes;

public readonly record struct FractalBrushEnvelopeBounds2D(Vector2 Min, Vector2 Max)
{
    public bool Contains(Vector2 point)
    {
        return point.X >= Min.X && point.X <= Max.X && point.Y >= Min.Y && point.Y <= Max.Y;
    }
}
