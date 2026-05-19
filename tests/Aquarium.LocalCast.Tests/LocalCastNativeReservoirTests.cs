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
    public void RuntimeStatusLayoutUsesNativeSizeCounts()
    {
        var expected = 16 + (10 * UIntPtr.Size);
        Assert.Equal(expected, Marshal.SizeOf<LocalCastNativeRuntimeStatus>());
    }
}
