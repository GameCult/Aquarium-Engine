using System.Runtime.InteropServices;

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

public interface ILocalCastNativeProducer : IDisposable
{
    ulong NextSequence { get; }

    bool TryPush(ulong timestampNs, ulong arrivalNs, ulong payloadHandle, out LocalCastNativeSampleHandle sample);

    bool TryPushAudioBlock(
        ulong timestampNs,
        ulong arrivalNs,
        in LocalCastNativeAudioBlockDescriptor descriptor,
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
}
