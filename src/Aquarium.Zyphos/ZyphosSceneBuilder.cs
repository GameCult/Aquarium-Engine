using System.Numerics;
using Aquarium.Engine.Render;

namespace Aquarium.Zyphos;

public static class ZyphosSceneBuilder
{
    public static AquariumSceneState Build(float timeSeconds, float previousTimeSeconds)
    {
        return new AquariumSceneState
        {
            TraceHeightFieldSurface = false,
            UseStarfieldBackground = true,
            HeightFieldBrushes = BuildHeightFieldBrushes(),
            SdfObjects = BuildSdfObjects(timeSeconds, previousTimeSeconds),
            SdfLights = BuildSdfLights(timeSeconds),
        };
    }

    private static AquariumHeightFieldBrush[] BuildHeightFieldBrushes()
    {
        return ZyphosFractalTerrain.HeightBrushes;
    }

    private static AquariumSdfObject[] BuildSdfObjects(float timeSeconds, float previousTimeSeconds)
    {
        var planetCenter = ZyphosUmbrosSystem.ZyphosCenter;
        var rotation = ZyphosUmbrosSystem.MutualPhase(timeSeconds);
        var umbrosCenter = ZyphosUmbrosSystem.UmbrosCenter(timeSeconds);
        var previousUmbrosCenter = ZyphosUmbrosSystem.UmbrosCenter(previousTimeSeconds);
        var starCenter = ZyphosUmbrosSystem.PrimaryStarCenter(timeSeconds);
        var previousStarCenter = ZyphosUmbrosSystem.PrimaryStarCenter(previousTimeSeconds);

        var objects = new AquariumSdfObject[ZyphosRenderPlan.SdfObjectCount];
        objects[ZyphosRenderPlan.PlanetIndex] = new AquariumSdfObject(
            new Vector4(planetCenter, ZyphosUmbrosSystem.ZyphosBoundRadius),
            new Vector4(planetCenter, 0.0f),
            new Vector4(ZyphosUmbrosSystem.ZyphosSurfaceRadius, rotation, ZyphosUmbrosSystem.SeaLevel, rotation));
        objects[ZyphosRenderPlan.UmbrosIndex] = new AquariumSdfObject(
            new Vector4(umbrosCenter, ZyphosUmbrosSystem.UmbrosBoundRadius),
            new Vector4(previousUmbrosCenter, 0.0f),
            new Vector4(ZyphosUmbrosSystem.UmbrosSurfaceRadius, rotation, ZyphosUmbrosSystem.CenterSeparation, 0.0f));
        objects[ZyphosRenderPlan.StarIndex] = new AquariumSdfObject(
            new Vector4(starCenter, ZyphosUmbrosSystem.PrimaryStarVisualRadius),
            new Vector4(previousStarCenter, 0.0f),
            new Vector4(ZyphosUmbrosSystem.PrimaryStarVisualRadius, rotation, 0.0f, 0.0f));

        return objects;
    }

    private static AquariumSdfLight[] BuildSdfLights(float timeSeconds)
    {
        var starCenter = ZyphosUmbrosSystem.PrimaryStarCenter(timeSeconds);
        return
        [
            new AquariumSdfLight(
                new Vector4(starCenter, ZyphosUmbrosSystem.PrimaryStarVisualRadius),
                new Vector4(3.2f, 2.5f, 1.8f, -100.0f)),
            new AquariumSdfLight(
                new Vector4(ZyphosUmbrosSystem.UmbrosCenter(timeSeconds), ZyphosUmbrosSystem.UmbrosSurfaceRadius),
                new Vector4(0.06f, 0.08f, 0.11f, -101.0f))
        ];
    }
}
