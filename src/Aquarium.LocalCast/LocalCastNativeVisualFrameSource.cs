using System.Runtime.InteropServices;
using System.Numerics;

namespace Aquarium.LocalCast;

public delegate bool LocalCastNativeRenderPayloadDecoder(
    LocalCastNativeSampleHandle sample,
    out LocalCastVisualFrame frame);

public delegate bool LocalCastNativeRenderPointBufferDecoder(
    in LocalCastNativeRenderPacketDescriptor descriptor,
    LocalCastNativeSampleHandle sample,
    out IReadOnlyList<LocalCastVisualPoint> points);

public sealed class LocalCastNativeRenderDescriptorDecoder
{
    private readonly LocalCastNativeRenderPointBufferDecoder decodePointBuffer;
    private readonly string spoutSenderName;

    public LocalCastNativeRenderDescriptorDecoder(
        LocalCastNativeRenderPointBufferDecoder decodePointBuffer,
        string spoutSenderName = "LocalCastBridge Point Cloud")
    {
        this.decodePointBuffer = decodePointBuffer;
        this.spoutSenderName = spoutSenderName;
    }

    public bool TryDecode(LocalCastNativeSampleHandle sample, out LocalCastVisualFrame frame)
    {
        frame = EmptyFrame();
        if (!sample.IsLive || sample.PayloadHandle == 0)
        {
            return false;
        }

        var descriptor = Marshal.PtrToStructure<LocalCastNativeRenderPacketDescriptor>((IntPtr)sample.PayloadHandle);
        if (!decodePointBuffer(in descriptor, sample, out var points))
        {
            return false;
        }

        frame = new LocalCastVisualFrame
        {
            SchemaVersion = LocalCastVisualStateReader.RenderFrameSchemaId,
            FrameId = checked((long)sample.Sequence),
            CreatedMonotonicNs = checked((long)sample.ArrivalNs),
            SourceTimeMinNs = checked((long)descriptor.SourceTimeMinNs),
            SourceTimeMaxNs = checked((long)descriptor.SourceTimeMaxNs),
            PresentTimeNs = checked((long)descriptor.PresentTimeNs),
            AudioAlignmentTimeNs = checked((long)descriptor.AudioAlignmentTimeNs),
            SpoutSenderName = spoutSenderName,
            TargetWidth = checked((int)descriptor.TargetWidth),
            TargetHeight = checked((int)descriptor.TargetHeight),
            Points = points,
        };
        return true;
    }

    public static bool DecodeNativePointBuffer(
        in LocalCastNativeRenderPacketDescriptor descriptor,
        LocalCastNativeSampleHandle _,
        out IReadOnlyList<LocalCastVisualPoint> points)
    {
        points = [];
        var pointSize = Marshal.SizeOf<LocalCastNativeRenderPoint>();
        if (descriptor.PointCount == 0)
        {
            return true;
        }

        if (descriptor.PointBufferHandle == 0 || descriptor.PointStrideBytes < pointSize)
        {
            return false;
        }

        var decoded = new LocalCastVisualPoint[checked((int)descriptor.PointCount)];
        var cursor = (IntPtr)descriptor.PointBufferHandle;
        var stride = checked((int)descriptor.PointStrideBytes);
        for (var index = 0; index < decoded.Length; index++)
        {
            var native = Marshal.PtrToStructure<LocalCastNativeRenderPoint>(IntPtr.Add(cursor, index * stride));
            decoded[index] = new LocalCastVisualPoint(
                $"native:{native.StableKeyHash:x16}",
                new Vector3(native.X, native.Y, native.Z),
                native.RadiusMeters,
                new Vector4(native.Red, native.Green, native.Blue, native.Alpha),
                native.Confidence,
                checked((long)native.SourceTimestampNs));
        }

        points = decoded;
        return true;
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

public sealed class LocalCastNativeVisualFrameSource : ILocalCastVisualFrameSource
{
    private readonly ILocalCastNativeRuntime runtime;
    private readonly LocalCastNativeRenderPayloadDecoder decodeRenderPayload;

    public LocalCastNativeVisualFrameSource(
        ILocalCastNativeRuntime runtime,
        string description = "native LocalcastRuntime")
        : this(
            runtime,
            new LocalCastNativeRenderDescriptorDecoder(LocalCastNativeRenderDescriptorDecoder.DecodeNativePointBuffer).TryDecode,
            description)
    {
    }

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
