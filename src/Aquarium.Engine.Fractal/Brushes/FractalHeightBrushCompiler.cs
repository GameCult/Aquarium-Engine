using Aquarium.Engine.Render;

namespace Aquarium.Engine.Fractal.Brushes;

public static class FractalHeightBrushCompiler
{
    public static AquariumHeightFieldBrush Compile(AquariumBrushClaim claim)
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
            EnvelopeFalloff: claim.Falloff);
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
}
