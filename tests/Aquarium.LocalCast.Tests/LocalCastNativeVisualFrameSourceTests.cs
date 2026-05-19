using System.Numerics;
using System.Runtime.InteropServices;
using Aquarium.LocalCast;

namespace Aquarium.LocalCast.Tests;

public sealed class LocalCastNativeVisualFrameSourceTests
{
    [Fact]
    public void ReadsLatestRenderPacketFromNativeRuntimeThroughInjectedDecoder()
    {
        var sample = new LocalCastNativeSampleHandle
        {
            SensorIdHash = 11,
            TimestampNs = 1_000,
            ArrivalNs = 1_010,
            Sequence = 7,
            PayloadHandle = 99,
            Flags = LocalCastNativeSampleFlags.None,
        };
        var runtime = new FakeNativeRuntime(sample, renderPacketCount: 2);
        var source = new LocalCastNativeVisualFrameSource(
            runtime,
            (LocalCastNativeSampleHandle handle, out LocalCastVisualFrame frame) =>
            {
                frame = FrameFromPayload(handle.PayloadHandle, handle.TimestampNs);
                return true;
            });

        Assert.True(source.TryReadLatest(out var frame));
        Assert.Equal(99, frame.FrameId);
        Assert.Equal(1_000, frame.SourceTimeMaxNs);
        Assert.Equal(1, runtime.ViewReadCount);
    }

    [Fact]
    public void RejectsMissingOrNonLiveRenderPacket()
    {
        var diagnostic = new LocalCastNativeSampleHandle
        {
            SensorIdHash = 11,
            TimestampNs = 1_000,
            ArrivalNs = 1_010,
            Sequence = 7,
            PayloadHandle = 99,
            Flags = LocalCastNativeSampleFlags.Diagnostic,
        };
        var source = new LocalCastNativeVisualFrameSource(
            new FakeNativeRuntime(diagnostic, renderPacketCount: 1),
            (LocalCastNativeSampleHandle _, out LocalCastVisualFrame frame) =>
            {
                frame = FrameFromPayload(1, 1);
                return true;
            });

        Assert.False(source.TryReadLatest(out _));

        var empty = new LocalCastNativeVisualFrameSource(
            new FakeNativeRuntime(diagnostic, renderPacketCount: 0),
            (LocalCastNativeSampleHandle _, out LocalCastVisualFrame frame) =>
            {
                frame = FrameFromPayload(1, 1);
                return true;
            });
        Assert.False(empty.TryReadLatest(out _));
    }

    [Fact]
    public void DescriptorDecoderReadsTimingAndDelegatesPointBuffer()
    {
        var descriptor = new LocalCastNativeRenderPacketDescriptor
        {
            PointBufferHandle = 500,
            PointCount = 1,
            PointStrideBytes = 48,
            TargetWidth = 1280,
            TargetHeight = 720,
            SourceTimeMinNs = 900,
            SourceTimeMaxNs = 1_000,
            PresentTimeNs = 1_050,
            AudioAlignmentTimeNs = 1_025,
            MetadataHandle = 77,
        };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<LocalCastNativeRenderPacketDescriptor>());
        try
        {
            Marshal.StructureToPtr(descriptor, pointer, false);
            var sample = new LocalCastNativeSampleHandle
            {
                SensorIdHash = 11,
                TimestampNs = 1_000,
                ArrivalNs = 1_010,
                Sequence = 42,
                PayloadHandle = (ulong)pointer,
                Flags = LocalCastNativeSampleFlags.None,
            };
            var decoder = new LocalCastNativeRenderDescriptorDecoder(
                (in LocalCastNativeRenderPacketDescriptor read, LocalCastNativeSampleHandle _, out IReadOnlyList<LocalCastVisualPoint> points) =>
                {
                    Assert.Equal(500u, read.PointBufferHandle);
                    Assert.Equal(1u, read.PointCount);
                    points =
                    [
                        new LocalCastVisualPoint(
                            "native:decoded",
                            new Vector3(0.2f, 0.1f, 1.0f),
                            0.04f,
                            new Vector4(0.5f, 0.4f, 0.3f, 0.9f),
                            0.7f,
                            1_000),
                    ];
                    return true;
                });

            Assert.True(decoder.TryDecode(sample, out var frame));
            Assert.Equal(42, frame.FrameId);
            Assert.Equal(1_010, frame.CreatedMonotonicNs);
            Assert.Equal(900, frame.SourceTimeMinNs);
            Assert.Equal(1_000, frame.SourceTimeMaxNs);
            Assert.Equal(1_050, frame.PresentTimeNs);
            Assert.Equal(1_025, frame.AudioAlignmentTimeNs);
            Assert.Equal(1280, frame.TargetWidth);
            Assert.Equal(720, frame.TargetHeight);
            Assert.Single(frame.Points);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    [Fact]
    public void DescriptorDecoderRejectsNullPayloadAndPointDecoderFailure()
    {
        var decoder = new LocalCastNativeRenderDescriptorDecoder(
            (in LocalCastNativeRenderPacketDescriptor _, LocalCastNativeSampleHandle _, out IReadOnlyList<LocalCastVisualPoint> points) =>
            {
                points = [];
                return false;
            });

        Assert.False(decoder.TryDecode(new LocalCastNativeSampleHandle { Flags = LocalCastNativeSampleFlags.None }, out _));

        var descriptor = new LocalCastNativeRenderPacketDescriptor();
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<LocalCastNativeRenderPacketDescriptor>());
        try
        {
            Marshal.StructureToPtr(descriptor, pointer, false);
            Assert.False(
                decoder.TryDecode(
                    new LocalCastNativeSampleHandle
                    {
                        PayloadHandle = (ulong)pointer,
                        Flags = LocalCastNativeSampleFlags.None,
                    },
                    out _));
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static LocalCastVisualFrame FrameFromPayload(ulong payloadHandle, ulong timestampNs)
    {
        return new LocalCastVisualFrame
        {
            SchemaVersion = LocalCastVisualStateReader.RenderFrameSchemaId,
            FrameId = (long)payloadHandle,
            CreatedMonotonicNs = (long)timestampNs,
            SourceTimeMinNs = (long)timestampNs,
            SourceTimeMaxNs = (long)timestampNs,
            PresentTimeNs = (long)timestampNs,
            AudioAlignmentTimeNs = (long)timestampNs,
            SpoutSenderName = "LocalCastBridge Point Cloud",
            TargetWidth = 1920,
            TargetHeight = 1080,
            Points =
            [
                new LocalCastVisualPoint(
                    "native:point",
                    new Vector3(0.1f, 0.2f, 1.2f),
                    0.03f,
                    new Vector4(0.7f, 0.6f, 0.5f, 0.9f),
                    0.8f,
                    (long)timestampNs),
            ],
        };
    }

    private sealed class FakeNativeRuntime : ILocalCastNativeRuntime
    {
        private readonly LocalCastNativeSampleHandle sample;
        private readonly UIntPtr renderPacketCount;

        public FakeNativeRuntime(LocalCastNativeSampleHandle sample, int renderPacketCount)
        {
            this.sample = sample;
            this.renderPacketCount = (UIntPtr)renderPacketCount;
        }

        public int ViewReadCount { get; private set; }

        public bool TryGetStatus(out LocalCastNativeRuntimeStatus status)
        {
            status = new LocalCastNativeRuntimeStatus
            {
                TotalSampleCount = renderPacketCount,
                RenderPacketCount = renderPacketCount,
            };
            return true;
        }

        public bool TryReadSample(nuint index, out LocalCastNativeSampleKind kind, out LocalCastNativeSampleHandle sample)
        {
            kind = LocalCastNativeSampleKind.RenderPacket;
            sample = this.sample;
            return true;
        }

        public bool TryReadViewSample(LocalCastNativeSampleKind kind, nuint index, out LocalCastNativeSampleHandle sample)
        {
            ViewReadCount++;
            sample = this.sample;
            return kind == LocalCastNativeSampleKind.RenderPacket && index + 1 == renderPacketCount;
        }

        public bool TryReadLatestForSensor(LocalCastNativeSampleKind kind, ulong sensorIdHash, out LocalCastNativeSampleHandle sample)
        {
            sample = this.sample;
            return true;
        }

        public void Dispose()
        {
        }
    }
}
