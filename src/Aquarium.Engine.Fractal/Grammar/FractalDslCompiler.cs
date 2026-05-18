using System.Globalization;
using System.Numerics;
using Aquarium.Engine.Fractal;

namespace Aquarium.Engine.Fractal.Grammar;

public static class FractalDslCompiler
{
    public static FractalOwnershipTree Compile(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        AquariumFractalDomain? domain = null;
        AquariumFractalKey rootKey = default;
        var domains = new List<AquariumFractalDomain>();
        var claims = new List<AquariumBrushClaim>();

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = StripComment(lines[lineIndex]).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            switch (tokens[0])
            {
                case "domain":
                    EnsureTokenCount(tokens, 4, lineIndex);
                    domains.Add(ParseDomain(tokens, lineIndex));
                    break;
                case "tile":
                    EnsureTokenCount(tokens, 6, lineIndex);
                    var tile = new CubeTileKey(ParseFace(tokens[1], lineIndex), ParseInt(tokens[2], lineIndex), ParseInt(tokens[3], lineIndex), ParseInt(tokens[4], lineIndex));
                    var domainKey = FractalStableKeyBuilder.ForCubeTile(tile, tokens[5]);
                    var parentKey = tokens.Length >= 7 ? new AquariumFractalKey(tokens[6]) : default;
                    rootKey = FractalStableKeyBuilder.Child(domainKey, "root");
                    domain = new AquariumFractalDomain(
                        domainKey,
                        AquariumFractalDomainKind.CubeSphereTile,
                        parentKey,
                        new Vector4((float)tile.Face, tile.Level, tile.X, tile.Y),
                        Vector4.Zero);
                    domains.Add(domain.Value);
                    break;
                case "height":
                    EnsureDomain(domain, lineIndex);
                    EnsureTokenCount(tokens, 12, lineIndex);
                    claims.Add(ParseHeight(tokens, rootKey, lineIndex, claims.Count));
                    break;
                case "ifs":
                    EnsureDomain(domain, lineIndex);
                    EnsureTokenCount(tokens, 15, lineIndex);
                    AddIfsClaims(tokens, rootKey, lineIndex, claims);
                    break;
                default:
                    throw new FormatException($"Unknown fractal DSL command `{tokens[0]}` at line {lineIndex + 1}.");
            }
        }

        if (domain is null)
        {
            throw new FormatException("Fractal DSL must declare a `tile <face> <level> <x> <y> <path>` line before claims.");
        }

        return FractalOwnershipTreeBuilder.BuildFlatUnion(domain.Value, domains, rootKey, claims);
    }

    private static AquariumFractalDomain ParseDomain(string[] tokens, int lineIndex)
    {
        if (!Enum.TryParse<AquariumFractalDomainKind>(tokens[1], ignoreCase: true, out var kind))
        {
            throw new FormatException($"Invalid domain kind `{tokens[1]}` at line {lineIndex + 1}.");
        }

        var parent = tokens[3] == "-" ? default : new AquariumFractalKey(tokens[3]);
        var parameters = new float[8];
        for (var index = 4; index < tokens.Length && index < 12; index++)
        {
            parameters[index - 4] = ParseFloat(tokens[index], lineIndex);
        }

        return new AquariumFractalDomain(
            new AquariumFractalKey(tokens[2]),
            kind,
            parent,
            new Vector4(parameters[0], parameters[1], parameters[2], parameters[3]),
            new Vector4(parameters[4], parameters[5], parameters[6], parameters[7]));
    }

    private static AquariumBrushClaim ParseHeight(string[] tokens, AquariumFractalKey rootKey, int lineIndex, int claimIndex)
    {
        var name = tokens[1];
        return HeightClaim(
            rootKey,
            name,
            claimIndex,
            new Vector2(ParseFloat(tokens[2], lineIndex), ParseFloat(tokens[3], lineIndex)),
            new Vector2(ParsePositiveFloat(tokens[4], lineIndex), ParsePositiveFloat(tokens[5], lineIndex)),
            ParseFloat(tokens[6], lineIndex),
            ParsePositiveFloat(tokens[7], lineIndex),
            ParsePositiveFloat(tokens[8], lineIndex),
            ParseFloat(tokens[9], lineIndex),
            ParseInt(tokens[10], lineIndex),
            tokens[11]);
    }

    private static void AddIfsClaims(string[] tokens, AquariumFractalKey rootKey, int lineIndex, List<AquariumBrushClaim> claims)
    {
        var name = tokens[1];
        var levels = ParsePositiveInt(tokens[2], lineIndex);
        var branches = ParsePositiveInt(tokens[3], lineIndex);
        var center = new Vector2(ParseFloat(tokens[4], lineIndex), ParseFloat(tokens[5], lineIndex));
        var radii = new Vector2(ParsePositiveFloat(tokens[6], lineIndex), ParsePositiveFloat(tokens[7], lineIndex));
        var childScale = ParsePositiveFloat(tokens[8], lineIndex);
        var spread = ParsePositiveFloat(tokens[9], lineIndex);
        var rotationStep = ParseFloat(tokens[10], lineIndex);
        var falloff = ParsePositiveFloat(tokens[11], lineIndex);
        var shapePower = ParsePositiveFloat(tokens[12], lineIndex);
        var amplitude = ParseFloat(tokens[13], lineIndex);
        var seed = ParseInt(tokens[14], lineIndex);
        var tags = tokens.Length >= 16 ? tokens[15] : name;

        if (childScale >= 1.0f)
        {
            throw new FormatException($"IFS child scale must be below 1 at line {lineIndex + 1}.");
        }

        EmitIfsLevel(rootKey, name, center, radii, levels, branches, childScale, spread, rotationStep, falloff, shapePower, amplitude, seed, tags, claims);
    }

    private static void EmitIfsLevel(
        AquariumFractalKey rootKey,
        string name,
        Vector2 center,
        Vector2 radii,
        int levelsRemaining,
        int branches,
        float childScale,
        float spread,
        float rotationStep,
        float falloff,
        float shapePower,
        float amplitude,
        int seed,
        string tags,
        List<AquariumBrushClaim> claims)
    {
        var level = claims.Count;
        claims.Add(HeightClaim(rootKey, $"{name}/{level}", claims.Count, center, radii, rotationStep * level, falloff, shapePower, amplitude, seed + level, tags));

        if (levelsRemaining <= 1)
        {
            return;
        }

        var nextRadii = radii * childScale;
        for (var branch = 0; branch < branches; branch++)
        {
            var phase = ((MathF.Tau * branch) / branches) + rotationStep * level + HashAngle(seed, level, branch);
            var offset = new Vector2(MathF.Cos(phase), MathF.Sin(phase)) * spread * MathF.Max(nextRadii.X, nextRadii.Y);
            EmitIfsLevel(
                rootKey,
                name,
                center + offset,
                nextRadii,
                levelsRemaining - 1,
                branches,
                childScale,
                spread,
                rotationStep,
                falloff,
                shapePower,
                amplitude * childScale,
                seed + 31 + branch,
                tags,
                claims);
        }
    }

    private static AquariumBrushClaim HeightClaim(
        AquariumFractalKey rootKey,
        string name,
        int claimIndex,
        Vector2 center,
        Vector2 radii,
        float rotationRadians,
        float falloff,
        float shapePower,
        float amplitude,
        int seed,
        string tags)
    {
        return new AquariumBrushClaim(
            FractalStableKeyBuilder.Child(rootKey, $"claim/{claimIndex:0000}/{name}"),
            rootKey,
            rootKey,
            AquariumFractalPayloadKind.Height,
            center,
            radii,
            rotationRadians,
            falloff,
            shapePower,
            amplitude,
            seed,
            tags);
    }

    private static string StripComment(string line)
    {
        var index = line.IndexOf('#', StringComparison.Ordinal);
        return index < 0 ? line : line[..index];
    }

    private static void EnsureTokenCount(string[] tokens, int minimum, int lineIndex)
    {
        if (tokens.Length < minimum)
        {
            throw new FormatException($"Command `{tokens[0]}` at line {lineIndex + 1} expected at least {minimum - 1} arguments.");
        }
    }

    private static void EnsureDomain(AquariumFractalDomain? domain, int lineIndex)
    {
        if (domain is null)
        {
            throw new FormatException($"Fractal DSL claim at line {lineIndex + 1} appears before a tile declaration.");
        }
    }

    private static CubeFace ParseFace(string value, int lineIndex)
    {
        if (Enum.TryParse<CubeFace>(value, ignoreCase: false, out var face))
        {
            return face;
        }

        throw new FormatException($"Invalid cube face `{value}` at line {lineIndex + 1}.");
    }

    private static int ParsePositiveInt(string value, int lineIndex)
    {
        var parsed = ParseInt(value, lineIndex);
        if (parsed <= 0)
        {
            throw new FormatException($"Expected positive integer at line {lineIndex + 1}, got `{value}`.");
        }

        return parsed;
    }

    private static int ParseInt(string value, int lineIndex)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        throw new FormatException($"Invalid integer `{value}` at line {lineIndex + 1}.");
    }

    private static float ParsePositiveFloat(string value, int lineIndex)
    {
        var parsed = ParseFloat(value, lineIndex);
        if (parsed <= 0.0f)
        {
            throw new FormatException($"Expected positive number at line {lineIndex + 1}, got `{value}`.");
        }

        return parsed;
    }

    private static float ParseFloat(string value, int lineIndex)
    {
        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        throw new FormatException($"Invalid number `{value}` at line {lineIndex + 1}.");
    }

    private static float HashAngle(int seed, int level, int branch)
    {
        var value = (seed * 1103515245) ^ (level * 374761393) ^ (branch * 668265263);
        value ^= value >> 13;
        value *= 1274126177;
        value ^= value >> 16;
        var normalized = (value & 0xFFFF) / 65535.0f;
        return (normalized - 0.5f) * 0.35f;
    }
}
