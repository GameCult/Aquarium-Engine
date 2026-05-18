using System.Numerics;
using System.Runtime.InteropServices;

namespace Aquarium.Engine.Render.Features;

[StructLayout(LayoutKind.Sequential)]
internal struct D3D12HeightFieldBrushConstants
{
    public const int MaxBrushCount = 32;

    private const int VectorCount = MaxBrushCount * 3;

    private Vector4 value00;
    private Vector4 value01;
    private Vector4 value02;
    private Vector4 value03;
    private Vector4 value04;
    private Vector4 value05;
    private Vector4 value06;
    private Vector4 value07;
    private Vector4 value08;
    private Vector4 value09;
    private Vector4 value10;
    private Vector4 value11;
    private Vector4 value12;
    private Vector4 value13;
    private Vector4 value14;
    private Vector4 value15;
    private Vector4 value16;
    private Vector4 value17;
    private Vector4 value18;
    private Vector4 value19;
    private Vector4 value20;
    private Vector4 value21;
    private Vector4 value22;
    private Vector4 value23;
    private Vector4 value24;
    private Vector4 value25;
    private Vector4 value26;
    private Vector4 value27;
    private Vector4 value28;
    private Vector4 value29;
    private Vector4 value30;
    private Vector4 value31;
    private Vector4 value32;
    private Vector4 value33;
    private Vector4 value34;
    private Vector4 value35;
    private Vector4 value36;
    private Vector4 value37;
    private Vector4 value38;
    private Vector4 value39;
    private Vector4 value40;
    private Vector4 value41;
    private Vector4 value42;
    private Vector4 value43;
    private Vector4 value44;
    private Vector4 value45;
    private Vector4 value46;
    private Vector4 value47;
    private Vector4 value48;
    private Vector4 value49;
    private Vector4 value50;
    private Vector4 value51;
    private Vector4 value52;
    private Vector4 value53;
    private Vector4 value54;
    private Vector4 value55;
    private Vector4 value56;
    private Vector4 value57;
    private Vector4 value58;
    private Vector4 value59;
    private Vector4 value60;
    private Vector4 value61;
    private Vector4 value62;
    private Vector4 value63;
    private Vector4 value64;
    private Vector4 value65;
    private Vector4 value66;
    private Vector4 value67;
    private Vector4 value68;
    private Vector4 value69;
    private Vector4 value70;
    private Vector4 value71;
    private Vector4 value72;
    private Vector4 value73;
    private Vector4 value74;
    private Vector4 value75;
    private Vector4 value76;
    private Vector4 value77;
    private Vector4 value78;
    private Vector4 value79;
    private Vector4 value80;
    private Vector4 value81;
    private Vector4 value82;
    private Vector4 value83;
    private Vector4 value84;
    private Vector4 value85;
    private Vector4 value86;
    private Vector4 value87;
    private Vector4 value88;
    private Vector4 value89;
    private Vector4 value90;
    private Vector4 value91;
    private Vector4 value92;
    private Vector4 value93;
    private Vector4 value94;
    private Vector4 value95;

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
                new Vector4(brush.WaveAmplitude, brush.WaveFrequency, brush.WaveSpeed, brush.WaveSinePower));
        }

        return constants;
    }

    private void Set(int brushIndex, Vector4 centerRadius, Vector4 shape, Vector4 wave)
    {
        if ((uint)brushIndex >= MaxBrushCount)
        {
            throw new ArgumentOutOfRangeException(nameof(brushIndex), brushIndex, "Height Field brush index is outside the fixed brush table.");
        }

        SetVector(brushIndex, centerRadius);
        SetVector(MaxBrushCount + brushIndex, shape);
        SetVector(MaxBrushCount * 2 + brushIndex, wave);
    }

    private void SetVector(int index, Vector4 value)
    {
        if ((uint)index >= VectorCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Height Field constant index is outside the fixed vector table.");
        }

        var span = MemoryMarshal.CreateSpan(ref value00, VectorCount);
        span[index] = value;
    }
}
