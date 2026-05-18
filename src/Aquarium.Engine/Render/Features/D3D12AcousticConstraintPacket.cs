using System.Numerics;
using System.Runtime.InteropServices;

namespace Aquarium.Engine.Render.Features;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct D3D12AcousticConstraintPacket(
    Vector4 PositionRadius,
    Vector4 VelocityConfidence,
    Vector4 KindTimePad);
