using System.Runtime.InteropServices;
using static Prism.Audio.CoreAudioInterop;

namespace Prism.Audio;

internal enum AudioSource { Off, Speakers, Microphone }

/// <summary>
/// Captures the default speaker output (loopback) or microphone on a background thread
/// and keeps the most recent mono samples in a ring buffer.
/// </summary>
internal sealed class AudioCapture : IDisposable
{
    private const int RingSize = 8192;
    private const int StaleAfterMs = 250;
    private const int DeviceCheckIntervalMs = 2000;

    private readonly float[] _ring = new float[RingSize];
    private readonly object _lock = new();
    private byte[] _scratch = [];
    private int _writeIndex;
    private long _lastDataTick;
    private Thread? _thread;
    private volatile bool _running;

    public int SampleRate { get; private set; } = 48000;

    public void Start(AudioSource source)
    {
        Stop();
        if (source == AudioSource.Off)
        {
            return;
        }
        _running = true;
        _thread = new Thread(() => Run(source)) { IsBackground = true, Name = "Prism audio capture" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(1000);
        _thread = null;
        Interlocked.Exchange(ref _lastDataTick, 0);
    }

    /// <summary>Copies the newest samples (oldest first). Returns false when no audio arrived recently.</summary>
    public bool TryGetLatest(float[] destination)
    {
        if (Environment.TickCount64 - Interlocked.Read(ref _lastDataTick) > StaleAfterMs)
        {
            return false;
        }
        lock (_lock)
        {
            int start = (_writeIndex - destination.Length + RingSize) % RingSize;
            for (int i = 0; i < destination.Length; i++)
            {
                destination[i] = _ring[(start + i) % RingSize];
            }
        }
        return true;
    }

    private void Run(AudioSource source)
    {
        while (_running)
        {
            try
            {
                CaptureUntilDeviceChanges(source == AudioSource.Speakers ? ERender : ECapture);
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException)
            {
                // No device, device removed, or microphone access blocked: retry shortly.
            }
            for (int i = 0; i < 20 && _running; i++)
            {
                Thread.Sleep(100);
            }
        }
    }

    private void CaptureUntilDeviceChanges(int dataFlow)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        IMMDevice? device = null;
        IAudioClient? client = null;
        IAudioCaptureClient? capture = null;
        try
        {
            Check(enumerator.GetDefaultAudioEndpoint(dataFlow, EConsole, out device));
            Check(device.GetId(out string deviceId));
            var clientIid = IidAudioClient;
            Check(device.Activate(ref clientIid, ClsCtxAll, IntPtr.Zero, out object clientObject));
            client = (IAudioClient)clientObject;

            Check(client.GetMixFormat(out IntPtr formatPointer));
            SampleFormat format;
            try
            {
                format = SampleFormat.Read(formatPointer);
                if (!format.IsSupported)
                {
                    return;
                }
                int flags = dataFlow == ERender ? StreamFlagsLoopback : 0;
                Check(client.Initialize(ShareModeShared, flags, 2_000_000, 0, formatPointer, IntPtr.Zero));
            }
            finally
            {
                Marshal.FreeCoTaskMem(formatPointer);
            }
            SampleRate = format.SampleRate;

            var captureIid = IidAudioCaptureClient;
            Check(client.GetService(ref captureIid, out object captureObject));
            capture = (IAudioCaptureClient)captureObject;
            Check(client.Start());

            long nextDeviceCheck = Environment.TickCount64 + DeviceCheckIntervalMs;
            while (_running)
            {
                Thread.Sleep(10);
                Check(capture.GetNextPacketSize(out uint packetFrames));
                while (packetFrames > 0)
                {
                    Check(capture.GetBuffer(out IntPtr data, out uint frames, out uint bufferFlags, out _, out _));
                    Append(data, (int)frames, format, (bufferFlags & BufferFlagsSilent) != 0);
                    Check(capture.ReleaseBuffer(frames));
                    Check(capture.GetNextPacketSize(out packetFrames));
                }

                if (Environment.TickCount64 >= nextDeviceCheck)
                {
                    nextDeviceCheck = Environment.TickCount64 + DeviceCheckIntervalMs;
                    if (DefaultDeviceChanged(enumerator, dataFlow, deviceId))
                    {
                        return;
                    }
                }
            }
        }
        finally
        {
            client?.Stop();
            Release(capture);
            Release(client);
            Release(device);
            Release(enumerator);
        }
    }

    private void Append(IntPtr data, int frames, SampleFormat format, bool silent)
    {
        int byteCount = frames * format.BlockAlign;
        if (_scratch.Length < byteCount)
        {
            _scratch = new byte[byteCount];
        }
        if (!silent)
        {
            Marshal.Copy(data, _scratch, 0, byteCount);
        }

        var bytes = _scratch.AsSpan(0, byteCount);
        var floats = MemoryMarshal.Cast<byte, float>(bytes);
        var shorts = MemoryMarshal.Cast<byte, short>(bytes);
        var ints = MemoryMarshal.Cast<byte, int>(bytes);
        int channels = format.Channels;

        lock (_lock)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                float mono = 0;
                if (!silent)
                {
                    for (int channel = 0; channel < channels; channel++)
                    {
                        int index = frame * channels + channel;
                        mono += format.IsFloat ? floats[index]
                            : format.BitsPerSample == 16 ? shorts[index] / 32768f
                            : ints[index] / 2147483648f;
                    }
                    mono /= channels;
                }
                _ring[_writeIndex] = mono;
                _writeIndex = (_writeIndex + 1) % RingSize;
            }
        }
        Interlocked.Exchange(ref _lastDataTick, Environment.TickCount64);
    }

    private static bool DefaultDeviceChanged(IMMDeviceEnumerator enumerator, int dataFlow, string deviceId)
    {
        if (enumerator.GetDefaultAudioEndpoint(dataFlow, EConsole, out var current) < 0)
        {
            return true;
        }
        bool changed = current.GetId(out string currentId) < 0 || currentId != deviceId;
        Release(current);
        return changed;
    }

    private static void Check(int hresult)
    {
        if (hresult < 0)
        {
            Marshal.ThrowExceptionForHR(hresult);
        }
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.ReleaseComObject(comObject);
        }
    }

    public void Dispose() => Stop();
}
