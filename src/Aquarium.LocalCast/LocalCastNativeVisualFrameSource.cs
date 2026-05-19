namespace Aquarium.LocalCast;

public delegate bool LocalCastNativeRenderPayloadDecoder(
    LocalCastNativeSampleHandle sample,
    out LocalCastVisualFrame frame);

public sealed class LocalCastNativeVisualFrameSource : ILocalCastVisualFrameSource
{
    private readonly ILocalCastNativeRuntime runtime;
    private readonly LocalCastNativeRenderPayloadDecoder decodeRenderPayload;

    public LocalCastNativeVisualFrameSource(
        ILocalCastNativeRuntime runtime,
        LocalCastNativeRenderPayloadDecoder decodeRenderPayload,
        string description = "native LocalcastRuntime")
    {
        this.runtime = runtime;
        this.decodeRenderPayload = decodeRenderPayload;
        Description = description;
    }

    public string Description { get; }

    public bool TryReadLatest(out LocalCastVisualFrame frame)
    {
        frame = EmptyFrame();
        if (!runtime.TryGetStatus(out var status) || status.RenderPacketCount == UIntPtr.Zero)
        {
            return false;
        }

        var index = status.RenderPacketCount - 1;
        if (!runtime.TryReadViewSample(LocalCastNativeSampleKind.RenderPacket, index, out var sample) || !sample.IsLive)
        {
            return false;
        }

        return decodeRenderPayload(sample, out frame);
    }

    public bool TryReadLatestClapEvents(out LocalCastClapCalibrationFrame frame)
    {
        frame = new LocalCastClapCalibrationFrame
        {
            SchemaVersion = LocalCastVisualStateReader.ClapEventsSchemaId,
            FrameId = -1,
            CreatedMonotonicNs = 0,
            Events = [],
        };
        return false;
    }

    private static LocalCastVisualFrame EmptyFrame()
    {
        return new LocalCastVisualFrame
        {
            SchemaVersion = LocalCastVisualStateReader.RenderFrameSchemaId,
            FrameId = -1,
            CreatedMonotonicNs = 0,
            SourceTimeMinNs = 0,
            SourceTimeMaxNs = 0,
            PresentTimeNs = 0,
            AudioAlignmentTimeNs = 0,
            SpoutSenderName = string.Empty,
            TargetWidth = 0,
            TargetHeight = 0,
            Points = [],
        };
    }
}
