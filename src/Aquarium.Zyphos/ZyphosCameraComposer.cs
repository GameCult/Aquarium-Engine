using System.Numerics;

namespace Aquarium.Zyphos;

public enum ZyphosCameraFrame
{
    Planet,
    Umbros,
    Binary,
}

public readonly record struct ZyphosCameraShot(
    Vector3 CameraPosition,
    Vector3 CameraTarget,
    Vector3 ParentAnchor,
    Vector3 TrackedCenter,
    float EffectiveDistance);

public static class ZyphosCameraComposer
{
    public static ZyphosCameraShot Compose(
        ZyphosCameraFrame frame,
        float yaw,
        float pitch,
        float requestedDistance,
        float timeSeconds)
    {
        var parent = ZyphosUmbrosSystem.ZyphosCenter;
        var tracked = frame switch
        {
            ZyphosCameraFrame.Umbros => ZyphosUmbrosSystem.UmbrosCenter(timeSeconds),
            ZyphosCameraFrame.Binary => ZyphosUmbrosSystem.ZyphosCenter + (ZyphosUmbrosSystem.UmbrosCenter(timeSeconds) - ZyphosUmbrosSystem.ZyphosCenter) * 0.5f,
            _ => ZyphosUmbrosSystem.ZyphosCenter,
        };
        var desiredTarget = frame == ZyphosCameraFrame.Planet ? tracked : parent;
        var subjectSpan = Vector3.Distance(parent, tracked);
        var effectiveDistance = MathF.Max(requestedDistance, subjectSpan * 1.35f);
        var orbitDirection = OrbitDirection(yaw, pitch);
        var cameraPosition = desiredTarget + orbitDirection * effectiveDistance;
        return new ZyphosCameraShot(cameraPosition, desiredTarget, parent, tracked, effectiveDistance);
    }

    public static string DisplayName(ZyphosCameraFrame frame)
    {
        return frame switch
        {
            ZyphosCameraFrame.Umbros => "Umbros frame",
            ZyphosCameraFrame.Binary => "Binary frame",
            _ => "Planet frame",
        };
    }

    private static Vector3 OrbitDirection(float yaw, float pitch)
    {
        var horizontal = MathF.Cos(pitch);
        return Vector3.Normalize(new Vector3(
            MathF.Sin(yaw) * horizontal,
            -MathF.Cos(yaw) * horizontal,
            MathF.Sin(pitch)));
    }
}
