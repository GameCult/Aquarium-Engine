using System.Runtime.InteropServices;
using System.Text;

namespace Aquarium.LocalCast;

public enum LocalCastNativeSampleKind : uint
{
    CameraFrame = 0,
    CameraFeature = 1,
    SceneRay = 2,
    SurfaceClaim = 3,
    MaterialClaim = 4,
    AudioBlock = 5,
    PhaseClaim = 6,
    EventClaim = 7,
    RenderPacket = 8,
}

public enum LocalCastNativeAudioSampleFormat : uint
{
    Float32Interleaved = 1,
}

[Flags]
public enum LocalCastNativeSampleFlags : uint
{
    None = 0,
    Diagnostic = 1u << 0,
}

[StructLayout(LayoutKind.Sequential)]
public struct LocalCastNativeSampleHandle
{
    public ulong SensorIdHash;
    public ulong TimestampNs;
    public ulong ArrivalNs;
    public ulong Sequence;
    public ulong PayloadHandle;
    public LocalCastNativeSampleFlags Flags;
    public uint Reserved;

    public readonly bool IsLive => Flags == LocalCastNativeSampleFlags.None;
}

[StructLayout(LayoutKind.Sequential)]
public struct LocalCastNativeAudioBlockDescriptor
{
    public ulong DataHandle;
    public uint FrameCount;
    public uint ChannelCount;
    public uint SampleRateHz;
    public LocalCastNativeAudioSampleFormat SampleFormat;
    public ulong StartSample;
    public ulong ChannelLayoutHash;
}

[StructLayout(LayoutKind.Sequential)]
public struct LocalCastNativeRenderPacketDescriptor
{
    public ulong PointBufferHandle;
    public uint PointCount;
    public uint PointStrideBytes;
    public uint TargetWidth;
    public uint TargetHeight;
    public ulong SourceTimeMinNs;
    public ulong SourceTimeMaxNs;
    public ulong PresentTimeNs;
    public ulong AudioAlignmentTimeNs;
    public ulong MetadataHandle;
}

[StructLayout(LayoutKind.Sequential)]
public struct LocalCastNativeRenderPoint
{
    public ulong StableKeyHash;
    public ulong SourceTimestampNs;
    public float X;
    public float Y;
    public float Z;
    public float RadiusMeters;
    public float Red;
    public float Green;
    public float Blue;
    public float Alpha;
    public float Confidence;
}

[StructLayout(LayoutKind.Sequential)]
public struct LocalCastNativeRuntimeStatus
{
    public ulong EdgeNs;
    public ulong WindowStartNs;
    public UIntPtr TotalSampleCount;
    public UIntPtr CameraFrameCount;
    public UIntPtr CameraFeatureCount;
    public UIntPtr SceneRayCount;
    public UIntPtr SurfaceClaimCount;
    public UIntPtr MaterialClaimCount;
    public UIntPtr AudioBlockCount;
    public UIntPtr PhaseClaimCount;
    public UIntPtr EventClaimCount;
    public UIntPtr RenderPacketCount;
}

public interface ILocalCastNativeRuntime : IDisposable
{
    bool TryGetStatus(out LocalCastNativeRuntimeStatus status);

    bool TryReadSample(nuint index, out LocalCastNativeSampleKind kind, out LocalCastNativeSampleHandle sample);

    bool TryReadViewSample(LocalCastNativeSampleKind kind, nuint index, out LocalCastNativeSampleHandle sample);

    bool TryReadLatestForSensor(LocalCastNativeSampleKind kind, ulong sensorIdHash, out LocalCastNativeSampleHandle sample);
}

public static class LocalCastNativeSourceId
{
    public static ulong HashUtf8(string sourceId)
    {
        if (string.IsNullOrEmpty(sourceId))
        {
            return 0;
        }

        var bytes = Encoding.UTF8.GetBytes(sourceId);
        return LocalCastNativeMethods.HashSourceId(bytes, (UIntPtr)bytes.Length);
    }
}

public interface ILocalCastNativeProducer : IDisposable
{
    ulong NextSequence { get; }

    bool TryPush(ulong timestampNs, ulong arrivalNs, ulong payloadHandle, out LocalCastNativeSampleHandle sample);

    bool TryPushAudioBlock(
        ulong timestampNs,
        ulong arrivalNs,
        in LocalCastNativeAudioBlockDescriptor descriptor,
        out LocalCastNativeSampleHandle sample);

    bool TryPushRenderPacket(
        ulong timestampNs,
        ulong arrivalNs,
        in LocalCastNativeRenderPacketDescriptor descriptor,
        out LocalCastNativeSampleHandle sample);
}

public sealed class LocalCastNativeRuntime : ILocalCastNativeRuntime
{
    private IntPtr handle;

    public LocalCastNativeRuntime(ulong durationNs)
    {
        handle = LocalCastNativeMethods.RuntimeCreate(durationNs);
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("LocalCast native runtime creation failed.");
        }
    }

    internal IntPtr Handle => handle;

    public bool TryGetStatus(out LocalCastNativeRuntimeStatus status)
    {
        EnsureNotDisposed();
        return LocalCastNativeMethods.RuntimeStatus(handle, out status);
    }

    public bool TryReadSample(nuint index, out LocalCastNativeSampleKind kind, out LocalCastNativeSampleHandle sample)
    {
        EnsureNotDisposed();
        var ok = LocalCastNativeMethods.RuntimeSampleAt(handle, index, out var rawKind, out sample);
        kind = (LocalCastNativeSampleKind)rawKind;
        return ok;
    }

    public bool TryReadViewSample(LocalCastNativeSampleKind kind, nuint index, out LocalCastNativeSampleHandle sample)
    {
        EnsureNotDisposed();
        return LocalCastNativeMethods.RuntimeViewSampleAt(handle, (uint)kind, index, out sample);
    }

    public bool TryReadLatestForSensor(LocalCastNativeSampleKind kind, ulong sensorIdHash, out LocalCastNativeSampleHandle sample)
    {
        EnsureNotDisposed();
        return LocalCastNativeMethods.RuntimeLatestForSensor(handle, (uint)kind, sensorIdHash, out sample);
    }

    public LocalCastNativeProducer CreateProducer(LocalCastNativeSampleKind kind, ulong sensorIdHash, ulong initialSequence = 0)
    {
        EnsureNotDisposed();
        return new LocalCastNativeProducer(this, kind, sensorIdHash, initialSequence);
    }

    public void Dispose()
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        LocalCastNativeMethods.RuntimeDestroy(handle);
        handle = IntPtr.Zero;
    }

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);
    }
}

public sealed class LocalCastNativeProducer : ILocalCastNativeProducer
{
    private readonly LocalCastNativeRuntime runtime;
    private IntPtr handle;

    internal LocalCastNativeProducer(LocalCastNativeRuntime runtime, LocalCastNativeSampleKind kind, ulong sensorIdHash, ulong initialSequence)
    {
        this.runtime = runtime;
        handle = LocalCastNativeMethods.ProducerCreate((uint)kind, sensorIdHash, initialSequence);
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("LocalCast native producer creation failed.");
        }
    }

    public ulong NextSequence
    {
        get
        {
            EnsureNotDisposed();
            return LocalCastNativeMethods.ProducerNextSequence(handle);
        }
    }

    public bool TryPush(ulong timestampNs, ulong arrivalNs, ulong payloadHandle, out LocalCastNativeSampleHandle sample)
    {
        EnsureNotDisposed();
        return LocalCastNativeMethods.ProducerPush(handle, runtime.Handle, timestampNs, arrivalNs, payloadHandle, out sample);
    }

    public bool TryPushAudioBlock(
        ulong timestampNs,
        ulong arrivalNs,
        in LocalCastNativeAudioBlockDescriptor descriptor,
        out LocalCastNativeSampleHandle sample)
    {
        EnsureNotDisposed();
        return LocalCastNativeMethods.ProducerPushAudioBlock(handle, runtime.Handle, timestampNs, arrivalNs, in descriptor, out sample);
    }

    public bool TryPushRenderPacket(
        ulong timestampNs,
        ulong arrivalNs,
        in LocalCastNativeRenderPacketDescriptor descriptor,
        out LocalCastNativeSampleHandle sample)
    {
        EnsureNotDisposed();
        return LocalCastNativeMethods.ProducerPushRenderPacket(handle, runtime.Handle, timestampNs, arrivalNs, in descriptor, out sample);
    }

    public void Dispose()
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        LocalCastNativeMethods.ProducerDestroy(handle);
        handle = IntPtr.Zero;
    }

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(handle == IntPtr.Zero, this);
    }
}

internal static class LocalCastNativeMethods
{
    private const string LibraryName = "localcast_reservoir";

    [DllImport(LibraryName, EntryPoint = "localcast_hash_source_id")]
    internal static extern ulong HashSourceId(byte[] bytes, UIntPtr byteLen);

    [DllImport(LibraryName, EntryPoint = "localcast_runtime_create")]
    internal static extern IntPtr RuntimeCreate(ulong durationNs);

    [DllImport(LibraryName, EntryPoint = "localcast_runtime_destroy")]
    internal static extern void RuntimeDestroy(IntPtr runtime);

    [DllImport(LibraryName, EntryPoint = "localcast_runtime_status")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool RuntimeStatus(IntPtr runtime, out LocalCastNativeRuntimeStatus status);

    [DllImport(LibraryName, EntryPoint = "localcast_runtime_sample_at")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool RuntimeSampleAt(
        IntPtr runtime,
        UIntPtr index,
        out uint sampleKind,
        out LocalCastNativeSampleHandle sample);

    [DllImport(LibraryName, EntryPoint = "localcast_runtime_view_sample_at")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool RuntimeViewSampleAt(
        IntPtr runtime,
        uint sampleKind,
        UIntPtr index,
        out LocalCastNativeSampleHandle sample);

    [DllImport(LibraryName, EntryPoint = "localcast_runtime_latest_for_sensor")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool RuntimeLatestForSensor(
        IntPtr runtime,
        uint sampleKind,
        ulong sensorIdHash,
        out LocalCastNativeSampleHandle sample);

    [DllImport(LibraryName, EntryPoint = "localcast_producer_create")]
    internal static extern IntPtr ProducerCreate(uint sampleKind, ulong sensorIdHash, ulong initialSequence);

    [DllImport(LibraryName, EntryPoint = "localcast_producer_destroy")]
    internal static extern void ProducerDestroy(IntPtr producer);

    [DllImport(LibraryName, EntryPoint = "localcast_producer_next_sequence")]
    internal static extern ulong ProducerNextSequence(IntPtr producer);

    [DllImport(LibraryName, EntryPoint = "localcast_producer_push")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool ProducerPush(
        IntPtr producer,
        IntPtr runtime,
        ulong timestampNs,
        ulong arrivalNs,
        ulong payloadHandle,
        out LocalCastNativeSampleHandle sample);

    [DllImport(LibraryName, EntryPoint = "localcast_producer_push_audio_block")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool ProducerPushAudioBlock(
        IntPtr producer,
        IntPtr runtime,
        ulong timestampNs,
        ulong arrivalNs,
        in LocalCastNativeAudioBlockDescriptor descriptor,
        out LocalCastNativeSampleHandle sample);

    [DllImport(LibraryName, EntryPoint = "localcast_producer_push_render_packet")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool ProducerPushRenderPacket(
        IntPtr producer,
        IntPtr runtime,
        ulong timestampNs,
        ulong arrivalNs,
        in LocalCastNativeRenderPacketDescriptor descriptor,
        out LocalCastNativeSampleHandle sample);
}
