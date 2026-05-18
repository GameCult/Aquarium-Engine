using Aquarium.Zyphos;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class ZyphosSpatialDomainCatalogTests
{
    [Fact]
    public void CatalogCoversAquageoDomainStack()
    {
        var catalogKeys = ZyphosSpatialDomainCatalog.Domains.Select(domain => domain.Key.Value).ToHashSet();

        foreach (var domain in ZyphosFractalTerrain.OwnershipTree.Domains)
        {
            Assert.Contains(domain.Key.Value, catalogKeys);
        }
    }

    [Fact]
    public void CatalogParentsPointAtDeclaredDomains()
    {
        var catalogKeys = ZyphosSpatialDomainCatalog.Domains.Select(domain => domain.Key).ToHashSet();

        foreach (var domain in ZyphosSpatialDomainCatalog.Domains)
        {
            if (domain.ParentKey is { } parentKey)
            {
                Assert.Contains(parentKey, catalogKeys);
            }
        }
    }

    [Fact]
    public void ZyphosAndUmbrosBothExposeSurfaceAndTileChildren()
    {
        Assert.Contains(ZyphosSpatialDomainCatalog.ChildrenOf(ZyphosSpatialDomainCatalog.Planet), domain => domain.Key == ZyphosSpatialDomainCatalog.PlanetLatLong);
        Assert.Contains(ZyphosSpatialDomainCatalog.ChildrenOf(ZyphosSpatialDomainCatalog.Umbros), domain => domain.Key == ZyphosSpatialDomainCatalog.UmbrosLatLong);
        Assert.Contains(ZyphosSpatialDomainCatalog.ChildrenOf(ZyphosSpatialDomainCatalog.EquatorialForest), domain => domain.Key == ZyphosSpatialDomainCatalog.CanopyTree);
        Assert.Contains(ZyphosSpatialDomainCatalog.ChildrenOf(ZyphosSpatialDomainCatalog.CanopyTree), domain => domain.Key == ZyphosSpatialDomainCatalog.CanopyLeaf);
        Assert.Contains(ZyphosSpatialDomainCatalog.ChildrenOf(ZyphosSpatialDomainCatalog.PebbleField), domain => domain.Key == ZyphosSpatialDomainCatalog.PebbleFieldTile);
        Assert.Contains(ZyphosSpatialDomainCatalog.ChildrenOf(ZyphosSpatialDomainCatalog.UmbrosBoulderField), domain => domain.Key == ZyphosSpatialDomainCatalog.UmbrosPebbleField);
        Assert.Contains(ZyphosSpatialDomainCatalog.ChildrenOf(ZyphosSpatialDomainCatalog.UmbrosPebbleField), domain => domain.Key == ZyphosSpatialDomainCatalog.UmbrosBoulderPebbleTile);
    }
}
