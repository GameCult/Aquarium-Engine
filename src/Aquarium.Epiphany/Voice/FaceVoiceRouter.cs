using System.Numerics;
using Aquarium.Engine.Audio;
using Aquarium.Epiphany.State;

namespace Aquarium.Epiphany.Voice;

public sealed class FaceVoiceRouter : IDisposable
{
    private readonly AquariumAudioDocument audio;
    private readonly List<FaceVoiceEndpoint> endpoints = [];
    private int activeIndex;
    private float audibleRadius = 24.0f;

    public FaceVoiceRouter(AquariumAudioDocument audio, AquariumLiveState state)
    {
        this.audio = audio;
        AppServerUri = state.FaceVoiceAppServerUri;
        AutoSelect = state.FaceVoiceAutoSelect;
        AudibleRadius = state.FaceVoiceAudibleRadius;

        var endpointStates = state.FaceVoiceEndpoints.Count > 0
            ? state.FaceVoiceEndpoints
            : CreateLegacyEndpointStates(state);
        foreach (var endpointState in endpointStates)
        {
            endpoints.Add(new FaceVoiceEndpoint(audio, endpointState, AppServerUri));
        }

        if (endpoints.Count == 0)
        {
            endpoints.Add(new FaceVoiceEndpoint(audio, new AquariumFaceVoiceEndpointState(), AppServerUri));
        }
    }

    public string AppServerUri { get; set; }

    public bool AutoSelect { get; set; } = true;

    public float AudibleRadius
    {
        get => audibleRadius;
        set => audibleRadius = Math.Clamp(value, 1.0f, 128.0f);
    }

    public Vector2 ListenerWorld { get; private set; }

    public IReadOnlyList<FaceVoiceEndpoint> Endpoints => endpoints;

    public FaceVoiceEndpoint ActiveEndpoint => endpoints[Math.Clamp(activeIndex, 0, endpoints.Count - 1)];

    public string ActiveSummary
    {
        get
        {
            var endpoint = ActiveEndpoint;
            var distance = Vector2.Distance(ListenerWorld, endpoint.Anchor);
            return $"{endpoint.DisplayName} ({endpoint.Id}) {distance:0.0}m gain {endpoint.SpatialGain:0.00} pan {endpoint.SpatialPan:0.00}";
        }
    }

    public string ActiveStatus => $"{ActiveEndpoint.Status}; audio {ActiveEndpoint.AudioStats}";

    public string LastError => ActiveEndpoint.LastError;

    public string Transcript => ActiveEndpoint.Transcript;

    public string RouteSummary => string.Join(
        " | ",
        endpoints.Select(endpoint =>
        {
            var mark = ReferenceEquals(endpoint, ActiveEndpoint) ? "*" : "";
            var distance = Vector2.Distance(ListenerWorld, endpoint.Anchor);
            return $"{mark}{endpoint.Id}:{distance:0.0} g{endpoint.SpatialGain:0.00} p{endpoint.SpatialPan:0.00}";
        }));

    public void UpdateListener(Vector2 listenerWorld)
    {
        ListenerWorld = listenerWorld;
        foreach (var endpoint in endpoints)
        {
            endpoint.UpdateSpatial(listenerWorld, AudibleRadius);
        }

        if (!AutoSelect || endpoints.Count <= 1)
        {
            return;
        }

        activeIndex = SelectNearestEndpointIndex(listenerWorld);
    }

    public bool Select(string id)
    {
        var normalized = FaceVoiceEndpoint.NormalizeId(id);
        var index = endpoints.FindIndex(endpoint => endpoint.Id == normalized);
        if (index < 0)
        {
            return false;
        }

        activeIndex = index;
        AutoSelect = false;
        return true;
    }

    public FaceVoiceEndpoint AddOrUpdate(string id, string threadId, Vector2 anchor, string? voice = null)
    {
        var normalized = FaceVoiceEndpoint.NormalizeId(id);
        var endpoint = endpoints.FirstOrDefault(candidate => candidate.Id == normalized);
        if (endpoint is null)
        {
            endpoint = new FaceVoiceEndpoint(
                audio,
                new AquariumFaceVoiceEndpointState
                {
                    Id = normalized,
                    DisplayName = normalized,
                    ThreadId = threadId.Trim(),
                    Voice = string.IsNullOrWhiteSpace(voice) ? "marin" : voice.Trim(),
                    AnchorX = anchor.X,
                    AnchorY = anchor.Y
                },
                AppServerUri);
            endpoints.Add(endpoint);
            endpoint.UpdateSpatial(ListenerWorld, AudibleRadius);
            activeIndex = endpoints.Count - 1;
            AutoSelect = false;
            return endpoint;
        }

        endpoint.ThreadId = threadId.Trim();
        endpoint.Anchor = anchor;
        if (!string.IsNullOrWhiteSpace(voice))
        {
            endpoint.Voice = voice.Trim();
        }

        endpoint.ApplySessionSettings(AppServerUri);
        endpoint.UpdateSpatial(ListenerWorld, AudibleRadius);
        activeIndex = endpoints.IndexOf(endpoint);
        AutoSelect = false;
        return endpoint;
    }

    public bool Remove(string id)
    {
        if (endpoints.Count <= 1)
        {
            return false;
        }

        var normalized = FaceVoiceEndpoint.NormalizeId(id);
        var index = endpoints.FindIndex(endpoint => endpoint.Id == normalized);
        if (index < 0)
        {
            return false;
        }

        endpoints[index].Dispose();
        endpoints.RemoveAt(index);
        activeIndex = Math.Clamp(activeIndex, 0, endpoints.Count - 1);
        return true;
    }

    public void StartActive()
    {
        ActiveEndpoint.ApplySessionSettings(AppServerUri);
        ActiveEndpoint.Session.Start();
    }

    public void StartAll()
    {
        foreach (var endpoint in endpoints.Where(endpoint => endpoint.Enabled))
        {
            endpoint.ApplySessionSettings(AppServerUri);
            endpoint.UpdateSpatial(ListenerWorld, AudibleRadius);
            endpoint.Session.Start();
        }
    }

    public void StopActive()
    {
        ActiveEndpoint.Session.Stop();
    }

    public void StopAll()
    {
        foreach (var endpoint in endpoints)
        {
            endpoint.Session.Stop();
        }
    }

    public void SendText(string text)
    {
        ActiveEndpoint.ApplySessionSettings(AppServerUri);
        ActiveEndpoint.Session.SendText(text);
    }

    public void ClearActiveLog()
    {
        ActiveEndpoint.Session.ClearTranscript();
    }

    public List<AquariumFaceVoiceEndpointState> ExportState()
    {
        return endpoints.Select(endpoint => endpoint.ToState()).ToList();
    }

    public void Dispose()
    {
        foreach (var endpoint in endpoints)
        {
            endpoint.Dispose();
        }
    }

    private int SelectNearestEndpointIndex(Vector2 listenerWorld)
    {
        var selected = activeIndex;
        var selectedDistance = DistanceSquared(listenerWorld, endpoints[selected].Anchor);
        for (var index = 0; index < endpoints.Count; index++)
        {
            var endpoint = endpoints[index];
            if (!endpoint.Enabled)
            {
                continue;
            }

            var distance = DistanceSquared(listenerWorld, endpoint.Anchor);
            if (distance + 1.0f < selectedDistance)
            {
                selected = index;
                selectedDistance = distance;
            }
        }

        return selected;
    }

    private static float DistanceSquared(Vector2 a, Vector2 b)
    {
        var delta = a - b;
        return Vector2.Dot(delta, delta);
    }

    private static List<AquariumFaceVoiceEndpointState> CreateLegacyEndpointStates(AquariumLiveState state)
    {
        return
        [
            new AquariumFaceVoiceEndpointState
            {
                Id = "face",
                DisplayName = "Face",
                ThreadId = state.FaceVoiceThreadId,
                Voice = state.FaceVoiceVoice,
                Prompt = state.FaceVoicePrompt,
                AnchorX = 0.0f,
                AnchorY = 6.0f,
                Enabled = true
            }
        ];
    }
}
