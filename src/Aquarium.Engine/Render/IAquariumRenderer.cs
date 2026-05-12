using Aquarium.Engine.Input;
using Aquarium.Engine.Audio;
using Aquarium.Engine.Ui;

namespace Aquarium.Engine.Render;

public interface IAquariumRenderer : IDisposable
{
    int RenderDebugMode { get; set; }

    bool DebugUiVisible { get; set; }

    bool HasPresentedReadyFrame { get; }

    AquariumSynthDocument DebugSynth { get; }

    void UpdateUi(InputState input, AquariumUiDocument clientUi);

    void CycleRenderDebugMode();

    GraphicsSettings CaptureGraphicsSettings();

    void ApplyGraphicsSettings(GraphicsSettings settings);

    void Render(AquariumFrame frame, int width, int height);
}
