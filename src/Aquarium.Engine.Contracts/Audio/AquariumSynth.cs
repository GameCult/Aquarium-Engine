namespace Aquarium.Engine.Audio;

public sealed class AquariumSynthDocument
{
    private readonly List<AquariumSynthPatch> patches = [];

    public static AquariumSynthDocument Empty { get; } = new();

    public bool Enabled { get; init; } = true;

    public float MasterGain { get; init; } = 0.55f;

    public IReadOnlyList<AquariumSynthPatch> Patches => patches;

    public AquariumSynthDocument Patch(
        string id,
        string script,
        AquariumSynthTrigger trigger,
        float gain = 1.0f,
        int faustCompileRevision = 0,
        string? faustName = null)
    {
        patches.Add(new AquariumSynthPatch(id, script, trigger, gain, faustCompileRevision, faustName ?? id));
        return this;
    }
}

public sealed record AquariumSynthPatch(
    string Id,
    string Script,
    AquariumSynthTrigger Trigger,
    float Gain = 1.0f,
    int FaustCompileRevision = 0,
    string FaustName = "aquarium_patch");

public readonly record struct AquariumSynthTrigger(float IntervalSeconds, float PhaseSeconds = 0.0f)
{
    public static AquariumSynthTrigger Repeat(float intervalSeconds, float phaseSeconds = 0.0f) =>
        new(MathF.Max(0.001f, intervalSeconds), phaseSeconds);
}
