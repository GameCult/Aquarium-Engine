using System.Numerics;
using Aquarium.Engine.Fractal;

namespace Aquarium.Engine.Fractal.Grammar;

public static class FractalOwnershipTreeBuilder
{
    public static FractalOwnershipTree BuildFlatUnion(
        AquariumFractalDomain domain,
        AquariumFractalKey rootKey,
        IReadOnlyList<AquariumBrushClaim> claims)
    {
        return BuildFlatUnion(domain, [domain], rootKey, claims);
    }

    public static FractalOwnershipTree BuildFlatUnion(
        AquariumFractalDomain domain,
        IReadOnlyList<AquariumFractalDomain> domains,
        AquariumFractalKey rootKey,
        IReadOnlyList<AquariumBrushClaim> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(domains);

        var bounds = BoundsForClaims(claims);
        var root = new AquariumFractalNode(
            rootKey,
            domain.Key,
            default,
            AquariumFractalOperation.Union,
            FirstChildIndex: 0,
            ChildCount: 0,
            FirstClaimIndex: 0,
            ClaimCount: claims.Count,
            bounds,
            Seed: 0);

        return new FractalOwnershipTree(domain, domains, [root], claims);
    }

    private static Vector4 BoundsForClaims(IReadOnlyList<AquariumBrushClaim> claims)
    {
        if (claims.Count == 0)
        {
            return Vector4.Zero;
        }

        var minX = float.PositiveInfinity;
        var minY = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var maxY = float.NegativeInfinity;

        foreach (var claim in claims)
        {
            var radius = MathF.Max(claim.Radii.X, claim.Radii.Y);
            minX = MathF.Min(minX, claim.Center.X - radius);
            minY = MathF.Min(minY, claim.Center.Y - radius);
            maxX = MathF.Max(maxX, claim.Center.X + radius);
            maxY = MathF.Max(maxY, claim.Center.Y + radius);
        }

        return new Vector4(minX, minY, maxX, maxY);
    }
}
