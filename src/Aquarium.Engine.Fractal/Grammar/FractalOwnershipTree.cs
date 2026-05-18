using Aquarium.Engine.Fractal;

namespace Aquarium.Engine.Fractal.Grammar;

public sealed class FractalOwnershipTree
{
    public FractalOwnershipTree(
        AquariumFractalDomain domain,
        IReadOnlyList<AquariumFractalNode> nodes,
        IReadOnlyList<AquariumBrushClaim> claims)
        : this(domain, [domain], nodes, claims)
    {
    }

    public FractalOwnershipTree(
        AquariumFractalDomain domain,
        IReadOnlyList<AquariumFractalDomain> domains,
        IReadOnlyList<AquariumFractalNode> nodes,
        IReadOnlyList<AquariumBrushClaim> claims)
    {
        Domain = domain;
        Domains = domains;
        Nodes = nodes;
        Claims = claims;
    }

    public AquariumFractalDomain Domain { get; }

    public IReadOnlyList<AquariumFractalDomain> Domains { get; }

    public IReadOnlyList<AquariumFractalNode> Nodes { get; }

    public IReadOnlyList<AquariumBrushClaim> Claims { get; }
}
