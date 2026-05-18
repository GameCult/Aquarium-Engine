using System.Numerics;

namespace Aquarium.Zyphos;

public readonly record struct ZyphosSpatialDomainKey(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct ZyphosSpatialDomainPose(Vector3 Center, float Radius);

public sealed record ZyphosSpatialDomain(
    ZyphosSpatialDomainKey Key,
    ZyphosSpatialDomainKey? ParentKey,
    string Label,
    string Detail,
    string Kind,
    float NavigationRadius,
    Func<float, ZyphosSpatialDomainPose> Pose);

public static class ZyphosSpatialDomainCatalog
{
    public static readonly ZyphosSpatialDomainKey Solar = new("zyphos/parent-star");
    public static readonly ZyphosSpatialDomainKey Orbital = new("zyphos/zyphos-umbros-orbit");
    public static readonly ZyphosSpatialDomainKey Planet = new("zyphos/planet");
    public static readonly ZyphosSpatialDomainKey Umbros = new("zyphos/umbros");
    public static readonly ZyphosSpatialDomainKey PlanetLatLong = new("zyphos/planet-latlong");
    public static readonly ZyphosSpatialDomainKey UmbrosLatLong = new("zyphos/umbros-latlong");
    public static readonly ZyphosSpatialDomainKey TerrainTile = new("cube/PositiveZ/L00/0/0:zyphos/first-fractal-terrain");
    public static readonly ZyphosSpatialDomainKey TerminatorShelvesTile = new("cube/PositiveX/L01/1/0:zyphos/terminator-shelves");
    public static readonly ZyphosSpatialDomainKey UmbrosTerrainTile = new("cube/PositiveZ/L00/0/0:umbros/first-fractal-terrain");
    public static readonly ZyphosSpatialDomainKey UmbrosNightCracksTile = new("cube/NegativeY/L01/0/1:umbros/night-cracks");

    private static readonly ZyphosSpatialDomain[] DomainList =
    [
        new(Solar, null, "Parent Star", "solar", "Solar", ZyphosUmbrosSystem.CenterSeparation * 5.4f, time => new ZyphosSpatialDomainPose(ZyphosUmbrosSystem.PrimaryStarCenter(time), ZyphosUmbrosSystem.CenterSeparation * 0.28f)),
        new(Orbital, Solar, "Zyphos-Umbros Orbit", "orbital", "Orbital", ZyphosUmbrosSystem.CenterSeparation * 1.9f, time => new ZyphosSpatialDomainPose(BinaryCenter(time), ZyphosUmbrosSystem.CenterSeparation * 0.5f)),
        new(Planet, Orbital, "Zyphos", "planet", "Planetary", ZyphosUmbrosSystem.ZyphosBoundRadius * 2.6f, _ => new ZyphosSpatialDomainPose(ZyphosUmbrosSystem.ZyphosCenter, ZyphosUmbrosSystem.ZyphosBoundRadius)),
        new(Umbros, Orbital, "Umbros", "twin", "Planetary", ZyphosUmbrosSystem.CenterSeparation * 1.35f, time => new ZyphosSpatialDomainPose(ZyphosUmbrosSystem.UmbrosCenter(time), ZyphosUmbrosSystem.UmbrosBoundRadius)),
        new(PlanetLatLong, Planet, "Lat/Long Surface", "surface", "LatLong", ZyphosUmbrosSystem.ZyphosBoundRadius * 1.55f, _ => new ZyphosSpatialDomainPose(ZyphosUmbrosSystem.ZyphosCenter, ZyphosUmbrosSystem.ZyphosSurfaceRadius)),
        new(TerrainTile, PlanetLatLong, "Zyphos Zenith Tile", "tile", "Cube tile", ZyphosUmbrosSystem.ZyphosBoundRadius * 1.22f, _ => new ZyphosSpatialDomainPose(ZyphosUmbrosSystem.ZyphosCenter + new Vector3(0.0f, 0.0f, ZyphosUmbrosSystem.ZyphosSurfaceRadius), ZyphosUmbrosSystem.ZyphosSurfaceRadius * 0.32f)),
        new(TerminatorShelvesTile, PlanetLatLong, "Terminator Shelves", "L1 tile", "Cube tile", ZyphosUmbrosSystem.ZyphosBoundRadius * 1.18f, _ => new ZyphosSpatialDomainPose(ZyphosUmbrosSystem.ZyphosCenter + Vector3.Normalize(new Vector3(1.0f, 0.0f, 0.2f)) * ZyphosUmbrosSystem.ZyphosSurfaceRadius, ZyphosUmbrosSystem.ZyphosSurfaceRadius * 0.25f)),
        new(UmbrosLatLong, Umbros, "Umbros Lat/Long", "surface", "LatLong", ZyphosUmbrosSystem.UmbrosBoundRadius * 1.65f, time => new ZyphosSpatialDomainPose(ZyphosUmbrosSystem.UmbrosCenter(time), ZyphosUmbrosSystem.UmbrosSurfaceRadius)),
        new(UmbrosTerrainTile, UmbrosLatLong, "Umbros Zenith Tile", "tile", "Cube tile", ZyphosUmbrosSystem.UmbrosBoundRadius * 1.2f, time => new ZyphosSpatialDomainPose(ZyphosUmbrosSystem.UmbrosCenter(time) + new Vector3(0.0f, 0.0f, ZyphosUmbrosSystem.UmbrosSurfaceRadius), ZyphosUmbrosSystem.UmbrosSurfaceRadius * 0.34f)),
        new(UmbrosNightCracksTile, UmbrosLatLong, "Umbros Night Cracks", "L1 tile", "Cube tile", ZyphosUmbrosSystem.UmbrosBoundRadius * 1.18f, time => new ZyphosSpatialDomainPose(ZyphosUmbrosSystem.UmbrosCenter(time) + Vector3.Normalize(new Vector3(0.0f, -1.0f, 0.18f)) * ZyphosUmbrosSystem.UmbrosSurfaceRadius, ZyphosUmbrosSystem.UmbrosSurfaceRadius * 0.24f)),
    ];

    private static readonly IReadOnlyDictionary<ZyphosSpatialDomainKey, ZyphosSpatialDomain> DomainByKey =
        DomainList.ToDictionary(domain => domain.Key);

    public static IReadOnlyList<ZyphosSpatialDomain> Domains => DomainList;

    public static ZyphosSpatialDomain GetRequired(ZyphosSpatialDomainKey key)
    {
        if (!DomainByKey.TryGetValue(key, out var domain))
        {
            throw new ArgumentException($"Unknown Zyphos spatial domain `{key}`.", nameof(key));
        }

        return domain;
    }

    public static IReadOnlyList<ZyphosSpatialDomain> ChildrenOf(ZyphosSpatialDomainKey key)
    {
        return DomainList.Where(domain => domain.ParentKey == key).ToArray();
    }

    public static ZyphosSpatialDomain? ParentOf(ZyphosSpatialDomain domain)
    {
        return domain.ParentKey is { } parentKey ? GetRequired(parentKey) : null;
    }

    public static int DepthOf(ZyphosSpatialDomain domain)
    {
        var depth = 0;
        var current = domain;
        while (ParentOf(current) is { } parent)
        {
            depth++;
            current = parent;
        }

        return depth;
    }

    public static string DisplayName(ZyphosSpatialDomainKey key) => GetRequired(key).Label;

    private static Vector3 BinaryCenter(float timeSeconds)
    {
        return ZyphosUmbrosSystem.ZyphosCenter + (ZyphosUmbrosSystem.UmbrosCenter(timeSeconds) - ZyphosUmbrosSystem.ZyphosCenter) * 0.5f;
    }
}
