namespace Aquarium.Engine.Audio;

public sealed class AquariumSynthDocument
{
    private readonly List<AquariumSynthPatch> patches = [];

    public static AquariumSynthDocument Empty { get; } = new();

    public bool Enabled { get; init; } = true;

    public float MasterGain { get; init; } = 0.55f;

    public IReadOnlyList<AquariumSynthPatch> Patches => patches;

    public static AquariumSynthDocument Combine(params AquariumSynthDocument[] documents)
    {
        var combined = new AquariumSynthDocument
        {
            Enabled = documents.Any(document => document.Enabled),
            MasterGain = documents.Length == 0 ? 1.0f : documents.Max(document => document.MasterGain)
        };
        foreach (var document in documents)
        {
            if (!document.Enabled)
            {
                continue;
            }

            foreach (var patch in document.Patches)
            {
                combined.patches.Add(patch);
            }
        }

        return combined;
    }

    public AquariumSynthDocument Patch(
        string id,
        string script,
        AquariumSynthTrigger trigger,
        float gain = 1.0f,
        int faustCompileRevision = 0,
        string? faustName = null,
        Action<AquariumSynthPatchStatus>? statusSink = null)
    {
        patches.Add(new AquariumSynthPatch(id, script, trigger, gain, faustCompileRevision, faustName ?? id, statusSink));
        return this;
    }
}

public sealed record AquariumSynthPatch(
    string Id,
    string Script,
    AquariumSynthTrigger Trigger,
    float Gain = 1.0f,
    int FaustCompileRevision = 0,
    string FaustName = "aquarium_patch",
    Action<AquariumSynthPatchStatus>? StatusSink = null);

public sealed record AquariumSynthPatchStatus(
    string Id,
    AquariumSynthPatchCompileState State,
    string Message,
    int Revision,
    double ChangedAtSeconds);

public enum AquariumSynthPatchCompileState
{
    Idle,
    Queued,
    Compiling,
    Ready,
    Failed
}

public readonly record struct AquariumSynthTrigger(float IntervalSeconds, float PhaseSeconds = 0.0f, int FireRevision = 0)
{
    public static AquariumSynthTrigger Repeat(float intervalSeconds, float phaseSeconds = 0.0f) =>
        new(MathF.Max(0.001f, intervalSeconds), phaseSeconds);

    public static AquariumSynthTrigger Manual(int fireRevision) =>
        new(float.PositiveInfinity, 0.0f, fireRevision);
}
