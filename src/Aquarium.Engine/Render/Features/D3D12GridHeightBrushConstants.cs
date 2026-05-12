using System.Numerics;
using System.Runtime.InteropServices;

namespace Aquarium.Engine.Render.Features;

[StructLayout(LayoutKind.Sequential)]
internal record struct D3D12GridHeightBrushConstants(
    Vector4 CenterRadius0,
    Vector4 CenterRadius1,
    Vector4 CenterRadius2,
    Vector4 CenterRadius3,
    Vector4 CenterRadius4,
    Vector4 CenterRadius5,
    Vector4 CenterRadius6,
    Vector4 CenterRadius7,
    Vector4 Shape0,
    Vector4 Shape1,
    Vector4 Shape2,
    Vector4 Shape3,
    Vector4 Shape4,
    Vector4 Shape5,
    Vector4 Shape6,
    Vector4 Shape7,
    Vector4 Wave0,
    Vector4 Wave1,
    Vector4 Wave2,
    Vector4 Wave3,
    Vector4 Wave4,
    Vector4 Wave5,
    Vector4 Wave6,
    Vector4 Wave7)
{
    public const int MaxBrushCount = 8;

    public static D3D12GridHeightBrushConstants FromBrushes(IReadOnlyList<AquariumGridHeightBrush> brushes)
    {
        var constants = new D3D12GridHeightBrushConstants();
        var brushCount = Math.Min(brushes.Count, MaxBrushCount);
        for (var index = 0; index < brushCount; index++)
        {
            var brush = brushes[index];
            constants.Set(
                index,
                new Vector4(brush.Center, brush.Radius, 0.0f),
                new Vector4(brush.Power, brush.Amplitude, 0.0f, 0.0f),
                new Vector4(brush.WaveAmplitude, brush.WaveFrequency, brush.WaveSpeed, brush.WaveSinePower));
        }

        return constants;
    }

    private void Set(int index, Vector4 centerRadius, Vector4 shape, Vector4 wave)
    {
        switch (index)
        {
            case 0:
                CenterRadius0 = centerRadius;
                Shape0 = shape;
                Wave0 = wave;
                break;
            case 1:
                CenterRadius1 = centerRadius;
                Shape1 = shape;
                Wave1 = wave;
                break;
            case 2:
                CenterRadius2 = centerRadius;
                Shape2 = shape;
                Wave2 = wave;
                break;
            case 3:
                CenterRadius3 = centerRadius;
                Shape3 = shape;
                Wave3 = wave;
                break;
            case 4:
                CenterRadius4 = centerRadius;
                Shape4 = shape;
                Wave4 = wave;
                break;
            case 5:
                CenterRadius5 = centerRadius;
                Shape5 = shape;
                Wave5 = wave;
                break;
            case 6:
                CenterRadius6 = centerRadius;
                Shape6 = shape;
                Wave6 = wave;
                break;
            case 7:
                CenterRadius7 = centerRadius;
                Shape7 = shape;
                Wave7 = wave;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(index), index, "Grid height brush index is outside the fixed brush table.");
        }
    }
}
