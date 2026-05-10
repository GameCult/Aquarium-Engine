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
    public float MediumCompositeIntensity { get; set; } = GraphicsSettings.Default.MediumCompositeIntensity;

    [Key(6)]
    public int MediumDebugStep { get; set; } = GraphicsSettings.Default.MediumDebugStep;

    [Key(7)]
    public long SaveGeneration { get; set; }

    [Key(8)]
    public float MediumFogDensity { get; set; } = GraphicsSettings.Default.MediumFogDensity;

    [Key(9)]
    public float MediumFogHeightFalloff { get; set; } = GraphicsSettings.Default.MediumFogHeightFalloff;

    [Key(10)]
    public float MediumNoiseScale { get; set; } = GraphicsSettings.Default.MediumNoiseScale;

    [Key(11)]
    public float MediumNoiseContrast { get; set; } = GraphicsSettings.Default.MediumNoiseContrast;

    [Key(12)]
    public float MediumGridFogDensity { get; set; } = GraphicsSettings.Default.MediumGridFogDensity;

    [Key(13)]
    public float MediumPrimitiveFogDensity { get; set; } = GraphicsSettings.Default.MediumPrimitiveFogDensity;

    [Key(14)]
    public float MediumNoiseSpeed { get; set; } = GraphicsSettings.Default.MediumNoiseSpeed;

    public static AquariumGraphicsSettingsState FromSettings(GraphicsSettings settings)
    {
        var normalized = settings.Normalized();
        return new AquariumGraphicsSettingsState
        {
            RenderDebugMode = normalized.RenderDebugMode,
            SceneExposure = normalized.SceneExposure,
            BloomIntensity = normalized.BloomIntensity,
            BloomVeilIntensity = normalized.BloomVeilIntensity,
            MediumCompositeIntensity = normalized.MediumCompositeIntensity,
            MediumDebugStep = normalized.MediumDebugStep,
            MediumFogDensity = normalized.MediumFogDensity,
            MediumFogHeightFalloff = normalized.MediumFogHeightFalloff,
            MediumNoiseScale = normalized.MediumNoiseScale,
            MediumNoiseContrast = normalized.MediumNoiseContrast,
            MediumGridFogDensity = normalized.MediumGridFogDensity,
            MediumPrimitiveFogDensity = normalized.MediumPrimitiveFogDensity,
            MediumNoiseSpeed = normalized.MediumNoiseSpeed
        };
    }

    public GraphicsSettings ToSettings()
    {
        return new GraphicsSettings(
            RenderDebugMode,
            SceneExposure,
            BloomIntensity,
            BloomVeilIntensity,
            MediumCompositeIntensity,
            MediumDebugStep,
            MediumFogDensity,
            MediumFogHeightFalloff,
            MediumNoiseScale,
            MediumNoiseContrast,
            MediumGridFogDensity,
            MediumPrimitiveFogDensity,
            MediumNoiseSpeed).Normalized();
    }
}
