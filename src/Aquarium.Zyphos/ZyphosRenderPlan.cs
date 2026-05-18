using Aquarium.Engine.Render;

namespace Aquarium.Zyphos;

public static class ZyphosRenderPlan
{
    public const int PlanetIndex = 0;
    public const int UmbrosIndex = 1;
    public const int SdfObjectCount = 2;

    public static AquariumRenderPlan Create()
    {
        var app = new AquariumApp();
        app.Shaders
            .Core("D3D12HeightField.hlsl", "D3D12Scene.hlsl", "D3D12Post.hlsl")
            .SdfShader("D3D12ZyphosPlanet.hlsl")
            .SdfShader("D3D12ZyphosUmbros.hlsl");

        var heightField = app.RenderTargets.Create("height-field")
            .FixedSize(128, 128)
            .Format(RenderFormat.R16Float)
            .Register();
        var scene = app.RenderTargets.Hdr("scene");
        app.Cameras.Orbit("main");
        app.Graph.Pass("height-field").Fullscreen();
        app.Graph.Pass("scene").Fullscreen();
        app.Graph.Pass("sdf-proxies").Proxy();
        app.Features.Bloom(scene.Color);
        app.Features.Presentation(scene.Color);
        app.Features.DirectWriteOverlay();
        app.Debug.View("Height Field", heightField).View("Scene", scene.Color);
        return app.Plan;
    }
}
