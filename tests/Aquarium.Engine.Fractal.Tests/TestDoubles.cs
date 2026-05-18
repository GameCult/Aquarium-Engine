using Aquarium.Engine.Fractal;

namespace Aquarium.Engine.Fractal.Tests;

internal sealed class TestFractalClock(double timeSeconds, ulong frameIndex) : IFractalClock
{
    public double TimeSeconds { get; } = timeSeconds;

    public ulong FrameIndex { get; } = frameIndex;
}

internal sealed class TestFractalDebugSink : IFractalDebugSink
{
    public Dictionary<(string Channel, string Key), double> Values { get; } = [];

    public void Record(string channel, string key, double value)
    {
        Values[(channel, key)] = value;
    }
}

internal sealed class TestFractalRandom(params double[] values) : IFractalRandom
{
    private int index;

    public double NextDouble()
    {
        if (values.Length == 0)
        {
            throw new InvalidOperationException("Test random has no values.");
        }

        var value = values[index % values.Length];
        index++;
        return value;
    }
}
