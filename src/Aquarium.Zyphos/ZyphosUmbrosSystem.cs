using System.Numerics;

namespace Aquarium.Zyphos;

public static class ZyphosUmbrosSystem
{
    public const float ZyphosSurfaceRadius = 3.8f;
    public const float ZyphosBoundRadius = 4.52f;
    public const float UmbrosRadiusRatio = 0.9f;
    public const float SeparationInZyphosRadii = 8.0f;
    public const float SeaLevel = 0.56f;
    public const float MutualPhaseRate = 0.032f;

    public static float UmbrosSurfaceRadius => ZyphosSurfaceRadius * UmbrosRadiusRatio;

    public static float UmbrosBoundRadius => UmbrosSurfaceRadius + 0.42f;

    public static float CenterSeparation => ZyphosSurfaceRadius * SeparationInZyphosRadii;

    public static float UmbrosAngularDiameterDegrees =>
        MathF.Asin(Math.Clamp(UmbrosSurfaceRadius / CenterSeparation, -1.0f, 1.0f)) * 2.0f * 180.0f / MathF.PI;

    public static Vector3 ZyphosCenter => new(0.0f, 0.0f, ZyphosSurfaceRadius + 0.48f);

    public static float MutualPhase(float timeSeconds)
    {
        return timeSeconds * MutualPhaseRate;
    }

    public static Vector3 UmbrosDirection(float timeSeconds)
    {
        // Zyphos renders in the mutually locked local frame: Umbros is fixed in the sky while the primary star phase moves.
        _ = timeSeconds;
        return Vector3.UnitX;
    }

    public static Vector3 UmbrosCenter(float timeSeconds)
    {
        return ZyphosCenter + UmbrosDirection(timeSeconds) * CenterSeparation;
    }

    public static Vector3 PrimaryStarDirection(float timeSeconds)
    {
        var phase = MutualPhase(timeSeconds);
        return Vector3.Normalize(new Vector3(MathF.Cos(phase), MathF.Sin(phase), 0.18f));
    }

    public static Vector3 PrimaryStarCenter(float timeSeconds)
    {
        return ZyphosCenter + PrimaryStarDirection(timeSeconds) * (CenterSeparation * 3.2f);
    }
}
