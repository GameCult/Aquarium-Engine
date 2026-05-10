using System.Numerics;
using Aquarium.Engine.Input;

namespace Aquarium.Engine;

public sealed class OrbitCameraRig
{
    public const float MinimumDistance = 5.0f;
    public const float MaximumDistance = 40.0f;

    public OrbitCameraRig(Vector2 target, float yawRadians, float pitchRadians, float distance)
    {
        Target = target;
        YawRadians = yawRadians;
        PitchRadians = pitchRadians;
        Distance = ClampDistance(distance);
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
        PitchRadians = Math.Clamp(
            PitchRadians + pitchDeltaRadians,
            Angle.DegreesToRadians(5.0f),
            Angle.DegreesToRadians(85.0f));
    }

    public void Zoom(float wheelSteps)
    {
        Distance = ClampDistance(Distance * MathF.Pow(0.92f, wheelSteps));
    }

    public void Pan(Vector2 gridDelta)
    {
        Target += gridDelta;
    }

    public void ApplyInput(InputState input, float deltaSeconds)
    {
        if (input.MiddleMouseDown)
        {
            Orbit(input.MouseDelta.X * -0.0065f, input.MouseDelta.Y * 0.0065f);
        }

        if (MathF.Abs(input.WheelDelta) > 0.0f)
        {
            Zoom(input.WheelDelta);
        }

        var keyboardPan = Vector2.Zero;
        if (input.IsKeyDown(KeyCode.W))
        {
            keyboardPan.Y += 1.0f;
        }

        if (input.IsKeyDown(KeyCode.S))
        {
            keyboardPan.Y -= 1.0f;
        }

        if (input.IsKeyDown(KeyCode.D))
        {
            keyboardPan.X += 1.0f;
        }

        if (input.IsKeyDown(KeyCode.A))
        {
            keyboardPan.X -= 1.0f;
        }

        if (keyboardPan != Vector2.Zero)
        {
            Pan(GridPanVector(Vector2.Normalize(keyboardPan) * Distance * 0.45f * deltaSeconds));
        }

        if (input.RightMouseDown && input.MouseDelta != Vector2.Zero)
        {
            Pan(GridPanVector(new Vector2(-input.MouseDelta.X, input.MouseDelta.Y) * Distance * 0.0018f));
        }
    }

    private Vector2 GridPanVector(Vector2 localDelta)
    {
        var forward = Vector2.Normalize(new Vector2(
            MathF.Cos(YawRadians + MathF.PI),
            MathF.Sin(YawRadians + MathF.PI)));
        var right = new Vector2(forward.Y, -forward.X);

        return right * localDelta.X + forward * localDelta.Y;
    }

    private static float ClampDistance(float distance)
    {
        return Math.Clamp(distance, MinimumDistance, MaximumDistance);
    }
}
