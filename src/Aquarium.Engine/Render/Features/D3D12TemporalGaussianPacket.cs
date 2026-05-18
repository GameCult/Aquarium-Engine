using System.Numerics;
using System.Runtime.InteropServices;

namespace Aquarium.Engine.Render.Features;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct D3D12TemporalGaussianPacket(
    Vector4 CenterHistoryWeight,
    Vector4 PreviousCenterFieldId,
    Vector4 VelocityConfidence,
    Vector4 RadiiFalloff,
    Vector4 Orientation,
    Vector4 ColorOpacity,
    Vector4 ShapePad);
