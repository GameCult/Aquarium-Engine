using Aquarium.Engine.Fractal;

namespace Aquarium.Engine.Fractal.Lod;

public static class FractalContributionEstimator
{
    public static AquariumContributionState Observe(
        AquariumContributionState state,
        float observedContribution,
        uint frameIndex,
        bool resident)
    {
        var sampleCount = state.SampleCount + 1;
        var delta = observedContribution - state.MeanContribution;
        var mean = state.MeanContribution + (delta / sampleCount);
        var variance = sampleCount <= 1
            ? 0.0f
            : (((sampleCount - 2) * state.Variance) + (delta * (observedContribution - mean))) / (sampleCount - 1);
        var confidence = Math.Clamp(sampleCount / 16.0f, 0.0f, 1.0f);

        return state with
        {
            MeanContribution = mean,
            Variance = MathF.Max(variance, 0.0f),
            Confidence = confidence,
            SampleCount = sampleCount,
            LastSampledFrame = frameIndex,
            Resident = resident,
        };
    }
}
