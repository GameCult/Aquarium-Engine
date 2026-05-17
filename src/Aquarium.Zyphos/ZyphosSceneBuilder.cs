using System.Numerics;
using Aquarium.Engine.Render;

namespace Aquarium.Zyphos;

public static class ZyphosSceneBuilder
{
    private const float PlanetSurfaceRadius = 3.8f;
    private const float PlanetBoundRadius = 4.52f;
    private const float MoonSurfaceRadius = 0.54f;
    private const float MoonBoundRadius = 0.70f;

    public static AquariumSceneState Build(float timeSeconds, float previousTimeSeconds)
    {
        return new AquariumSceneState
        {
            HeightFieldBrushes = BuildHeightFieldBrushes(),
            SdfObjects = BuildSdfObjects(timeSeconds, previousTimeSeconds),
            SdfLights = BuildSdfLights(timeSeconds),
        };
    }

    private static AquariumHeightFieldBrush[] BuildHeightFieldBrushes()
    {
        return
        [
            new AquariumHeightFieldBrush(
                Vector2.Zero,
                30.0f,
                2.6f,
                -0.18f,
                0.03f,
                1.9f,
                0.22f,
                1.1f)
        ];
    }

    private static AquariumSdfObject[] BuildSdfObjects(float timeSeconds, float previousTimeSeconds)
    {
        var planetCenter = new Vector3(0.0f, 0.0f, PlanetSurfaceRadius + 0.48f);
        var rotation = timeSeconds * 0.032f;
        var previousRotation = previousTimeSeconds * 0.032f;
        var moonCenter = MoonCenter(timeSeconds);
        var previousMoonCenter = MoonCenter(previousTimeSeconds);

        var objects = new AquariumSdfObject[ZyphosRenderPlan.SdfObjectCount];
        objects[ZyphosRenderPlan.PlanetIndex] = new AquariumSdfObject(
            new Vector4(planetCenter, PlanetBoundRadius),
            new Vector4(planetCenter, 0.0f),
            new Vector4(PlanetSurfaceRadius, rotation, 0.56f, previousRotation));
        objects[ZyphosRenderPlan.MoonIndex] = new AquariumSdfObject(
            new Vector4(moonCenter, MoonBoundRadius),
            new Vector4(previousMoonCenter, 0.0f),
            new Vector4(MoonSurfaceRadius, timeSeconds * 0.11f, PlanetSurfaceRadius, 0.0f));

        return objects;
    }

    private static AquariumSdfLight[] BuildSdfLights(float timeSeconds)
    {
        var sunPhase = timeSeconds * 0.015f;
        var sunCenter = new Vector3(
            -12.0f + MathF.Sin(sunPhase) * 2.4f,
            -18.0f,
            11.0f + MathF.Cos(sunPhase * 0.7f) * 1.3f);
        return
        [
            new AquariumSdfLight(
                new Vector4(sunCenter, 5.5f),
                new Vector4(8.6f, 7.9f, 6.7f, -100.0f)),
            new AquariumSdfLight(
                new Vector4(0.0f, 0.0f, PlanetSurfaceRadius + 0.48f, 4.0f),
                new Vector4(0.10f, 0.18f, 0.28f, -101.0f))
        ];
    }

    private static Vector3 MoonCenter(float timeSeconds)
    {
        var orbit = timeSeconds * 0.075f + 1.3f;
        var radius = 6.35f;
        return new Vector3(
            MathF.Cos(orbit) * radius,
            MathF.Sin(orbit) * radius * 0.62f,
            PlanetSurfaceRadius + 0.62f + MathF.Sin(orbit * 0.73f) * 1.2f);
    }
}
