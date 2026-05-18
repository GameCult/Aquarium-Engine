using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Aquarium.Engine.Render.Features;

[StructLayout(LayoutKind.Sequential)]
internal struct D3D12HeightFieldBrushConstants
{
    public const int MaxBrushCount = 64;

    private const int VectorCount = MaxBrushCount * 4;

    private VectorTable values;

    public static D3D12HeightFieldBrushConstants FromBrushes(IReadOnlyList<AquariumHeightFieldBrush> brushes)
    {
        var constants = new D3D12HeightFieldBrushConstants();
        var brushCount = Math.Min(brushes.Count, MaxBrushCount);
        for (var index = 0; index < brushCount; index++)
        {
            var brush = brushes[index];
            var radiusY = brush.RadiusY > 0.0f ? brush.RadiusY : brush.Radius;
            constants.Set(
                index,
                new Vector4(brush.Center, brush.Radius, radiusY),
                new Vector4(brush.Power, brush.Amplitude, brush.RotationRadians, brush.EnvelopeFalloff),
                new Vector4(brush.WaveAmplitude, brush.WaveFrequency, brush.WaveSpeed, brush.WaveSinePower),
                new Vector4(brush.DomainFace, brush.DomainLevel, brush.DomainX, brush.DomainY));
        }

        return constants;
    }

    private void Set(int brushIndex, Vector4 centerRadius, Vector4 shape, Vector4 wave, Vector4 domain)
    {
        if ((uint)brushIndex >= MaxBrushCount)
        {
            throw new ArgumentOutOfRangeException(nameof(brushIndex), brushIndex, "Height Field brush index is outside the fixed brush table.");
        }

        SetVector(brushIndex, centerRadius);
        SetVector(MaxBrushCount + brushIndex, shape);
        SetVector(MaxBrushCount * 2 + brushIndex, wave);
        SetVector(MaxBrushCount * 3 + brushIndex, domain);
    }

    private void SetVector(int index, Vector4 value)
    {
        if ((uint)index >= VectorCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Height Field constant index is outside the fixed vector table.");
        }

        values[index] = value;
    }

    [InlineArray(VectorCount)]
    private struct VectorTable
    {
        private Vector4 element0;
    }
}
