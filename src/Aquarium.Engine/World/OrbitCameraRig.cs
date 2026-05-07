using Stride.Core.Mathematics;

namespace Aquarium.Engine;

public sealed class OrbitCameraRig
{
    public OrbitCameraRig(Vector2 target, float yawRadians, float pitchRadians, float distance)
    {
        Target = target;
        YawRadians = yawRadians;
        PitchRadians = pitchRadians;
        Distance = MathF.Max(1.0f, distance);
    }

    public Vector2 Target { get; private set; }

    public float YawRadians { get; private set; }

    public float PitchRadians { get; private set; }

    public float Distance { get; private set; }

    public Vector3 Position
    {
        get
        {
            var horizontal = Distance * MathF.Cos(PitchRadians);
            var x = Target.X + horizontal * MathF.Cos(YawRadians);
            var y = Target.Y + horizontal * MathF.Sin(YawRadians);
            var z = Distance * MathF.Sin(PitchRadians);
            return new Vector3(x, y, z);
        }
    }

    public void Orbit(float yawDeltaRadians, float pitchDeltaRadians)
    {
        YawRadians += yawDeltaRadians;
        PitchRadians = MathUtil.Clamp(
            PitchRadians + pitchDeltaRadians,
            MathUtil.DegreesToRadians(5.0f),
            MathUtil.DegreesToRadians(85.0f));
    }

    public void Zoom(float wheelSteps)
    {
        Distance = MathF.Max(1.0f, Distance * MathF.Pow(0.92f, wheelSteps));
    }

    public void Pan(Vector2 gridDelta)
    {
        Target += gridDelta;
    }
}
