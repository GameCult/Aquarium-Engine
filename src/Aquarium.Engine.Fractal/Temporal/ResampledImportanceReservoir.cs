namespace Aquarium.Engine.Fractal.Temporal;

public sealed class ResampledImportanceReservoir<TSample>
{
    private const float Epsilon = 1.0e-8f;

    private TSample selectedSample = default!;

    public bool HasSample { get; private set; }

    public TSample SelectedSample
    {
        get
        {
            if (!HasSample)
            {
                throw new InvalidOperationException("The reservoir does not contain a selected sample.");
            }

            return selectedSample;
        }
    }

    public float SelectedTarget { get; private set; }

    public float WeightSum { get; private set; }

    public int CandidateCount { get; private set; }

    public float ContributionWeight
    {
        get
        {
            if (!HasSample || CandidateCount <= 0 || SelectedTarget <= Epsilon)
            {
                return 0.0f;
            }

            return WeightSum / (CandidateCount * SelectedTarget);
        }
    }

    public bool Add(ResampledImportanceCandidate<TSample> candidate, double randomUnit)
    {
        return AddWeightedSample(
            candidate.Sample,
            candidate.Target,
            candidate.ImportanceWeight,
            candidate.CandidateCount,
            randomUnit);
    }

    public bool Merge(ResampledImportanceReservoir<TSample> other, double randomUnit)
    {
        if (other is null)
        {
            throw new ArgumentNullException(nameof(other));
        }

        if (!other.HasSample)
        {
            return false;
        }

        return AddWeightedSample(
            other.SelectedSample,
            other.SelectedTarget,
            other.WeightSum,
            other.CandidateCount,
            randomUnit);
    }

    private bool AddWeightedSample(
        TSample sample,
        float target,
        float weightSum,
        int candidateCount,
        double randomUnit)
    {
        if (candidateCount <= 0 || !float.IsFinite(target) || target <= 0.0f || !float.IsFinite(weightSum) || weightSum <= 0.0f)
        {
            return false;
        }

        if (!double.IsFinite(randomUnit) || randomUnit < 0.0 || randomUnit >= 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(randomUnit), "Reservoir random value must be in [0, 1).");
        }

        var previousWeightSum = WeightSum;
        var nextWeightSum = previousWeightSum + weightSum;
        if (!float.IsFinite(nextWeightSum) || nextWeightSum <= 0.0f)
        {
            return false;
        }

        WeightSum = nextWeightSum;
        CandidateCount = checked(CandidateCount + candidateCount);

        if (!HasSample || randomUnit < weightSum / nextWeightSum)
        {
            selectedSample = sample;
            SelectedTarget = target;
            HasSample = true;
            return true;
        }

        return false;
    }
}
