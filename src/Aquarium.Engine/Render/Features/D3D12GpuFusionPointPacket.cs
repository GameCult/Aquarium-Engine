using System.Runtime.InteropServices;

namespace Aquarium.Engine.Render.Features;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct D3D12GpuFusionPointPacket(
    ulong StableKeyHash,
    ulong SourceTimestampNs,
    float X,
    float Y,
    float Z,
    float RadiusMeters,
    float Red,
    float Green,
    float Blue,
    float Alpha,
    float Confidence);
