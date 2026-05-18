using System.Numerics;

namespace Aquarium.Engine.Fractal.Brushes;

public readonly record struct FractalBrushEnvelope2DPacket(
    Vector4 CenterRadii,
    Vector4 RotationFalloffShape,
    Vector4 Payload);
