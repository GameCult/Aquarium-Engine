using Aquarium.Engine.Input;

namespace Aquarium.Engine.Render;

public interface IAquariumRenderer : IDisposable
{
    int RenderDebugMode { get; set; }

    bool DebugUiVisible { get; set; }

    void UpdateDebugUi(InputState input);

    void CycleRenderDebugMode();

    GraphicsSettings CaptureGraphicsSettings();

    void ApplyGraphicsSettings(GraphicsSettings settings);

    void Render(AquariumFrame frame);
}
