using System.Numerics;

namespace Aquarium.Engine.Fractal.Temporal;

public readonly record struct TemporalGaussianObservation(
    string StableKey,
    Vector3 Center,
    Vector3 Radii,
    Quaternion Orientation,
    Vector4 ColorOpacity,
    float Confidence,
    float ObservedTimeSeconds,
    float Falloff = 4.0f,
    float ShapePower = 1.0f,
    int FieldId = 0);
