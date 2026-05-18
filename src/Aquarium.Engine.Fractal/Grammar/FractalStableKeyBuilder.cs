using Aquarium.Engine.Fractal;

namespace Aquarium.Engine.Fractal.Grammar;

public static class FractalStableKeyBuilder
{
    public static AquariumFractalKey ForCubeTile(CubeTileKey tile, string grammarPath)
    {
        return new AquariumFractalKey($"cube/{tile.Face}/L{tile.Level:D2}/{tile.X}/{tile.Y}:{NormalizePath(grammarPath)}");
    }

    public static AquariumFractalKey Child(AquariumFractalKey parent, string segment)
    {
        return new AquariumFractalKey($"{parent.Value}/{NormalizePath(segment)}");
    }

    private static string NormalizePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Fractal key path segment must not be empty.", nameof(value));
        }

        return value.Trim().Replace('\\', '/');
    }
}
