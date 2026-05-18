namespace Aquarium.Engine.Fractal;

public interface IFractalClock
{
    double TimeSeconds { get; }

    ulong FrameIndex { get; }
}
