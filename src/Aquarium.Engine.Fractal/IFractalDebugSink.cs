namespace Aquarium.Engine.Fractal;

public interface IFractalDebugSink
{
    void Record(string channel, string key, double value);
}
