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
    public void RuntimeStatusLayoutUsesNativeSizeCounts()
    {
        var expected = 16 + (10 * UIntPtr.Size);
        Assert.Equal(expected, Marshal.SizeOf<LocalCastNativeRuntimeStatus>());
    }
}
