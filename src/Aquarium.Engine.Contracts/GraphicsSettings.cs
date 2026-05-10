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
    float MediumNoiseContrast,
    float MediumGridFogDensity,
    float MediumPrimitiveFogDensity,
    float MediumNoiseSpeed)
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
    public const float MaxMediumFogDensity = 64.0f;
    public const float MinMediumFogHeightFalloff = 0.0f;
    public const float MaxMediumFogHeightFalloff = 0.25f;
    public const float MinMediumNoiseScale = 0.005f;
    public const float MaxMediumNoiseScale = 64.0f;
    public const float MinMediumNoiseContrast = 0.0f;
    public const float MaxMediumNoiseContrast = 32.0f;
    public const float MinMediumGridFogDensity = 0.0f;
    public const float MaxMediumGridFogDensity = 64.0f;
    public const float MinMediumPrimitiveFogDensity = 0.0f;
    public const float MaxMediumPrimitiveFogDensity = 64.0f;
    public const float MinMediumNoiseSpeed = 0.0f;
    public const float MaxMediumNoiseSpeed = 4.0f;

    public static GraphicsSettings Default { get; } = new(
        RenderDebugMode: 0,
        SceneExposure: 0.16f,
        BloomIntensity: 0.072f,
        BloomVeilIntensity: 0.014f,
        MediumCompositeIntensity: 0.0f,
        MediumDebugStep: 12,
        MediumFogDensity: 1.0f,
        MediumFogHeightFalloff: 0.04f,
        MediumNoiseScale: 1.0f,
        MediumNoiseContrast: 1.0f,
        MediumGridFogDensity: 1.0f,
        MediumPrimitiveFogDensity: 1.0f,
        MediumNoiseSpeed: 0.12f);

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
            Math.Clamp(MediumNoiseContrast, MinMediumNoiseContrast, MaxMediumNoiseContrast),
            Math.Clamp(MediumGridFogDensity, MinMediumGridFogDensity, MaxMediumGridFogDensity),
            Math.Clamp(MediumPrimitiveFogDensity, MinMediumPrimitiveFogDensity, MaxMediumPrimitiveFogDensity),
            Math.Clamp(MediumNoiseSpeed, MinMediumNoiseSpeed, MaxMediumNoiseSpeed));
    }
}
