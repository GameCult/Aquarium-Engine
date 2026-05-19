using System.Numerics;
using Aquarium.Engine.Fractal.Brushes;
using Aquarium.Engine.Fractal.Grammar;

namespace Aquarium.Engine.Fractal.Lod;

public static class FractalSurfacePageRasterizer
{
    public static FractalSurfacePagePayload Rasterize(FractalOwnershipTree tree, AquariumFractalSurfacePage page)
    {
        ArgumentNullException.ThrowIfNull(tree);
        if (page.Width <= 0 || page.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(page), "Surface page dimensions must be positive.");
        }

        var node = tree.Nodes.FirstOrDefault(candidate => candidate.Key == page.Key.NodeKey);
        if (node.Key.Value is null)
        {
            throw new ArgumentException($"Surface page node {page.Key.NodeKey} does not exist in the ownership tree.", nameof(page));
        }

        var claims = ClaimsForNode(tree, node);
        var samples = new float[page.Width * page.Height];
        for (var y = 0; y < page.Height; y++)
        {
            for (var x = 0; x < page.Width; x++)
            {
                var point = SamplePoint(page, x, y);
                samples[(y * page.Width) + x] = Evaluate(page.Key.Kind, claims, point);
            }
        }

        return new FractalSurfacePagePayload(page, samples);
    }

    private static AquariumBrushClaim[] ClaimsForNode(FractalOwnershipTree tree, AquariumFractalNode node)
    {
        if (node.ClaimCount <= 0)
        {
            return [];
        }

        var claims = new AquariumBrushClaim[node.ClaimCount];
        for (var index = 0; index < node.ClaimCount; index++)
        {
            claims[index] = tree.Claims[node.FirstClaimIndex + index];
        }

        return claims;
    }

    private static Vector2 SamplePoint(AquariumFractalSurfacePage page, int x, int y)
    {
        var u = page.Width == 1 ? 0.5f : x / (float)(page.Width - 1);
        var v = page.Height == 1 ? 0.5f : y / (float)(page.Height - 1);
        return new Vector2(
            Lerp(page.BoundsMinMax.X, page.BoundsMinMax.Z, u),
            Lerp(page.BoundsMinMax.Y, page.BoundsMinMax.W, v));
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + ((b - a) * t);
    }

    private static float Evaluate(AquariumFractalSurfacePageKind kind, IReadOnlyList<AquariumBrushClaim> claims, Vector2 point)
    {
        return kind switch
        {
            AquariumFractalSurfacePageKind.Height => EvaluateHeight(claims, point),
            AquariumFractalSurfacePageKind.SignedDistance2D => EvaluateSignedDistance(claims, point),
            AquariumFractalSurfacePageKind.Material => EvaluateMaterial(claims, point),
            AquariumFractalSurfacePageKind.Confidence => EvaluateConfidence(claims, point),
            _ => 0.0f,
        };
    }

    private static float EvaluateHeight(IReadOnlyList<AquariumBrushClaim> claims, Vector2 point)
    {
        var value = 0.0;
        foreach (var claim in claims)
        {
            value += Envelope(claim).Evaluate(point) * claim.Amplitude;
        }

        return (float)value;
    }

    private static float EvaluateSignedDistance(IReadOnlyList<AquariumBrushClaim> claims, Vector2 point)
    {
        if (claims.Count == 0)
        {
            return float.PositiveInfinity;
        }

        var distance = double.PositiveInfinity;
        foreach (var claim in claims)
        {
            distance = Math.Min(distance, Envelope(claim).SignedSupportDistanceEstimate(point));
        }

        return (float)distance;
    }

    private static float EvaluateMaterial(IReadOnlyList<AquariumBrushClaim> claims, Vector2 point)
    {
        var weighted = 0.0;
        var weightSum = 0.0;
        foreach (var claim in claims)
        {
            var weight = Envelope(claim).Evaluate(point);
            if (weight <= 0.0)
            {
                continue;
            }

            weighted += weight * StableMaterialValue(claim);
            weightSum += weight;
        }

        return weightSum <= 0.0 ? 0.0f : (float)(weighted / weightSum);
    }

    private static float EvaluateConfidence(IReadOnlyList<AquariumBrushClaim> claims, Vector2 point)
    {
        var confidence = 0.0;
        foreach (var claim in claims)
        {
            confidence += Envelope(claim).Evaluate(point);
        }

        return (float)Math.Clamp(confidence, 0.0, 1.0);
    }

    private static double StableMaterialValue(AquariumBrushClaim claim)
    {
        var hash = (uint)claim.Seed * 747796405u;
        foreach (var ch in claim.Tags)
        {
            hash = (hash * 16777619u) ^ ch;
        }

        return (hash & 0xFFFFu) / 65535.0;
    }

    private static FractalBrushEnvelope2D Envelope(AquariumBrushClaim claim)
    {
        return new FractalBrushEnvelope2D(
            claim.Center,
            claim.Radii.X,
            claim.Radii.Y,
            claim.RotationRadians,
            claim.Falloff,
            claim.ShapePower);
    }
}
