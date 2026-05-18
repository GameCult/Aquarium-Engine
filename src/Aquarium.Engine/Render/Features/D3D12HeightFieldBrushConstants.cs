using System.Numerics;
using System.Runtime.InteropServices;

namespace Aquarium.Engine.Render.Features;

[StructLayout(LayoutKind.Sequential)]
internal record struct D3D12HeightFieldBrushConstants(
    Vector4 CenterRadius0,
    Vector4 CenterRadius1,
    Vector4 CenterRadius2,
    Vector4 CenterRadius3,
    Vector4 CenterRadius4,
    Vector4 CenterRadius5,
    Vector4 CenterRadius6,
    Vector4 CenterRadius7,
    Vector4 CenterRadius8,
    Vector4 CenterRadius9,
    Vector4 CenterRadius10,
    Vector4 CenterRadius11,
    Vector4 CenterRadius12,
    Vector4 CenterRadius13,
    Vector4 CenterRadius14,
    Vector4 CenterRadius15,
    Vector4 Shape0,
    Vector4 Shape1,
    Vector4 Shape2,
    Vector4 Shape3,
    Vector4 Shape4,
    Vector4 Shape5,
    Vector4 Shape6,
    Vector4 Shape7,
    Vector4 Shape8,
    Vector4 Shape9,
    Vector4 Shape10,
    Vector4 Shape11,
    Vector4 Shape12,
    Vector4 Shape13,
    Vector4 Shape14,
    Vector4 Shape15,
    Vector4 Wave0,
    Vector4 Wave1,
    Vector4 Wave2,
    Vector4 Wave3,
    Vector4 Wave4,
    Vector4 Wave5,
    Vector4 Wave6,
    Vector4 Wave7,
    Vector4 Wave8,
    Vector4 Wave9,
    Vector4 Wave10,
    Vector4 Wave11,
    Vector4 Wave12,
    Vector4 Wave13,
    Vector4 Wave14,
    Vector4 Wave15)
{
    public const int MaxBrushCount = 16;

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
            case 8:
                CenterRadius8 = centerRadius;
                Shape8 = shape;
                Wave8 = wave;
                break;
            case 9:
                CenterRadius9 = centerRadius;
                Shape9 = shape;
                Wave9 = wave;
                break;
            case 10:
                CenterRadius10 = centerRadius;
                Shape10 = shape;
                Wave10 = wave;
                break;
            case 11:
                CenterRadius11 = centerRadius;
                Shape11 = shape;
                Wave11 = wave;
                break;
            case 12:
                CenterRadius12 = centerRadius;
                Shape12 = shape;
                Wave12 = wave;
                break;
            case 13:
                CenterRadius13 = centerRadius;
                Shape13 = shape;
                Wave13 = wave;
                break;
            case 14:
                CenterRadius14 = centerRadius;
                Shape14 = shape;
                Wave14 = wave;
                break;
            case 15:
                CenterRadius15 = centerRadius;
                Shape15 = shape;
                Wave15 = wave;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(index), index, "Height Field brush index is outside the fixed brush table.");
        }
    }
}
