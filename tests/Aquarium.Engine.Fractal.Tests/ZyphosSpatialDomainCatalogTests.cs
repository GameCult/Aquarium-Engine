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
}
