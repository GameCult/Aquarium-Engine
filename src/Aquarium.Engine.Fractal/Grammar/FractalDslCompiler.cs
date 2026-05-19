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
        NodeDraft? currentNode = null;
        var domains = new List<AquariumFractalDomain>();
        var claims = new List<AquariumBrushClaim>();
        var nodes = new List<NodeDraft>();

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
                    rootKey = FractalStableKeyBuilder.Child(domainKey, "root");
                    currentNode = new NodeDraft(rootKey, domainKey, claims.Count);
                    nodes.Add(currentNode);
                    break;
                case "height":
                    EnsureDomain(domain, lineIndex);
                    EnsureNode(currentNode, lineIndex);
                    EnsureTokenCount(tokens, 12, lineIndex);
                    var heightDomain = domain!.Value;
                    claims.Add(ParseHeight(tokens, heightDomain.Key, currentNode!.Key, lineIndex, claims.Count - currentNode.FirstClaimIndex));
                    currentNode.ClaimCount++;
                    break;
                case "ifs":
                    EnsureDomain(domain, lineIndex);
                    EnsureNode(currentNode, lineIndex);
                    EnsureTokenCount(tokens, 15, lineIndex);
                    var ifsDomain = domain!.Value;
                    var beforeIfs = claims.Count;
                    AddIfsClaims(tokens, ifsDomain.Key, currentNode!.Key, lineIndex, claims);
                    currentNode.ClaimCount += claims.Count - beforeIfs;
                    break;
                case "flame":
                    EnsureDomain(domain, lineIndex);
                    EnsureNode(currentNode, lineIndex);
                    EnsureTokenCount(tokens, 15, lineIndex);
                    var flameDomain = domain!.Value;
                    var beforeFlame = claims.Count;
                    AddFlameClaims(tokens, flameDomain.Key, currentNode!.Key, lineIndex, claims);
                    currentNode.ClaimCount += claims.Count - beforeFlame;
                    break;
                default:
                    throw new FormatException($"Unknown fractal DSL command `{tokens[0]}` at line {lineIndex + 1}.");
            }
        }

        if (domain is null)
        {
            throw new FormatException("Fractal DSL must declare a `tile <face> <level> <x> <y> <path>` line before claims.");
        }

        return new FractalOwnershipTree(domain.Value, domains, BuildNodes(nodes, claims), claims);
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

    private static AquariumBrushClaim ParseHeight(string[] tokens, AquariumFractalKey domainKey, AquariumFractalKey nodeKey, int lineIndex, int claimIndex)
    {
        var name = tokens[1];
        return HeightClaim(
            domainKey,
            nodeKey,
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

    private static void AddIfsClaims(string[] tokens, AquariumFractalKey domainKey, AquariumFractalKey nodeKey, int lineIndex, List<AquariumBrushClaim> claims)
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

        EmitIfsLevel(domainKey, nodeKey, name, center, radii, levels, branches, childScale, spread, rotationStep, falloff, shapePower, amplitude, seed, tags, claims);
    }

    private static void AddFlameClaims(string[] tokens, AquariumFractalKey domainKey, AquariumFractalKey nodeKey, int lineIndex, List<AquariumBrushClaim> claims)
    {
        var name = tokens[1];
        var levels = ParsePositiveInt(tokens[2], lineIndex);
        var branches = ParsePositiveInt(tokens[3], lineIndex);
        var center = new Vector2(ParseFloat(tokens[4], lineIndex), ParseFloat(tokens[5], lineIndex));
        var radii = new Vector2(ParsePositiveFloat(tokens[6], lineIndex), ParsePositiveFloat(tokens[7], lineIndex));
        var childScale = ParsePositiveFloat(tokens[8], lineIndex);
        var curl = ParseFloat(tokens[9], lineIndex);
        var spread = ParsePositiveFloat(tokens[10], lineIndex);
        var falloff = ParsePositiveFloat(tokens[11], lineIndex);
        var shapePower = ParsePositiveFloat(tokens[12], lineIndex);
        var amplitude = ParseFloat(tokens[13], lineIndex);
        var seed = ParseInt(tokens[14], lineIndex);
        var tags = tokens.Length >= 16 ? tokens[15] : name;

        if (childScale >= 1.0f)
        {
            throw new FormatException($"Flame child scale must be below 1 at line {lineIndex + 1}.");
        }

        EmitFlameLevel(domainKey, nodeKey, name, center, radii, 0.0f, levels, branches, childScale, curl, spread, falloff, shapePower, amplitude, seed, tags, claims);
    }

    private static void EmitIfsLevel(
        AquariumFractalKey domainKey,
        AquariumFractalKey nodeKey,
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
        claims.Add(HeightClaim(domainKey, nodeKey, $"{name}/{level}", claims.Count, center, radii, rotationStep * level, falloff, shapePower, amplitude, seed + level, tags));

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
                domainKey,
                nodeKey,
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

    private static void EmitFlameLevel(
        AquariumFractalKey domainKey,
        AquariumFractalKey nodeKey,
        string name,
        Vector2 center,
        Vector2 radii,
        float spiralAngle,
        int levelsRemaining,
        int branches,
        float childScale,
        float curl,
        float spread,
        float falloff,
        float shapePower,
        float amplitude,
        int seed,
        string tags,
        List<AquariumBrushClaim> claims)
    {
        var level = claims.Count;
        var rotation = spiralAngle + curl * level;
        claims.Add(HeightClaim(domainKey, nodeKey, $"{name}/{level}", claims.Count, center, radii, rotation, falloff, shapePower, amplitude, seed + level, tags));

        if (levelsRemaining <= 1)
        {
            return;
        }

        var nextRadii = radii * childScale;
        var branchStep = MathF.Tau / branches;
        for (var branch = 0; branch < branches; branch++)
        {
            var turn = spiralAngle + curl + (branchStep * branch) + HashAngle(seed, level, branch) * 0.5f;
            var reach = spread * MathF.Max(nextRadii.X, nextRadii.Y) * (1.0f + branch * 0.17f);
            var offset = new Vector2(MathF.Cos(turn), MathF.Sin(turn)) * reach;
            EmitFlameLevel(
                domainKey,
                nodeKey,
                name,
                center + offset,
                nextRadii,
                turn,
                levelsRemaining - 1,
                branches,
                childScale,
                curl,
                spread,
                falloff,
                shapePower,
                amplitude * MathF.Sqrt(childScale),
                seed + 97 + (branch * 17),
                tags,
                claims);
        }
    }

    private static AquariumBrushClaim HeightClaim(
        AquariumFractalKey domainKey,
        AquariumFractalKey nodeKey,
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
            FractalStableKeyBuilder.Child(nodeKey, $"claim/{claimIndex:0000}/{name}"),
            domainKey,
            nodeKey,
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

    private static void EnsureNode(NodeDraft? node, int lineIndex)
    {
        if (node is null)
        {
            throw new FormatException($"Fractal DSL claim at line {lineIndex + 1} appears before a tile root was created.");
        }
    }

    private static AquariumFractalNode[] BuildNodes(IReadOnlyList<NodeDraft> drafts, IReadOnlyList<AquariumBrushClaim> claims)
    {
        var nodes = new AquariumFractalNode[drafts.Count];
        for (var nodeIndex = 0; nodeIndex < drafts.Count; nodeIndex++)
        {
            var draft = drafts[nodeIndex];
            nodes[nodeIndex] = new AquariumFractalNode(
                draft.Key,
                draft.DomainKey,
                default,
                AquariumFractalOperation.Union,
                FirstChildIndex: 0,
                ChildCount: 0,
                draft.FirstClaimIndex,
                draft.ClaimCount,
                BoundsForClaims(claims, draft.FirstClaimIndex, draft.ClaimCount),
                Seed: 0);
        }

        return nodes;
    }

    private static Vector4 BoundsForClaims(IReadOnlyList<AquariumBrushClaim> claims, int firstClaimIndex, int claimCount)
    {
        if (claimCount == 0)
        {
            return Vector4.Zero;
        }

        var minX = float.PositiveInfinity;
        var minY = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var maxY = float.NegativeInfinity;

        for (var index = firstClaimIndex; index < firstClaimIndex + claimCount; index++)
        {
            var claim = claims[index];
            var radius = MathF.Max(claim.Radii.X, claim.Radii.Y);
            minX = MathF.Min(minX, claim.Center.X - radius);
            minY = MathF.Min(minY, claim.Center.Y - radius);
            maxX = MathF.Max(maxX, claim.Center.X + radius);
            maxY = MathF.Max(maxY, claim.Center.Y + radius);
        }

        return new Vector4(minX, minY, maxX, maxY);
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

    private sealed class NodeDraft(AquariumFractalKey key, AquariumFractalKey domainKey, int firstClaimIndex)
    {
        public AquariumFractalKey Key { get; } = key;

        public AquariumFractalKey DomainKey { get; } = domainKey;

        public int FirstClaimIndex { get; } = firstClaimIndex;

        public int ClaimCount { get; set; }
    }
}
