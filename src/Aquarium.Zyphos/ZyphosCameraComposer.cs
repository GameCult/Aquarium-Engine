using System.Numerics;

namespace Aquarium.Zyphos;

public readonly record struct ZyphosCameraShot(
    Vector3 CameraPosition,
    Vector3 CameraTarget,
    Vector3 ParentAnchor,
    Vector3 TrackedCenter,
    float EffectiveDistance,
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
        var subjectSpan = Vector3.Distance(parent, tracked) + domainPose.Radius + (parentDomain is null ? 0.0f : parentPose.Radius * 0.35f);
        var effectiveDistance = MathF.Max(requestedDistance, MathF.Max(domain.NavigationRadius, subjectSpan * 1.22f));
        var orbitDirection = OrbitDirection(yaw, pitch);
        var cameraPosition = parent + orbitDirection * effectiveDistance;
        return new ZyphosCameraShot(cameraPosition, parent, parent, tracked, effectiveDistance, domain.Key, domain.ParentKey);
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
}
