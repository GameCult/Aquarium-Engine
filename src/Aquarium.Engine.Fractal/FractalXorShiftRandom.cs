namespace Aquarium.Engine.Fractal;

public sealed class FractalXorShiftRandom(uint seed) : IFractalRandom
{
    private uint state = seed == 0 ? 0xA341316Cu : seed;

    public double NextDouble()
    {
        var value = NextUInt32();
        return value / ((double)uint.MaxValue + 1.0);
    }

    private uint NextUInt32()
    {
        var value = state;
        value ^= value << 13;
        value ^= value >> 17;
        value ^= value << 5;
        state = value;
        return value;
    }
}
