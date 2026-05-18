using Aquarium.Engine.Fractal;

namespace Aquarium.Engine.Fractal.Grammar;

public sealed class FractalDomainGraph
{
    private readonly Dictionary<AquariumFractalKey, int> indices;

    public FractalDomainGraph(IReadOnlyList<AquariumFractalDomain> domains)
    {
        ArgumentNullException.ThrowIfNull(domains);
        if (domains.Count == 0)
        {
            throw new FormatException("Fractal domain graph must contain at least one domain.");
        }

        indices = new Dictionary<AquariumFractalKey, int>(domains.Count);
        for (var index = 0; index < domains.Count; index++)
        {
            var domain = domains[index];
            if (domain.Key.Value is null)
            {
                throw new FormatException($"Fractal domain at index {index} has no key.");
            }

            if (!indices.TryAdd(domain.Key, index))
            {
                throw new FormatException($"Duplicate fractal domain key `{domain.Key}`.");
            }
        }

        Domains = domains.ToArray();
        ValidateParents();
    }

    public IReadOnlyList<AquariumFractalDomain> Domains { get; }

    public bool Contains(AquariumFractalKey key)
    {
        return key.Value is not null && indices.ContainsKey(key);
    }

    public AquariumFractalDomain GetRequired(AquariumFractalKey key)
    {
        if (key.Value is null || !indices.TryGetValue(key, out var index))
        {
            throw new FormatException($"Unknown fractal domain `{key}`.");
        }

        return Domains[index];
    }

    public IReadOnlyList<AquariumFractalDomain> GetPath(AquariumFractalKey leafKey)
    {
        var path = new List<AquariumFractalDomain>();
        var seen = new HashSet<AquariumFractalKey>();
        var current = GetRequired(leafKey);
        while (true)
        {
            if (!seen.Add(current.Key))
            {
                throw new FormatException($"Fractal domain graph contains a parent cycle at `{current.Key}`.");
            }

            path.Add(current);
            if (current.ParentKey.Value is null)
            {
                break;
            }

            current = GetRequired(current.ParentKey);
        }

        path.Reverse();
        return path;
    }

    private void ValidateParents()
    {
        foreach (var domain in Domains)
        {
            _ = GetPath(domain.Key);
        }
    }
}
