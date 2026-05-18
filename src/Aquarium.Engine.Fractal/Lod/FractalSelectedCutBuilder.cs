using Aquarium.Engine.Fractal;

namespace Aquarium.Engine.Fractal.Lod;

public static class FractalSelectedCutBuilder
{
    public static AquariumSelectedCut[] Build(
        IReadOnlyList<AquariumFractalSummary> summaries,
        Func<AquariumFractalSummary, float> projectedPixelsPerWorldUnit,
        float maxEstimatedCost,
        float childRequestScore = 1.0f)
    {
        ArgumentNullException.ThrowIfNull(summaries);
        ArgumentNullException.ThrowIfNull(projectedPixelsPerWorldUnit);

        if (maxEstimatedCost <= 0.0f)
        {
            return [];
        }

        var candidates = summaries
            .Select(summary =>
            {
                var score = FractalProjectedErrorScorer.Score(summary, projectedPixelsPerWorldUnit(summary));
                return new Candidate(summary, score);
            })
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Summary.NodeKey.Value, StringComparer.Ordinal)
            .ToArray();

        var cuts = new List<AquariumSelectedCut>();
        var usedCost = 0.0f;
        foreach (var candidate in candidates)
        {
            var cost = MathF.Max(candidate.Summary.EstimatedCost, 1.0f);
            if (usedCost + cost > maxEstimatedCost)
            {
                continue;
            }

            usedCost += cost;
            cuts.Add(new AquariumSelectedCut(
                candidate.Summary.NodeKey,
                candidate.Score,
                Fade: FadeFromScore(candidate.Score, childRequestScore),
                UsesSummary: true,
                RequestedChildren: candidate.Score >= childRequestScore));
        }

        return cuts.ToArray();
    }

    private static float FadeFromScore(float score, float childRequestScore)
    {
        if (score <= 0.0f)
        {
            return 0.0f;
        }

        var denominator = MathF.Max(childRequestScore, 0.000001f);
        return Math.Clamp(score / denominator, 0.0f, 1.0f);
    }

    private readonly record struct Candidate(AquariumFractalSummary Summary, float Score);
}
