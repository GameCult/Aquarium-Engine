using System.Globalization;
using System.Text;

namespace Aquarium.Engine.Fractal.Projection;

public static class ProjectionReport
{
    public static string BuildMarkdown(IReadOnlyList<ICubeSphereProjection> projections, int samplesPerAxis)
    {
        ArgumentNullException.ThrowIfNull(projections);

        if (projections.Count == 0)
        {
            throw new ArgumentException("At least one projection is required.", nameof(projections));
        }

        var builder = new StringBuilder();
        builder.AppendLine("# Cube-Sphere Projection Report");
        builder.AppendLine();
        builder.AppendLine("Unit sphere area target per cube-coordinate area is `pi / 6`.");
        builder.AppendLine("Lower mean absolute relative area error is better for terrain density.");
        builder.AppendLine();
        builder.AppendLine("| Projection | Face | Samples | Avg area scale | Min rel area | Max rel area | Mean abs rel error | RMS rel error |");
        builder.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |");

        foreach (var projection in projections)
        {
            foreach (var face in Enum.GetValues<CubeFace>())
            {
                var result = ProjectionDistortionSampler.SampleFace(projection, face, samplesPerAxis);
                builder.Append("| ")
                    .Append(result.ProjectionName)
                    .Append(" | ")
                    .Append(result.Face)
                    .Append(" | ")
                    .Append(result.SamplesPerAxis.ToString(CultureInfo.InvariantCulture))
                    .Append(" | ")
                    .Append(Format(result.AverageAreaScale))
                    .Append(" | ")
                    .Append(Format(result.MinRelativeArea))
                    .Append(" | ")
                    .Append(Format(result.MaxRelativeArea))
                    .Append(" | ")
                    .Append(Format(result.MeanAbsoluteRelativeAreaError))
                    .Append(" | ")
                    .Append(Format(result.RootMeanSquareRelativeAreaError))
                    .AppendLine(" |");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Default for the first terrain slice: `tangent`.");
        builder.AppendLine("It is still cheap, remains seam-compatible with the cube face mapping, and the sampler gives it lower area spread than direct normalization on this harness.");
        return builder.ToString();
    }

    private static string Format(double value)
    {
        return value.ToString("0.000000", CultureInfo.InvariantCulture);
    }
}
