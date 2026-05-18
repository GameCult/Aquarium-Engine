using System.Numerics;
using System.Runtime.InteropServices;

namespace Aquarium.Engine.Render.Features;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct D3D12GpuSensorCameraPacket(
    Vector4 Intrinsics,
    Vector4 Distortion01,
    Vector4 Distortion23,
    Vector4 ExtentsKind,
    Vector4 TextureRangeTime,
    Vector4 WorldFromSensor0,
    Vector4 WorldFromSensor1,
    Vector4 WorldFromSensor2,
    Vector4 SensorFromWorld0,
    Vector4 SensorFromWorld1,
    Vector4 SensorFromWorld2);
