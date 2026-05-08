using Aquarium.Engine.Input;

namespace Aquarium.Engine;

public interface IAquariumRuntime : IDisposable
{
    AquariumRuntimeOptions Options { get; }

    AquariumFrame Frame { get; }

    void Start();

    void Update(float deltaSeconds, InputState input);
}

public interface IAquariumRuntimeFactory
{
    IAquariumRuntime Create(AquariumRuntimeOptions options);
}

public readonly record struct AquariumRuntimeOptions(bool Headless, string? CultCachePath);

