namespace Aquarium.Engine.Fractal;

public readonly record struct CubeTileKey
{
    public const int MaxLevel = 30;

    public CubeTileKey(CubeFace face, int level, int x, int y)
    {
        if (level is < 0 or > MaxLevel)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, $"Cube tile level must be in [0, {MaxLevel}].");
        }

        var axisTileCount = 1 << level;
        if (x < 0 || x >= axisTileCount)
        {
            throw new ArgumentOutOfRangeException(nameof(x), x, $"Cube tile X must be in [0, {axisTileCount - 1}] for level {level}.");
        }

        if (y < 0 || y >= axisTileCount)
        {
            throw new ArgumentOutOfRangeException(nameof(y), y, $"Cube tile Y must be in [0, {axisTileCount - 1}] for level {level}.");
        }

        Face = face;
        Level = level;
        X = x;
        Y = y;
    }

    public CubeFace Face { get; }

    public int Level { get; }

    public int X { get; }

    public int Y { get; }

    public int AxisTileCount => 1 << Level;

    public CubeTileKey Parent()
    {
        if (Level == 0)
        {
            throw new InvalidOperationException("Root cube tile has no parent.");
        }

        return new CubeTileKey(Face, Level - 1, X >> 1, Y >> 1);
    }

    public CubeTileKey Child(int childX, int childY)
    {
        if (childX is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(childX), childX, "Child X must be 0 or 1.");
        }

        if (childY is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(childY), childY, "Child Y must be 0 or 1.");
        }

        if (Level == MaxLevel)
        {
            throw new InvalidOperationException($"Cube tile level {MaxLevel} cannot be subdivided.");
        }

        return new CubeTileKey(Face, Level + 1, (X << 1) | childX, (Y << 1) | childY);
    }

    public CubeFacePosition PositionAt(double localU, double localV)
    {
        if (!double.IsFinite(localU) || localU is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(localU), localU, "Local U must be finite and in [0, 1].");
        }

        if (!double.IsFinite(localV) || localV is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(localV), localV, "Local V must be finite and in [0, 1].");
        }

        var axisTileCount = AxisTileCount;
        var faceU = -1.0 + (2.0 * (X + localU) / axisTileCount);
        var faceV = -1.0 + (2.0 * (Y + localV) / axisTileCount);
        return new CubeFacePosition(Face, faceU, faceV);
    }

    public override string ToString()
    {
        return $"{Face}/L{Level:D2}/{X}/{Y}";
    }
}
