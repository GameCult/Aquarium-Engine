using Aquarium.Engine.Render;

namespace Aquarium.LocalCast;

public static class LocalCastRenderPlan
{
    public static AquariumRenderPlan Create()
    {
        var app = new AquariumApp();
        var scene = app.RenderTargets.Hdr("scene");
        app.Cameras.Orbit("main");
        app.Graph.Pass("scene").Fullscreen();
        app.Features.Bloom(scene.Color);
        app.Features.Presentation(scene.Color);
        app.Features.DirectWriteOverlay();
        app.Debug.View("Scene", scene.Color);
        return app.Plan;
    }
}
