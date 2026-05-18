using Aquarium.Engine.Render;
using Aquarium.Engine.Fractal.Grammar;

namespace Aquarium.Engine.Fractal.Brushes;

public static class FractalHeightBrushCompiler
{
    public static AquariumHeightFieldBrush Compile(AquariumBrushClaim claim)
    {
        return Compile(claim, null);
    }

    public static AquariumHeightFieldBrush Compile(AquariumBrushClaim claim, AquariumFractalDomain? domain)
    {
        if (claim.PayloadKind != AquariumFractalPayloadKind.Height)
        {
            throw new ArgumentException($"Claim {claim.Key} carries {claim.PayloadKind}; only height claims can compile to height-field brushes.", nameof(claim));
        }

        if (claim.Radii.X <= 0.0f || claim.Radii.Y <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(claim), claim.Radii, "Height claim radii must be positive.");
        }

        return new AquariumHeightFieldBrush(
            claim.Center,
            claim.Radii.X,
            claim.ShapePower,
            claim.Amplitude,
            WaveAmplitude: 0.0f,
            WaveFrequency: 0.0f,
            WaveSpeed: 0.0f,
            WaveSinePower: 0.0f,
            RadiusY: claim.Radii.Y,
            RotationRadians: claim.RotationRadians,
            EnvelopeFalloff: claim.Falloff,
            DomainFace: domain is { Kind: AquariumFractalDomainKind.CubeSphereTile } ? domain.Value.Parameters0.X : -1.0f,
            DomainLevel: domain is { Kind: AquariumFractalDomainKind.CubeSphereTile } ? domain.Value.Parameters0.Y : 0.0f,
            DomainX: domain is { Kind: AquariumFractalDomainKind.CubeSphereTile } ? domain.Value.Parameters0.Z : 0.0f,
            DomainY: domain is { Kind: AquariumFractalDomainKind.CubeSphereTile } ? domain.Value.Parameters0.W : 0.0f);
    }

    public static AquariumHeightFieldBrush[] CompileMany(IReadOnlyList<AquariumBrushClaim> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        var brushes = new AquariumHeightFieldBrush[claims.Count];
        for (var index = 0; index < claims.Count; index++)
        {
            brushes[index] = Compile(claims[index]);
        }

        return brushes;
    }

    public static AquariumHeightFieldBrush[] CompileTree(FractalOwnershipTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        var domains = tree.Domains.ToDictionary(domain => domain.Key, domain => domain);
        var brushes = new AquariumHeightFieldBrush[tree.Claims.Count];
        for (var index = 0; index < tree.Claims.Count; index++)
        {
            var claim = tree.Claims[index];
            domains.TryGetValue(claim.DomainKey, out var domain);
            brushes[index] = Compile(claim, domain);
        }

        return brushes;
    }
}
