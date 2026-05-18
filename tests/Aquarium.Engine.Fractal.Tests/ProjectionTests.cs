using System.Numerics;
using Aquarium.Engine.Fractal;
using Aquarium.Engine.Fractal.Projection;

namespace Aquarium.Engine.Fractal.Tests;

public sealed class ProjectionTests
{
    private static readonly ICubeSphereProjection[] Projections =
    [
        new NormalizeCubeSphereProjection(),
        new TangentCubeSphereProjection(),
    ];

    [Fact]
    public void ProjectionCandidatesReturnUnitSpherePoints()
    {
        foreach (var projection in Projections)
        {
            var point = projection.Project(new CubeFacePosition(CubeFace.PositiveZ, 0.35, -0.25));

            Assert.Equal(1.0f, point.Length(), 5);
        }
    }

    [Fact]
    public void FaceEdgesMeetAtTheSameProjectedPoints()
    {
        var edges = new (CubeFacePosition A, CubeFacePosition B)[]
        {
            (new CubeFacePosition(CubeFace.PositiveZ, 1.0, -0.5), new CubeFacePosition(CubeFace.PositiveX, -1.0, -0.5)),
            (new CubeFacePosition(CubeFace.PositiveZ, -1.0, 0.25), new CubeFacePosition(CubeFace.NegativeX, 1.0, 0.25)),
            (new CubeFacePosition(CubeFace.PositiveZ, 0.5, 1.0), new CubeFacePosition(CubeFace.PositiveY, 0.5, -1.0)),
            (new CubeFacePosition(CubeFace.PositiveZ, -0.25, -1.0), new CubeFacePosition(CubeFace.NegativeY, -0.25, 1.0)),
        };

        foreach (var projection in Projections)
        {
            foreach (var (a, b) in edges)
            {
                AssertVectorNear(projection.Project(a), projection.Project(b), 0.000001f);
            }
        }
    }

    [Fact]
    public void TangentProjectionHasLowerSampledAreaSpreadThanNormalizeBaseline()
    {
        var normalize = ProjectionDistortionSampler.SampleFace(new NormalizeCubeSphereProjection(), CubeFace.PositiveZ, 16);
        var tangent = ProjectionDistortionSampler.SampleFace(new TangentCubeSphereProjection(), CubeFace.PositiveZ, 16);

        Assert.True(tangent.RootMeanSquareRelativeAreaError < normalize.RootMeanSquareRelativeAreaError);
        Assert.True(tangent.MeanAbsoluteRelativeAreaError < normalize.MeanAbsoluteRelativeAreaError);
    }

    [Fact]
    public void ProjectionReportNamesMeasuredDefault()
    {
        var report = ProjectionReport.BuildMarkdown(Projections, 8);

        Assert.Contains("| tangent | PositiveZ |", report);
        Assert.Contains("Default for the first terrain slice: `tangent`.", report);
    }

    [Fact]
    public void CheckedInProjectionReportMatchesSampler()
    {
        var reportPath = Path.Combine(FindRepositoryRoot(), "research", "rendering", "fractal-projection-report.md");
        var expectedReport = ProjectionReport.BuildMarkdown(Projections, 16);

        Assert.Equal(NormalizeLineEndings(expectedReport), NormalizeLineEndings(File.ReadAllText(reportPath)));
    }

    private static void AssertVectorNear(Vector3 actual, Vector3 expected, float tolerance)
    {
        Assert.InRange(Vector3.Distance(actual, expected), 0.0f, tolerance);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Aquarium.Engine.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find Aquarium.Engine.sln.");
    }

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
