namespace Aquarium.Engine;

public readonly record struct GraphicsSettings(
    int RenderDebugMode,
    float SceneExposure,
    float BloomIntensity,
    float BloomVeilIntensity,
    float MediumCompositeIntensity)
{
    public const int MinRenderDebugMode = 0;
    public const int MaxRenderDebugMode = 11;
    public const float MinSceneExposure = 0.02f;
    public const float MaxSceneExposure = 1.2f;
    public const float MinBloomIntensity = 0.0f;
    public const float MaxBloomIntensity = 0.24f;
    public const float MinBloomVeilIntensity = 0.0f;
    public const float MaxBloomVeilIntensity = 0.08f;
    public const float MinMediumCompositeIntensity = 0.0f;
    public const float MaxMediumCompositeIntensity = 1.0f;

    public static GraphicsSettings Default { get; } = new(
        RenderDebugMode: 0,
        SceneExposure: 0.16f,
        BloomIntensity: 0.072f,
        BloomVeilIntensity: 0.014f,
        MediumCompositeIntensity: 0.0f);

    public GraphicsSettings Normalized()
    {
        return new GraphicsSettings(
            Math.Clamp(RenderDebugMode, MinRenderDebugMode, MaxRenderDebugMode),
            Math.Clamp(SceneExposure, MinSceneExposure, MaxSceneExposure),
            Math.Clamp(BloomIntensity, MinBloomIntensity, MaxBloomIntensity),
            Math.Clamp(BloomVeilIntensity, MinBloomVeilIntensity, MaxBloomVeilIntensity),
            Math.Clamp(MediumCompositeIntensity, MinMediumCompositeIntensity, MaxMediumCompositeIntensity));
    }
}
