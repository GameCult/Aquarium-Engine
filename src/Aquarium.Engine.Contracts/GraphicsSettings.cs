namespace Aquarium.Engine;

public readonly record struct GraphicsSettings(
    int RenderDebugMode,
    float SceneExposure,
    float BloomIntensity,
    float BloomVeilIntensity,
    float MediumCompositeIntensity,
    int MediumDebugStep,
    float MediumFogDensity,
    float MediumFogHeightFalloff,
    float MediumNoiseScale,
    float MediumNoiseContrast)
{
    public const int MinRenderDebugMode = 0;
    public const int MaxRenderDebugMode = 13;
    public const float MinSceneExposure = 0.02f;
    public const float MaxSceneExposure = 1.2f;
    public const float MinBloomIntensity = 0.0f;
    public const float MaxBloomIntensity = 0.24f;
    public const float MinBloomVeilIntensity = 0.0f;
    public const float MaxBloomVeilIntensity = 0.08f;
    public const float MinMediumCompositeIntensity = 0.0f;
    public const float MaxMediumCompositeIntensity = 1.0f;
    public const int MinMediumDebugStep = 0;
    public const int MaxMediumDebugStep = 47;
    public const float MinMediumFogDensity = 0.0f;
    public const float MaxMediumFogDensity = 8.0f;
    public const float MinMediumFogHeightFalloff = 0.0f;
    public const float MaxMediumFogHeightFalloff = 1.5f;
    public const float MinMediumNoiseScale = 0.1f;
    public const float MaxMediumNoiseScale = 4.0f;
    public const float MinMediumNoiseContrast = 0.0f;
    public const float MaxMediumNoiseContrast = 4.0f;

    public static GraphicsSettings Default { get; } = new(
        RenderDebugMode: 0,
        SceneExposure: 0.16f,
        BloomIntensity: 0.072f,
        BloomVeilIntensity: 0.014f,
        MediumCompositeIntensity: 0.0f,
        MediumDebugStep: 12,
        MediumFogDensity: 1.0f,
        MediumFogHeightFalloff: 0.16f,
        MediumNoiseScale: 1.0f,
        MediumNoiseContrast: 1.0f);

    public GraphicsSettings Normalized()
    {
        return new GraphicsSettings(
            Math.Clamp(RenderDebugMode, MinRenderDebugMode, MaxRenderDebugMode),
            Math.Clamp(SceneExposure, MinSceneExposure, MaxSceneExposure),
            Math.Clamp(BloomIntensity, MinBloomIntensity, MaxBloomIntensity),
            Math.Clamp(BloomVeilIntensity, MinBloomVeilIntensity, MaxBloomVeilIntensity),
            Math.Clamp(MediumCompositeIntensity, MinMediumCompositeIntensity, MaxMediumCompositeIntensity),
            Math.Clamp(MediumDebugStep, MinMediumDebugStep, MaxMediumDebugStep),
            Math.Clamp(MediumFogDensity, MinMediumFogDensity, MaxMediumFogDensity),
            Math.Clamp(MediumFogHeightFalloff, MinMediumFogHeightFalloff, MaxMediumFogHeightFalloff),
            Math.Clamp(MediumNoiseScale, MinMediumNoiseScale, MaxMediumNoiseScale),
            Math.Clamp(MediumNoiseContrast, MinMediumNoiseContrast, MaxMediumNoiseContrast));
    }
}
