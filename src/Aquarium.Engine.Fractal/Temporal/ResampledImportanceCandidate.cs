namespace Aquarium.Engine.Fractal.Temporal;

public readonly record struct ResampledImportanceCandidate<TSample>(
    TSample Sample,
    float Target,
    float SourcePdf,
    int CandidateCount = 1)
{
    public float ImportanceWeight
    {
        get
        {
            if (!float.IsFinite(Target) || !float.IsFinite(SourcePdf) || Target <= 0.0f || SourcePdf <= 0.0f)
            {
                return 0.0f;
            }

            return Target / SourcePdf;
        }
    }
}
