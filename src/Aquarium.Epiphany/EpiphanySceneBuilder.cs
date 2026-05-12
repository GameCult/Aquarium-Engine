using System.Numerics;
using Aquarium.Engine;
using Aquarium.Engine.Render;

namespace Aquarium.Epiphany;

public static class EpiphanySceneBuilder
{
    private const int GridHeightBrushCount = EpiphanyRenderPlan.RoleAgentCount + 1;
    private const float BodyGridClearanceRadiusScale = 2.0f;
    private const float SelfGravityRadius = 17.0f;
    private const float SunRadius = 1.12f;
    private const float CursorBodyRadius = 0.56f;
    private const float CursorBodyBoundRadius = 0.88f;
    private static readonly float[] AgentOrbitPhase =
    [
        0.23f,
        2.91f,
        5.47f,
        1.36f,
        4.18f,
        0.97f,
        3.74f,
    ];
    private static readonly float[] AgentOrbitSpeed =
    [
        0.0437f,
        -0.0613f,
        0.0789f,
        0.0271f,
        0.0967f,
        -0.0521f,
        0.0695f,
    ];
    private static readonly float[] AgentOrbitRadius =
    [
        4.1f,
        5.08f,
        5.63f,
        6.52f,
        7.24f,
        8.11f,
        8.92f,
    ];

    public static AquariumSceneState Build(AquariumFrame frame, float previousTimeSeconds, Vector2 previousCursorWorld)
    {
        var brushes = BuildHeightFieldBrushes(frame.TimeSeconds);
        return new AquariumSceneState
        {
            HeightFieldBrushes = brushes,
            SdfObjects = BuildSdfObjects(frame, previousTimeSeconds, previousCursorWorld, brushes),
            SdfLights = BuildSdfLights(frame, brushes),
        };
    }

    public static Vector2 ProjectMouseToGridPlane(Vector2 mousePosition, int width, int height, Vector3 cameraPosition, Vector2 gridCenter)
    {
        if (width <= 0 || height <= 0)
        {
            return gridCenter;
        }

        var pixel = new Vector2(
            Math.Clamp(mousePosition.X, 0.0f, MathF.Max(width - 1.0f, 0.0f)),
            MathF.Max(height, 1.0f) - Math.Clamp(mousePosition.Y, 0.0f, MathF.Max(height - 1.0f, 0.0f)));
        var rayDirection = RayDirectionForPixel(pixel, width, height, cameraPosition, gridCenter);
        if (MathF.Abs(rayDirection.Z) < 0.0001f)
        {
            return gridCenter;
        }

        var travel = -cameraPosition.Z / rayDirection.Z;
        if (travel <= 0.0f || !float.IsFinite(travel))
        {
            return gridCenter;
        }

        var world = cameraPosition + rayDirection * travel;
        return new Vector2(world.X, world.Y);
    }

    private static AquariumHeightFieldBrush[] BuildHeightFieldBrushes(float timeSeconds)
    {
        var brushes = new AquariumHeightFieldBrush[GridHeightBrushCount];
        brushes[0] = new AquariumHeightFieldBrush(
            Vector2.Zero,
            SelfGravityRadius,
            2.85f,
            -1.34f,
            0.18f,
            MathF.Tau,
            0.82f,
            1.25f);

        for (var index = 0; index < EpiphanyRenderPlan.RoleAgentCount; index++)
        {
            var radius = AgentRadius(index);
            var center = AgentAnchor(index, timeSeconds);
            brushes[index + 1] = new AquariumHeightFieldBrush(
                center,
                3.8f + radius * 2.5f,
                2.1f,
                -0.42f,
                0.022f,
                2.4f,
                1.35f,
                0.0f);
        }

        return brushes;
    }

    private static AquariumSdfObject[] BuildSdfObjects(AquariumFrame frame, float previousTimeSeconds, Vector2 previousCursorWorld, IReadOnlyList<AquariumHeightFieldBrush> brushes)
    {
        var SdfObjects = new AquariumSdfObject[EpiphanyRenderPlan.SdfVisualCount];
        var selfCenter = BodyCenterAtGridHeight(frame.TimeSeconds, Vector2.Zero, SunRadius, brushes);
        SdfObjects[EpiphanyRenderPlan.SelfVisualIndex] = new AquariumSdfObject(
            new Vector4(selfCenter, SunRadius),
            new Vector4(selfCenter, 0.0f),
            new Vector4(1.0f, 1.0f, 0.0f, 0.0f));

        for (var index = 0; index < EpiphanyRenderPlan.RoleAgentCount; index++)
        {
            var visualIndex = EpiphanyRenderPlan.RoleAgentBaseIndex + index;
            var radius = AgentRadius(index);
            var center = AgentCenterAtGridHeight(index, frame.TimeSeconds, brushes);
            var previousBrushes = BuildHeightFieldBrushes(previousTimeSeconds);
            var previousCenter = AgentCenterAtGridHeight(index, previousTimeSeconds, previousBrushes);
            var activity = 0.45f + 0.45f * Hash21(index + 2.7f, 31.0f);
            var heartbeat = 0.5f + 0.5f * MathF.Sin(frame.TimeSeconds * (1.2f + index * 0.09f) + index * 1.37f);
            var pressure = 0.25f + 0.25f * MathF.Sin(frame.TimeSeconds * 0.31f + index * 0.71f);

            SdfObjects[visualIndex] = new AquariumSdfObject(
                new Vector4(center, radius),
                new Vector4(previousCenter, 0.0f),
                new Vector4(activity, heartbeat, pressure, 0.0f));
        }

        var cursorCenter = new Vector3(frame.CursorWorld, CursorBodyRadius);
        var previousCursorCenter = new Vector3(previousCursorWorld, CursorBodyRadius);
        SdfObjects[EpiphanyRenderPlan.CursorVisualIndex] = new AquariumSdfObject(
            new Vector4(cursorCenter, CursorBodyBoundRadius),
            new Vector4(previousCursorCenter, -1.0f),
            new Vector4(1.0f, 0.5f, 0.0f, 0.0f));

        return SdfObjects;
    }

    private static AquariumSdfLight[] BuildSdfLights(AquariumFrame frame, IReadOnlyList<AquariumHeightFieldBrush> brushes)
    {
        var selfCenter = BodyCenterAtGridHeight(frame.TimeSeconds, Vector2.Zero, SunRadius, brushes);
        return
        [
            new AquariumSdfLight(
                new Vector4(selfCenter, SunRadius),
                new Vector4(10.0f, 8.7f, 4.2f, SdfFieldId(EpiphanyRenderPlan.SelfVisualIndex)))
        ];
    }

    private static float SdfFieldId(int objectIndex)
    {
        return 10.0f + objectIndex;
    }

    private static Vector3 BodyCenterAtGridHeight(float timeSeconds, Vector2 world, float radius, IReadOnlyList<AquariumHeightFieldBrush> brushes)
    {
        return new Vector3(world.X, world.Y, EvaluateGridHeight(timeSeconds, world, brushes) + radius * BodyGridClearanceRadiusScale);
    }

    private static Vector3 AgentCenterAtGridHeight(int index, float timeSeconds, IReadOnlyList<AquariumHeightFieldBrush> brushes)
    {
        var radius = AgentRadius(index);
        var xy = AgentAnchor(index, timeSeconds);
        return new Vector3(xy.X, xy.Y, EvaluateGridHeight(timeSeconds, xy, brushes) + radius * BodyGridClearanceRadiusScale);
    }

    private static float EvaluateGridHeight(float timeSeconds, Vector2 world, IReadOnlyList<AquariumHeightFieldBrush> brushes)
    {
        var slow = MathF.Sin((world.X * 0.08f + world.Y * 0.06f) + timeSeconds * 0.27f)
            * MathF.Sin((world.X * -0.04f + world.Y * 0.07f) - timeSeconds * 0.19f)
            * 0.035f;
        var height = slow;
        foreach (var brush in brushes)
        {
            height += GridBrushHeight(world, brush, timeSeconds);
        }

        return height;
    }

    private static float GridBrushHeight(Vector2 world, AquariumHeightFieldBrush brush, float timeSeconds)
    {
        var distanceValue = Vector2.Distance(world, brush.Center);
        if (distanceValue > brush.Radius)
        {
            return 0.0f;
        }

        var well = PowerPulse(distanceValue, brush.Radius, brush.Power);
        var normalized = Math.Clamp(distanceValue / MathF.Max(brush.Radius, 0.001f), 0.0f, 1.0f);
        var wavePhase = brush.WaveSinePower > 0.0f
            ? MathF.Pow(normalized, brush.WaveSinePower) * brush.WaveFrequency - timeSeconds * brush.WaveSpeed
            : distanceValue * brush.WaveFrequency - timeSeconds * brush.WaveSpeed;
        var ripple = brush.WaveSinePower > 0.0f ? MathF.Cos(wavePhase) : MathF.Sin(wavePhase);
        return brush.Amplitude * well + ripple * well * brush.WaveAmplitude;
    }

    private static Vector2 AgentAnchor(int index, float timeSeconds)
    {
        var phase = AgentOrbitPhase[index];
        var speed = AgentOrbitSpeed[index];
        var angleWander = 0.12f * MathF.Sin(timeSeconds * (0.037f + index * 0.0061f) + phase * 1.73f);
        var radiusWander = 0.16f * MathF.Sin(timeSeconds * (0.021f + index * 0.0047f) + phase * 2.41f);
        var angle = phase + timeSeconds * speed + angleWander;
        var orbitRadius = AgentOrbitRadius[index] + radiusWander;
        return new Vector2(
            MathF.Cos(angle) * orbitRadius,
            MathF.Sin(angle) * orbitRadius);
    }

    private static float AgentRadius(int index)
    {
        return Lerp(0.34f, 0.62f, Hash21(index, 19.7f));
    }

    private static Vector3 RayDirectionForPixel(Vector2 pixel, int width, int height, Vector3 cameraPosition, Vector2 gridCenter)
    {
        var resolution = new Vector2(width, height);
        var ndc = ((pixel * 2.0f) - resolution) / MathF.Max(height, 1.0f);
        var target = new Vector3(gridCenter.X, gridCenter.Y, 0.0f);
        var forward = Vector3.Normalize(target - cameraPosition);
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
        var up = Vector3.Cross(right, forward);
        return Vector3.Normalize(forward * 1.6f + right * ndc.X + up * ndc.Y);
    }

    private static float PowerPulse(float distanceValue, float radius, float power)
    {
        var normalized = Math.Clamp(distanceValue / MathF.Max(radius, 0.001f), 0.0f, 1.0f);
        var shaped = MathF.Pow(1.0f - normalized, power);
        return shaped * shaped * (3.0f - 2.0f * shaped);
    }

    private static float Hash21(float x, float y)
    {
        x = Frac(x * 123.34f);
        y = Frac(y * 456.21f);
        var d = x * (x + 45.32f) + y * (y + 45.32f);
        x += d;
        y += d;
        return Frac(x * y);
    }

    private static float Frac(float value)
    {
        return value - MathF.Floor(value);
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * t;
    }
}
