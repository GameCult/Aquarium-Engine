using System.Runtime.InteropServices;

namespace Aquarium.Engine.Audio;

internal sealed class WasapiAudioDevice : IDisposable
{
    private readonly object sync = new();
    private readonly List<Voice> voices = [];
    private readonly Thread thread;
    private volatile int outputSampleRate = 44100;
    private volatile bool running = true;
    private volatile bool started;

    public WasapiAudioDevice()
    {
        thread = new Thread(AudioThread)
        {
            IsBackground = true,
            Name = "Aquarium WASAPI Audio"
        };
        thread.Start();
    }

    public void Play(ReadOnlySpan<float> monoSamples, int sourceSampleRate)
    {
        Play(monoSamples, sourceSampleRate, 1.0f, 1.0f);
    }

    public void Play(ReadOnlySpan<float> monoSamples, int sourceSampleRate, float leftGain, float rightGain)
    {
        if (monoSamples.Length == 0)
        {
            return;
        }

        var copy = sourceSampleRate == outputSampleRate
            ? monoSamples.ToArray()
            : Resample(monoSamples, sourceSampleRate, outputSampleRate);
        lock (sync)
        {
            voices.Add(new Voice(copy, Math.Clamp(leftGain, 0.0f, 4.0f), Math.Clamp(rightGain, 0.0f, 4.0f)));
            if (voices.Count > 64)
            {
                voices.RemoveRange(0, voices.Count - 64);
            }
        }
    }

    public void Dispose()
    {
        running = false;
        if (started)
        {
            thread.Join(TimeSpan.FromSeconds(2.0));
        }
    }

    private void AudioThread()
    {
        started = true;
        var comInitialized = false;
        IAudioClient? audioClient = null;
        try
        {
            var hr = CoInitializeEx(IntPtr.Zero, CoInit.Multithreaded);
            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            comInitialized = true;
#pragma warning disable CA1416
            var enumeratorType = Type.GetTypeFromCLSID(MMDeviceEnumeratorClsid, throwOnError: true)
                ?? throw new InvalidOperationException("MMDeviceEnumerator COM type is not available.");
#pragma warning restore CA1416
            var deviceEnumerator = (IMMDeviceEnumerator)(Activator.CreateInstance(enumeratorType)
                ?? throw new InvalidOperationException("failed to create MMDeviceEnumerator"));
            Marshal.ThrowExceptionForHR(deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Console, out var device));
            var audioClientId = typeof(IAudioClient).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref audioClientId, ClsCtx.All, IntPtr.Zero, out var audioClientObject));
            audioClient = (IAudioClient)audioClientObject;
            Marshal.ThrowExceptionForHR(audioClient.GetMixFormat(out var mixFormatPointer));
            try
            {
                var format = WasapiFormat.FromPointer(mixFormatPointer);
                outputSampleRate = format.SampleRate;
                Marshal.ThrowExceptionForHR(audioClient.Initialize(
                    AudioClientShareMode.Shared,
                    AudioClientStreamFlags.NoPersist,
                    0,
                    0,
                    mixFormatPointer,
                    IntPtr.Zero));
                Marshal.ThrowExceptionForHR(audioClient.GetBufferSize(out var bufferFrameCount));
                var renderClientId = typeof(IAudioRenderClient).GUID;
                Marshal.ThrowExceptionForHR(audioClient.GetService(ref renderClientId, out var renderClientObject));
                var renderClient = (IAudioRenderClient)renderClientObject;
                PrimeSilence(renderClient, bufferFrameCount, format);
                Marshal.ThrowExceptionForHR(audioClient.Start());
                Console.WriteLine($"Aquarium WASAPI audio started: {format.SampleRate} Hz, {format.Channels} channels, {format.BitsPerSample}-bit {format.SampleKind}.");
                while (running)
                {
                    Marshal.ThrowExceptionForHR(audioClient.GetCurrentPadding(out var padding));
                    var availableFrames = bufferFrameCount - padding;
                    if (availableFrames > 0)
                    {
                        Marshal.ThrowExceptionForHR(renderClient.GetBuffer(availableFrames, out var buffer));
                        try
                        {
                            FillBuffer(buffer, availableFrames, format);
                        }
                        finally
                        {
                            Marshal.ThrowExceptionForHR(renderClient.ReleaseBuffer(availableFrames, 0));
                        }
                    }

                    Thread.Sleep(4);
                }

                audioClient.Stop();
            }
            finally
            {
                Marshal.FreeCoTaskMem(mixFormatPointer);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Aquarium WASAPI audio unavailable: {ex.Message}");
        }
        finally
        {
            audioClient?.Stop();
            if (comInitialized)
            {
                CoUninitialize();
            }
        }
    }

    private void PrimeSilence(IAudioRenderClient renderClient, int frameCount, WasapiFormat format)
    {
        Marshal.ThrowExceptionForHR(renderClient.GetBuffer(frameCount, out var buffer));
        try
        {
            FillSilence(buffer, frameCount, format);
        }
        finally
        {
            Marshal.ThrowExceptionForHR(renderClient.ReleaseBuffer(frameCount, 0));
        }
    }

    private void FillBuffer(IntPtr buffer, int frameCount, WasapiFormat format)
    {
        if (format.SampleKind == WasapiSampleKind.Float32)
        {
            FillFloat32(buffer, frameCount, format.Channels);
        }
        else
        {
            FillPcm16(buffer, frameCount, format.Channels);
        }
    }

    private unsafe void FillFloat32(IntPtr buffer, int frameCount, int channels)
    {
        var output = (float*)buffer;
        for (var frame = 0; frame < frameCount; frame++)
        {
            var sample = NextSample();
            for (var channel = 0; channel < channels; channel++)
            {
                *output++ = SampleForChannel(sample, channel, channels);
            }
        }
    }

    private unsafe void FillPcm16(IntPtr buffer, int frameCount, int channels)
    {
        var output = (short*)buffer;
        for (var frame = 0; frame < frameCount; frame++)
        {
            var sample = NextSample();
            for (var channel = 0; channel < channels; channel++)
            {
                *output++ = (short)Math.Clamp(SampleForChannel(sample, channel, channels) * short.MaxValue, short.MinValue, short.MaxValue);
            }
        }
    }

    private unsafe void FillSilence(IntPtr buffer, int frameCount, WasapiFormat format)
    {
        var bytes = frameCount * format.BlockAlign;
        new Span<byte>((void*)buffer, bytes).Clear();
    }

    private static float SampleForChannel(StereoSample sample, int channel, int channelCount)
    {
        if (channelCount == 1)
        {
            return (sample.Left + sample.Right) * 0.5f;
        }

        return channel switch
        {
            0 => sample.Left,
            1 => sample.Right,
            _ => (sample.Left + sample.Right) * 0.5f
        };
    }

    private StereoSample NextSample()
    {
        var left = 0.0f;
        var right = 0.0f;
        lock (sync)
        {
            for (var index = voices.Count - 1; index >= 0; index--)
            {
                var voice = voices[index];
                if (voice.Position >= voice.Samples.Length)
                {
                    voices.RemoveAt(index);
                    continue;
                }

                var sample = voice.Samples[voice.Position++];
                left += sample * voice.LeftGain;
                right += sample * voice.RightGain;
            }
        }

        return new StereoSample(Math.Clamp(left, -1.0f, 1.0f), Math.Clamp(right, -1.0f, 1.0f));
    }

    private static float[] Resample(ReadOnlySpan<float> source, int sourceSampleRate, int targetSampleRate)
    {
        if (source.Length == 0 || sourceSampleRate <= 0 || targetSampleRate <= 0)
        {
            return [];
        }

        var targetLength = Math.Max(1, (int)MathF.Ceiling(source.Length * targetSampleRate / (float)sourceSampleRate));
        var target = new float[targetLength];
        var ratio = sourceSampleRate / (float)targetSampleRate;
        for (var index = 0; index < target.Length; index++)
        {
            var sourcePosition = index * ratio;
            var left = Math.Clamp((int)sourcePosition, 0, source.Length - 1);
            var right = Math.Min(left + 1, source.Length - 1);
            var t = sourcePosition - left;
            target[index] = source[left] + (source[right] - source[left]) * t;
        }

        return target;
    }

    private readonly record struct StereoSample(float Left, float Right);

    private sealed class Voice(float[] samples, float leftGain, float rightGain)
    {
        public readonly float[] Samples = samples;
        public readonly float LeftGain = leftGain;
        public readonly float RightGain = rightGain;
        public int Position;
    }

    private readonly record struct WasapiFormat(int Channels, int SampleRate, int BitsPerSample, int BlockAlign, WasapiSampleKind SampleKind)
    {
        public static WasapiFormat FromPointer(IntPtr pointer)
        {
            var tag = Marshal.ReadInt16(pointer, 0);
            var channels = Marshal.ReadInt16(pointer, 2);
            var sampleRate = Marshal.ReadInt32(pointer, 4);
            var blockAlign = Marshal.ReadInt16(pointer, 12);
            var bitsPerSample = Marshal.ReadInt16(pointer, 14);
            var kind = tag switch
            {
                WaveFormatIeeeFloat => WasapiSampleKind.Float32,
                WaveFormatPcm => WasapiSampleKind.Pcm16,
                WaveFormatExtensible when bitsPerSample == 32 => WasapiSampleKind.Float32,
                _ => WasapiSampleKind.Pcm16
            };
            return new WasapiFormat(channels, sampleRate, bitsPerSample, blockAlign, kind);
        }
    }

    private enum WasapiSampleKind
    {
        Float32,
        Pcm16
    }

    private const short WaveFormatPcm = 1;
    private const short WaveFormatIeeeFloat = 3;
    private const short WaveFormatExtensible = -2;

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr pvReserved, CoInit dwCoInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    [Flags]
    private enum CoInit : uint
    {
        Multithreaded = 0x0
    }

    [Flags]
    private enum ClsCtx : uint
    {
        All = 23
    }

    private enum EDataFlow
    {
        Render,
        Capture,
        All
    }

    private enum ERole
    {
        Console,
        Multimedia,
        Communications
    }

    private enum AudioClientShareMode
    {
        Shared,
        Exclusive
    }

    [Flags]
    private enum AudioClientStreamFlags : uint
    {
        NoPersist = 0x00080000
    }

    private static readonly Guid MMDeviceEnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IntPtr devices);

        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, ClsCtx clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfaceObject);
    }

    [ComImport]
    [Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        int Initialize(AudioClientShareMode shareMode, AudioClientStreamFlags streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr audioSessionGuid);

        int GetBufferSize(out int bufferFrameCount);

        int GetStreamLatency(out long latency);

        int GetCurrentPadding(out int paddingFrameCount);

        int IsFormatSupported(AudioClientShareMode shareMode, IntPtr format, out IntPtr closestMatch);

        int GetMixFormat(out IntPtr deviceFormat);

        int GetDevicePeriod(out long defaultDevicePeriod, out long minimumDevicePeriod);

        int Start();

        int Stop();

        int Reset();

        int SetEventHandle(IntPtr eventHandle);

        int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object serviceInterface);
    }

    [ComImport]
    [Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioRenderClient
    {
        int GetBuffer(int numFramesRequested, out IntPtr dataBuffer);

        int ReleaseBuffer(int numFramesWritten, uint flags);
    }
}
