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
    public static readonly ZyphosSpatialDomainKey ContinentArchipelago = new("zyphos/continent-archipelago");
    public static readonly ZyphosSpatialDomainKey EquatorialForest = new("zyphos/equatorial-forest");
    public static readonly ZyphosSpatialDomainKey CanopyTree = new("zyphos/canopy-tree");
    public static readonly ZyphosSpatialDomainKey CanopyLeaf = new("zyphos/canopy-leaf");
    public static readonly ZyphosSpatialDomainKey ShingleCoast = new("zyphos/shingle-coast");
    public static readonly ZyphosSpatialDomainKey PebbleField = new("zyphos/pebble-field");
    public static readonly ZyphosSpatialDomainKey UmbrosCraterProvince = new("umbros/crater-province");
    public static readonly ZyphosSpatialDomainKey UmbrosBoulderField = new("umbros/boulder-field");
    public static readonly ZyphosSpatialDomainKey UmbrosPebbleField = new("umbros/pebble-field");
    public static readonly ZyphosSpatialDomainKey TerrainTile = new("cube/PositiveZ/L00/0/0:zyphos/first-fractal-terrain");
    public static readonly ZyphosSpatialDomainKey TerminatorShelvesTile = new("cube/PositiveX/L01/1/0:zyphos/terminator-shelves");
    public static readonly ZyphosSpatialDomainKey GreenleafCanopyTile = new("cube/NegativeX/L02/2/1:zyphos/greenleaf-canopy");
    public static readonly ZyphosSpatialDomainKey PebbleFieldTile = new("cube/NegativeZ/L02/1/2:zyphos/pebble-field");
    public static readonly ZyphosSpatialDomainKey UmbrosTerrainTile = new("cube/PositiveZ/L00/0/0:umbros/first-fractal-terrain");
    public static readonly ZyphosSpatialDomainKey UmbrosNightCracksTile = new("cube/NegativeY/L01/0/1:umbros/night-cracks");
    public static readonly ZyphosSpatialDomainKey UmbrosBoulderPebbleTile = new("cube/PositiveY/L02/1/1:umbros/boulder-pebble");

    private static readonly ZyphosSpatialDomain[] DomainList =
    [
        new(Solar, null, "Parent Star", "sun", "Solar", ZyphosUmbrosSystem.CenterSeparation * 0.9f, time => new ZyphosSpatialDomainPose(ZyphosUmbrosSystem.PrimaryStarCenter(time), ZyphosUmbrosSystem.PrimaryStarVisualRadius)),
        new(Orbital, Solar, "Zyphos-Umbros Orbit", "orbital", "Orbital", ZyphosUmbrosSystem.CenterSeparation * 1.9f, time => new ZyphosSpatialDomainPose(BinaryCenter(time), ZyphosUmbrosSystem.CenterSeparation * 0.5f)),
        new(Planet, Orbital, "Zyphos", "planet", "Planetary", ZyphosUmbrosSystem.ZyphosBoundRadius * 2.6f, _ => new ZyphosSpatialDomainPose(ZyphosUmbrosSystem.ZyphosCenter, ZyphosUmbrosSystem.ZyphosBoundRadius)),
        new(Umbros, Orbital, "Umbros", "twin", "Planetary", ZyphosUmbrosSystem.CenterSeparation * 1.35f, time => new ZyphosSpatialDomainPose(ZyphosUmbrosSystem.UmbrosCenter(time), ZyphosUmbrosSystem.UmbrosBoundRadius)),
        new(PlanetLatLong, Planet, "Lat/Long Surface", "surface", "LatLong", ZyphosUmbrosSystem.ZyphosBoundRadius * 1.55f, _ => new ZyphosSpatialDomainPose(ZyphosUmbrosSystem.ZyphosCenter, ZyphosUmbrosSystem.ZyphosSurfaceRadius)),
        new(ContinentArchipelago, PlanetLatLong, "Archipelago Continent", "continent", "Surface2D", ZyphosUmbrosSystem.ZyphosBoundRadius * 1.28f, _ => PlanetSurface(new Vector3(0.25f, 0.62f, 0.42f), ZyphosUmbrosSystem.ZyphosSurfaceRadius * 0.52f)),
        new(EquatorialForest, ContinentArchipelago, "Equatorial Forest", "forest", "Surface2D", ZyphosUmbrosSystem.ZyphosBoundRadius * 0.82f, _ => PlanetSurface(new Vector3(0.32f, 0.68f, 0.16f), ZyphosUmbrosSystem.ZyphosSurfaceRadius * 0.22f)),
        new(CanopyTree, EquatorialForest, "Canopy Tree", "tree", "Object3D", ZyphosUmbrosSystem.ZyphosBoundRadius * 0.42f, _ => PlanetSurface(new Vector3(0.35f, 0.71f, 0.18f), ZyphosUmbrosSystem.ZyphosSurfaceRadius * 0.075f)),
        new(CanopyLeaf, CanopyTree, "Canopy Leaf", "leaf", "Object3D", ZyphosUmbrosSystem.ZyphosBoundRadius * 0.24f, _ => PlanetSurface(new Vector3(0.36f, 0.72f, 0.19f), ZyphosUmbrosSystem.ZyphosSurfaceRadius * 0.028f)),
        new(ShingleCoast, ContinentArchipelago, "Shingle Coast", "coast", "Surface2D", ZyphosUmbrosSystem.ZyphosBoundRadius * 0.68f, _ => PlanetSurface(new Vector3(0.55f, -0.24f, 0.12f), ZyphosUmbrosSystem.ZyphosSurfaceRadius * 0.16f)),
        new(PebbleField, ShingleCoast, "Pebble Field", "pebbles", "Object3D", ZyphosUmbrosSystem.ZyphosBoundRadius * 0.32f, _ => PlanetSurface(new Vector3(0.57f, -0.28f, 0.10f), ZyphosUmbrosSystem.ZyphosSurfaceRadius * 0.045f)),
        new(TerrainTile, ContinentArchipelago, "Zyphos Zenith Tile", "tile", "Cube tile", ZyphosUmbrosSystem.ZyphosBoundRadius * 1.22f, _ => new ZyphosSpatialDomainPose(ZyphosUmbrosSystem.ZyphosCenter + new Vector3(0.0f, 0.0f, ZyphosUmbrosSystem.ZyphosSurfaceRadius), ZyphosUmbrosSystem.ZyphosSurfaceRadius * 0.32f)),
        new(TerminatorShelvesTile, ShingleCoast, "Terminator Shelves", "L1 tile", "Cube tile", ZyphosUmbrosSystem.ZyphosBoundRadius * 1.18f, _ => new ZyphosSpatialDomainPose(ZyphosUmbrosSystem.ZyphosCenter + Vector3.Normalize(new Vector3(1.0f, 0.0f, 0.2f)) * ZyphosUmbrosSystem.ZyphosSurfaceRadius, ZyphosUmbrosSystem.ZyphosSurfaceRadius * 0.25f)),
        new(GreenleafCanopyTile, CanopyLeaf, "Greenleaf Canopy Tile", "leaf IFS", "Cube tile", ZyphosUmbrosSystem.ZyphosBoundRadius * 0.36f, _ => PlanetSurface(new Vector3(0.36f, 0.72f, 0.19f), ZyphosUmbrosSystem.ZyphosSurfaceRadius * 0.05f)),
        new(PebbleFieldTile, PebbleField, "Pebble Field Tile", "pebble IFS", "Cube tile", ZyphosUmbrosSystem.ZyphosBoundRadius * 0.30f, _ => PlanetSurface(new Vector3(0.58f, -0.29f, 0.11f), ZyphosUmbrosSystem.ZyphosSurfaceRadius * 0.038f)),
        new(UmbrosLatLong, Umbros, "Umbros Lat/Long", "surface", "LatLong", ZyphosUmbrosSystem.UmbrosBoundRadius * 1.65f, time => new ZyphosSpatialDomainPose(ZyphosUmbrosSystem.UmbrosCenter(time), ZyphosUmbrosSystem.UmbrosSurfaceRadius)),
        new(UmbrosCraterProvince, UmbrosLatLong, "Crater Province", "province", "Surface2D", ZyphosUmbrosSystem.UmbrosBoundRadius * 0.72f, time => UmbrosSurface(time, new Vector3(0.84f, -0.18f, 0.28f), ZyphosUmbrosSystem.UmbrosSurfaceRadius * 0.18f)),
        new(UmbrosBoulderField, UmbrosCraterProvince, "Boulder Field", "boulders", "Surface2D", ZyphosUmbrosSystem.UmbrosBoundRadius * 0.44f, time => UmbrosSurface(time, new Vector3(0.86f, -0.22f, 0.22f), ZyphosUmbrosSystem.UmbrosSurfaceRadius * 0.08f)),
        new(UmbrosPebbleField, UmbrosBoulderField, "Umbros Pebble Field", "pebbles", "Object3D", ZyphosUmbrosSystem.UmbrosBoundRadius * 0.28f, time => UmbrosSurface(time, new Vector3(0.88f, -0.24f, 0.20f), ZyphosUmbrosSystem.UmbrosSurfaceRadius * 0.035f)),
        new(UmbrosTerrainTile, UmbrosLatLong, "Umbros Zenith Tile", "tile", "Cube tile", ZyphosUmbrosSystem.UmbrosBoundRadius * 1.2f, time => new ZyphosSpatialDomainPose(ZyphosUmbrosSystem.UmbrosCenter(time) + new Vector3(0.0f, 0.0f, ZyphosUmbrosSystem.UmbrosSurfaceRadius), ZyphosUmbrosSystem.UmbrosSurfaceRadius * 0.34f)),
        new(UmbrosNightCracksTile, UmbrosCraterProvince, "Umbros Night Cracks", "L1 tile", "Cube tile", ZyphosUmbrosSystem.UmbrosBoundRadius * 1.18f, time => new ZyphosSpatialDomainPose(ZyphosUmbrosSystem.UmbrosCenter(time) + Vector3.Normalize(new Vector3(0.0f, -1.0f, 0.18f)) * ZyphosUmbrosSystem.UmbrosSurfaceRadius, ZyphosUmbrosSystem.UmbrosSurfaceRadius * 0.24f)),
        new(UmbrosBoulderPebbleTile, UmbrosPebbleField, "Boulder Pebble Tile", "pebble IFS", "Cube tile", ZyphosUmbrosSystem.UmbrosBoundRadius * 0.30f, time => UmbrosSurface(time, new Vector3(0.89f, -0.25f, 0.19f), ZyphosUmbrosSystem.UmbrosSurfaceRadius * 0.04f)),
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

    private static ZyphosSpatialDomainPose PlanetSurface(Vector3 normal, float radius)
    {
        return new ZyphosSpatialDomainPose(
            ZyphosUmbrosSystem.ZyphosCenter + Vector3.Normalize(normal) * ZyphosUmbrosSystem.ZyphosSurfaceRadius,
            radius);
    }

    private static ZyphosSpatialDomainPose UmbrosSurface(float timeSeconds, Vector3 normal, float radius)
    {
        return new ZyphosSpatialDomainPose(
            ZyphosUmbrosSystem.UmbrosCenter(timeSeconds) + Vector3.Normalize(normal) * ZyphosUmbrosSystem.UmbrosSurfaceRadius,
            radius);
    }
}
