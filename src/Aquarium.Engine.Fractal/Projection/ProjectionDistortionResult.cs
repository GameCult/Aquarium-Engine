namespace Aquarium.Engine.Fractal.Projection;

public readonly record struct ProjectionDistortionResult(
    string ProjectionName,
    CubeFace Face,
    int SamplesPerAxis,
    double AverageAreaScale,
    double MinRelativeArea,
    double MaxRelativeArea,
    double MeanAbsoluteRelativeAreaError,
    double RootMeanSquareRelativeAreaError);
