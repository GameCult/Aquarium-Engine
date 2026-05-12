using Aquarium.Engine.Render;

namespace Aquarium.Epiphany;

public static class EpiphanyRenderPlan
{
    public const int RoleAgentCount = 7;
    public const int BodyVisualCount = RoleAgentCount + 2;
    public const int SelfVisualIndex = 0;
    public const int RoleAgentBaseIndex = 1;
    public const int CursorVisualIndex = BodyVisualCount - 1;

    public static AquariumRenderPlan Create()
    {
        var app = new AquariumApp();
        app.Shaders
            .Core("D3D12Grid.hlsl", "D3D12Scene.hlsl", "D3D12Post.hlsl")
            .BodyLibrary("D3D12AgentCharacters.hlsli")
            .BodyShader("D3D12SelfBody.hlsl")
            .BodyShader("D3D12FaceAgent.hlsl")
            .BodyShader("D3D12ImaginationAgent.hlsl")
            .BodyShader("D3D12EyesAgent.hlsl")
            .BodyShader("D3D12BodyAgent.hlsl")
            .BodyShader("D3D12HandsAgent.hlsl")
            .BodyShader("D3D12SoulAgent.hlsl")
            .BodyShader("D3D12LifeAgent.hlsl")
            .BodyShader("D3D12CursorBody.hlsl");

        var gridHeight = app.RenderTargets.Create("grid-height")
            .FixedSize(128, 128)
            .Format(RenderFormat.R16Float)
            .Register();
        var scene = app.RenderTargets.Hdr("scene");
        app.Cameras.Orbit("main");
        app.Graph.Pass("grid-height").Fullscreen();
        app.Graph.Pass("scene").Fullscreen();
        app.Graph.Pass("body-proxies").Proxy();
        app.Features.Bloom(scene.Color);
        app.Features.Presentation(scene.Color);
        app.Features.DirectWriteOverlay();
        app.Debug.View("Grid Height", gridHeight).View("Scene", scene.Color);
        return app.Plan;
    }
}
