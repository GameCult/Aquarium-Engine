using GameCult.Caching;
using MessagePack;

namespace Aquarium.Engine.State;

[CultDocument("epiphany.aquarium.graphics_settings", "epiphany.aquarium.graphics_settings.v0")]
[MessagePackObject]
public sealed class AquariumGraphicsSettingsState
{
    public const string PrimaryName = "aquarium-client-graphics";

    [Key(0)]
    [CultName]
    public string Name { get; set; } = PrimaryName;

    [Key(1)]
    public int RenderDebugMode { get; set; } = GraphicsSettings.Default.RenderDebugMode;

    [Key(2)]
    public float SceneExposure { get; set; } = GraphicsSettings.Default.SceneExposure;

    [Key(3)]
    public float BloomIntensity { get; set; } = GraphicsSettings.Default.BloomIntensity;

    [Key(4)]
    public float BloomVeilIntensity { get; set; } = GraphicsSettings.Default.BloomVeilIntensity;

    [Key(5)]
    public long SaveGeneration { get; set; }

    public static AquariumGraphicsSettingsState FromSettings(GraphicsSettings settings)
    {
        var normalized = settings.Normalized();
        return new AquariumGraphicsSettingsState
        {
            RenderDebugMode = normalized.RenderDebugMode,
            SceneExposure = normalized.SceneExposure,
            BloomIntensity = normalized.BloomIntensity,
            BloomVeilIntensity = normalized.BloomVeilIntensity
        };
    }

    public GraphicsSettings ToSettings()
    {
        return new GraphicsSettings(RenderDebugMode, SceneExposure, BloomIntensity, BloomVeilIntensity).Normalized();
    }
}
