using Aquarium.Engine.Audio;
using Aquarium.Engine.Input;
using Aquarium.Engine.Render;
using Aquarium.Engine.Ui;

namespace Aquarium.Engine;

public interface IAquariumRuntime : IDisposable
{
    AquariumRuntimeOptions Options { get; }

    AquariumFrame Frame { get; }

    GraphicsSettings GraphicsSettings { get; set; }

    AquariumRenderPlan RenderPlan { get; }

    AquariumUiDocument Ui { get; }

    AquariumSynthDocument Synth { get; }

    void Start();

    void Update(float deltaSeconds, InputState input);

    AquariumFrame ComposeFrame(AquariumFrame frame, AquariumFrameInput input);

    void FlushState();
}

public interface IAquariumRuntimeFactory
{
    IAquariumRuntime Create(AquariumRuntimeOptions options);
}

public readonly record struct AquariumRuntimeOptions(bool Headless, string? CultCachePath, int? RenderDebugModeOverride = null);

