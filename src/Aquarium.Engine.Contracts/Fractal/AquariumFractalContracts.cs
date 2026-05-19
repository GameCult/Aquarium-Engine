using System.Numerics;
using System.Runtime.InteropServices;

namespace Aquarium.Engine.Fractal;

public readonly record struct AquariumFractalKey
{
    public AquariumFractalKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Fractal key must not be empty.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString()
    {
        return Value;
    }
}

public enum AquariumFractalDomainKind
{
    Solar,
    Orbital,
    Planetary,
    LatLong,
    CubeSphereTile,
    Surface2D,
    Object3D,
}

public readonly record struct AquariumFractalDomain(
    AquariumFractalKey Key,
    AquariumFractalDomainKind Kind,
    AquariumFractalKey ParentKey,
    Vector4 Parameters0,
    Vector4 Parameters1);

public enum AquariumFractalPayloadKind
{
    Height,
    Material,
    SignedDistance,
}

public enum AquariumFractalSurfacePageKind
{
    Height,
    SignedDistance2D,
    Material,
    Confidence,
}

public readonly record struct AquariumFractalSurfacePageKey
{
    public AquariumFractalSurfacePageKey(
        AquariumFractalKey domainKey,
        AquariumFractalKey nodeKey,
        AquariumFractalSurfacePageKind kind,
        int mipLevel)
    {
        if (domainKey.Value is null)
        {
            throw new ArgumentException("Surface page domain key must not be empty.", nameof(domainKey));
        }

        if (nodeKey.Value is null)
        {
            throw new ArgumentException("Surface page node key must not be empty.", nameof(nodeKey));
        }

        if (mipLevel < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mipLevel), mipLevel, "Surface page mip level must not be negative.");
        }

        DomainKey = domainKey;
        NodeKey = nodeKey;
        Kind = kind;
        MipLevel = mipLevel;
    }

    public AquariumFractalKey DomainKey { get; }

    public AquariumFractalKey NodeKey { get; }

    public AquariumFractalSurfacePageKind Kind { get; }

    public int MipLevel { get; }

    public string Value => $"{DomainKey.Value}:{NodeKey.Value}:{Kind}:mip{MipLevel:D2}";

    public override string ToString()
    {
        return Value;
    }
}

public readonly record struct AquariumFractalSurfacePage(
    AquariumFractalSurfacePageKey Key,
    Vector4 BoundsMinMax,
    int Width,
    int Height,
    int PayloadHandle,
    float MaxError);

public readonly record struct AquariumFractalSdfSplat3DKey
{
    public AquariumFractalSdfSplat3DKey(
        AquariumFractalKey domainKey,
        AquariumFractalKey nodeKey,
        int payloadHandle)
    {
        if (domainKey.Value is null)
        {
            throw new ArgumentException("3D SDF splat domain key must not be empty.", nameof(domainKey));
        }

        if (nodeKey.Value is null)
        {
            throw new ArgumentException("3D SDF splat node key must not be empty.", nameof(nodeKey));
        }

        if (payloadHandle < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(payloadHandle), payloadHandle, "3D SDF splat payload handle must not be negative.");
        }

        DomainKey = domainKey;
        NodeKey = nodeKey;
        PayloadHandle = payloadHandle;
    }

    public AquariumFractalKey DomainKey { get; }

    public AquariumFractalKey NodeKey { get; }

    public int PayloadHandle { get; }

    public string Value => $"{DomainKey.Value}:{NodeKey.Value}:sdf3d:{PayloadHandle:D6}";

    public override string ToString()
    {
        return Value;
    }
}

public readonly record struct AquariumFractalSdfSplat3D(
    AquariumFractalSdfSplat3DKey Key,
    Vector3 Center,
    Quaternion Orientation,
    Vector3 Radii,
    float Falloff,
    float ShapePower,
    float DistanceOffset,
    float MaterialValue,
    float Confidence);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AquariumPackedFractalSdfSplat3D(
    Vector4 CenterRadius,
    Vector4 Orientation,
    Vector4 RadiiFalloff,
    Vector4 MaterialConfidence,
    Vector4 Key);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AquariumPackedSdfEnvelopeReservoir(
    Vector4 CenterRadius,
    Vector4 RadiiFalloff,
    Vector4 WeightTargetCount,
    Vector4 Validation);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AquariumPackedPbrMaterialReservoir(
    Vector4 BaseColorRoughMetal,
    Vector4 NormalVariance,
    Vector4 WeightTargetCount,
    Vector4 Validation);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AquariumPackedRadiosityReservoir(
    Vector4 RadianceDistance,
    Vector4 DirectionOcclusion,
    Vector4 WeightTargetCount,
    Vector4 Validation);

public readonly record struct AquariumBrushClaim(
    AquariumFractalKey Key,
    AquariumFractalKey DomainKey,
    AquariumFractalKey NodeKey,
    AquariumFractalPayloadKind PayloadKind,
    Vector2 Center,
    Vector2 Radii,
    float RotationRadians,
    float Falloff,
    float ShapePower,
    float Amplitude,
    int Seed,
    string Tags);

public enum AquariumFractalOperation
{
    Union,
    Subtract,
    Common,
    Repeat,
    Refine,
    Capture,
    Bind,
}

public readonly record struct AquariumFractalNode(
    AquariumFractalKey Key,
    AquariumFractalKey DomainKey,
    AquariumFractalKey ParentKey,
    AquariumFractalOperation Operation,
    int FirstChildIndex,
    int ChildCount,
    int FirstClaimIndex,
    int ClaimCount,
    Vector4 BoundsMinMax,
    int Seed);

public readonly record struct AquariumFractalSummary(
    AquariumFractalKey NodeKey,
    Vector4 BoundsMinMax,
    float MaxHeightError,
    float MaxMaterialDelta,
    float EstimatedCost,
    int DescendantCount);

public readonly record struct AquariumContributionState(
    AquariumFractalKey NodeKey,
    float MeanContribution,
    float Variance,
    float Confidence,
    int SampleCount,
    uint LastSampledFrame,
    bool Resident);

public readonly record struct AquariumSelectedCut(
    AquariumFractalKey NodeKey,
    float Score,
    float Fade,
    bool UsesSummary,
    bool RequestedChildren);
