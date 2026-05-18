using System.Numerics;

namespace Aquarium.Zyphos;

public readonly record struct ZyphosCameraShot(
    Vector3 CameraPosition,
    Vector3 CameraTarget,
    Vector3 ParentAnchor,
    Vector3 TrackedCenter,
    float EffectiveDistance,
    float MinimumDistance,
    float MaximumDistance,
    ZyphosSpatialDomainKey DomainKey,
    ZyphosSpatialDomainKey? ParentDomainKey);

public static class ZyphosCameraComposer
{
    public static ZyphosCameraShot Compose(
        ZyphosSpatialDomainKey domainKey,
        float yaw,
        float pitch,
        float requestedDistance,
        float timeSeconds)
    {
        var domain = ZyphosSpatialDomainCatalog.GetRequired(domainKey);
        var parentDomain = ZyphosSpatialDomainCatalog.ParentOf(domain);
        var domainPose = domain.Pose(timeSeconds);
        var parentPose = parentDomain?.Pose(timeSeconds) ?? domainPose;
        var parent = parentPose.Center;
        var tracked = domainPose.Center;
        var minimumDistance = MinimumDistanceFor(domainPose);
        var maximumDistance = MaximumDistanceFor(domainPose, parentPose, parentDomain is not null);
        var effectiveDistance = Math.Clamp(requestedDistance, minimumDistance, maximumDistance);
        var orbitDirection = OrbitDirection(yaw, pitch);
        var cameraPosition = parent + orbitDirection * effectiveDistance;
        return new ZyphosCameraShot(cameraPosition, parent, parent, tracked, effectiveDistance, minimumDistance, maximumDistance, domain.Key, domain.ParentKey);
    }

    public static string DisplayName(ZyphosSpatialDomainKey domainKey)
    {
        var domain = ZyphosSpatialDomainCatalog.GetRequired(domainKey);
        return $"{domain.Label} {domain.Kind.ToLowerInvariant()}";
    }

    private static Vector3 OrbitDirection(float yaw, float pitch)
    {
        var horizontal = MathF.Cos(pitch);
        return Vector3.Normalize(new Vector3(
            MathF.Sin(yaw) * horizontal,
            -MathF.Cos(yaw) * horizontal,
            MathF.Sin(pitch)));
    }

    private static float MinimumDistanceFor(ZyphosSpatialDomainPose domainPose)
    {
        return Math.Clamp(domainPose.Radius * 1.65f, 0.035f, 8.0f);
    }

    private static float MaximumDistanceFor(ZyphosSpatialDomainPose domainPose, ZyphosSpatialDomainPose parentPose, bool hasParent)
    {
        var parentSpan = hasParent ? Vector3.Distance(parentPose.Center, domainPose.Center) + parentPose.Radius * 0.25f : 0.0f;
        return MathF.Max(8.0f, MathF.Max(domainPose.Radius * 12.0f, parentSpan + domainPose.Radius * 5.0f));
    }
}
