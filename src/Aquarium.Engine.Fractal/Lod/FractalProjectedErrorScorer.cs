using Aquarium.Engine.Fractal;

namespace Aquarium.Engine.Fractal.Lod;

public static class FractalProjectedErrorScorer
{
    public static float Score(AquariumFractalSummary summary, float projectedPixelsPerWorldUnit)
    {
        if (projectedPixelsPerWorldUnit <= 0.0f || summary.MaxHeightError <= 0.0f)
        {
            return 0.0f;
        }

        var cost = MathF.Max(summary.EstimatedCost, 1.0f);
        return summary.MaxHeightError * projectedPixelsPerWorldUnit / cost;
    }
}
