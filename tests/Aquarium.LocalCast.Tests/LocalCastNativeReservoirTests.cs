using System.Runtime.InteropServices;
using Aquarium.LocalCast;

namespace Aquarium.LocalCast.Tests;

public sealed class LocalCastNativeReservoirTests
{
    [Fact]
    public void SampleKindIdsMatchLocalcastReservoirHeader()
    {
        Assert.Equal(0u, (uint)LocalCastNativeSampleKind.CameraFrame);
        Assert.Equal(5u, (uint)LocalCastNativeSampleKind.AudioBlock);
        Assert.Equal(8u, (uint)LocalCastNativeSampleKind.RenderPacket);
    }

    [Fact]
    public void SampleHandleLayoutMatchesNativeAbi()
    {
        Assert.Equal(48, Marshal.SizeOf<LocalCastNativeSampleHandle>());

        var handle = new LocalCastNativeSampleHandle
        {
            SensorIdHash = 10,
            TimestampNs = 20,
            ArrivalNs = 21,
            Sequence = 7,
            PayloadHandle = 99,
            Flags = LocalCastNativeSampleFlags.None,
        };

        Assert.True(handle.IsLive);
        handle.Flags = LocalCastNativeSampleFlags.Diagnostic;
        Assert.False(handle.IsLive);
    }

    [Fact]
    public void AudioBlockDescriptorLayoutMatchesNativeAbi()
    {
        Assert.Equal(40, Marshal.SizeOf<LocalCastNativeAudioBlockDescriptor>());
        Assert.Equal(1u, (uint)LocalCastNativeAudioSampleFormat.Float32Interleaved);

        var descriptor = new LocalCastNativeAudioBlockDescriptor
        {
            DataHandle = 99,
            FrameCount = 1024,
            ChannelCount = 6,
            SampleRateHz = 48_000,
            SampleFormat = LocalCastNativeAudioSampleFormat.Float32Interleaved,
            StartSample = 2_048,
            ChannelLayoutHash = 77,
        };

        Assert.Equal(99u, descriptor.DataHandle);
        Assert.Equal(1024u, descriptor.FrameCount);
        Assert.Equal(6u, descriptor.ChannelCount);
        Assert.Equal(48_000u, descriptor.SampleRateHz);
        Assert.Equal(2_048u, descriptor.StartSample);
        Assert.Equal(77u, descriptor.ChannelLayoutHash);
    }

    [Fact]
    public void RenderPacketDescriptorLayoutMatchesNativeAbi()
    {
        Assert.Equal(64, Marshal.SizeOf<LocalCastNativeRenderPacketDescriptor>());

        var descriptor = new LocalCastNativeRenderPacketDescriptor
        {
            PointBufferHandle = 99,
            PointCount = 4096,
            PointStrideBytes = 64,
            TargetWidth = 1920,
            TargetHeight = 1080,
            SourceTimeMinNs = 1_000,
            SourceTimeMaxNs = 2_000,
            PresentTimeNs = 2_350,
            AudioAlignmentTimeNs = 2_300,
            MetadataHandle = 77,
        };

        Assert.Equal(99u, descriptor.PointBufferHandle);
        Assert.Equal(4096u, descriptor.PointCount);
        Assert.Equal(64u, descriptor.PointStrideBytes);
        Assert.Equal(1920u, descriptor.TargetWidth);
        Assert.Equal(1080u, descriptor.TargetHeight);
        Assert.Equal(1_000u, descriptor.SourceTimeMinNs);
        Assert.Equal(2_000u, descriptor.SourceTimeMaxNs);
        Assert.Equal(2_350u, descriptor.PresentTimeNs);
        Assert.Equal(2_300u, descriptor.AudioAlignmentTimeNs);
        Assert.Equal(77u, descriptor.MetadataHandle);
    }

    [Fact]
    public void RenderPointLayoutMatchesNativeAbi()
    {
        Assert.Equal(56, Marshal.SizeOf<LocalCastNativeRenderPoint>());

        var point = new LocalCastNativeRenderPoint
        {
            StableKeyHash = 0xCAFE,
            SourceTimestampNs = 1_900,
            X = 0.1f,
            Y = 0.2f,
            Z = 1.2f,
            RadiusMeters = 0.03f,
            Red = 0.7f,
            Green = 0.6f,
            Blue = 0.5f,
            Alpha = 0.9f,
            Confidence = 0.8f,
        };

        Assert.Equal(0xCAFEu, point.StableKeyHash);
        Assert.Equal(1_900u, point.SourceTimestampNs);
        Assert.Equal(0.03f, point.RadiusMeters);
    }

    [Fact]
    public void RuntimeStatusLayoutUsesNativeSizeCounts()
    {
        var expected = 16 + (10 * UIntPtr.Size);
        Assert.Equal(expected, Marshal.SizeOf<LocalCastNativeRuntimeStatus>());
    }
}
