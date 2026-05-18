using System.Numerics;
using Aquarium.Engine.Audio;
using Aquarium.Epiphany.State;

namespace Aquarium.Epiphany.Voice;

public sealed class FaceVoiceEndpoint : IDisposable
{
    private readonly RealtimeFaceVoiceSession session;

    public FaceVoiceEndpoint(AquariumAudioDocument audio, AquariumFaceVoiceEndpointState state, string appServerUri)
    {
        Id = NormalizeId(state.Id);
        DisplayName = string.IsNullOrWhiteSpace(state.DisplayName) ? Id : state.DisplayName.Trim();
        ThreadId = state.ThreadId.Trim();
        Voice = state.Voice.Trim();
        Prompt = state.Prompt;
        Anchor = new Vector2(state.AnchorX, state.AnchorY);
        Enabled = state.Enabled;
        session = new RealtimeFaceVoiceSession(audio);
        ApplySessionSettings(appServerUri);
    }

    public string Id { get; private set; }

    public string DisplayName { get; set; }

    public string ThreadId { get; set; }

    public string Voice { get; set; }

    public string Prompt { get; set; }

    public Vector2 Anchor { get; set; }

    public bool Enabled { get; set; } = true;

    public RealtimeFaceVoiceSession Session => session;

    public float SpatialGain { get; private set; } = 1.0f;

    public float SpatialPan { get; private set; }

    public string Status => session.Status;

    public string AudioStats => session.AudioStats;

    public string LastError => session.LastError;

    public string Transcript => session.Transcript;

    public void ApplySessionSettings(string appServerUri)
    {
        session.AppServerUri = appServerUri.Trim();
        session.ThreadId = ThreadId.Trim();
        session.Voice = Voice.Trim();
        session.Prompt = Prompt;
    }

    public void UpdateSpatial(Vector2 listenerWorld, float audibleRadius)
    {
        var delta = Anchor - listenerWorld;
        var distance = delta.Length();
        var safeRadius = Math.Max(audibleRadius, 0.001f);
        var normalizedDistance = Math.Clamp(distance / safeRadius, 0.0f, 1.0f);
        SpatialGain = Math.Clamp(1.0f / (1.0f + normalizedDistance * normalizedDistance * 3.0f), 0.18f, 1.0f);
        SpatialPan = Math.Clamp(delta.X / safeRadius, -0.95f, 0.95f);
        session.SetOutputSpatial(SpatialGain, SpatialPan);
    }

    public AquariumFaceVoiceEndpointState ToState()
    {
        return new AquariumFaceVoiceEndpointState
        {
            Id = Id,
            DisplayName = DisplayName.Trim(),
            ThreadId = ThreadId.Trim(),
            Voice = Voice.Trim(),
            Prompt = Prompt,
            AnchorX = Anchor.X,
            AnchorY = Anchor.Y,
            Enabled = Enabled
        };
    }

    public void Dispose()
    {
        session.Dispose();
    }

    public static string NormalizeId(string value)
    {
        var trimmed = value.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(trimmed) ? "face" : trimmed;
    }
}
