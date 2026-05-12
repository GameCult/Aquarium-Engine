using Aquarium.Engine.Render;

namespace Aquarium.Epiphany;

public static class EpiphanyRenderPlan
{
    public const int RoleAgentCount = 7;
    public const int SdfVisualCount = RoleAgentCount + 2;
    public const int SelfVisualIndex = 0;
    public const int RoleAgentBaseIndex = 1;
    public const int CursorVisualIndex = SdfVisualCount - 1;

    public static AquariumRenderPlan Create()
    {
        var app = new AquariumApp();
        app.Shaders
            .Core("D3D12HeightField.hlsl", "D3D12Scene.hlsl", "D3D12Post.hlsl")
            .SdfLibrary("D3D12AgentCharacters.hlsli")
            .SdfShader("D3D12SelfBody.hlsl")
            .SdfShader("D3D12FaceAgent.hlsl")
            .SdfShader("D3D12ImaginationAgent.hlsl")
            .SdfShader("D3D12EyesAgent.hlsl")
            .SdfShader("D3D12BodyAgent.hlsl")
            .SdfShader("D3D12HandsAgent.hlsl")
            .SdfShader("D3D12SoulAgent.hlsl")
            .SdfShader("D3D12LifeAgent.hlsl")
            .SdfShader("D3D12CursorBody.hlsl");

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
