using System.Runtime.InteropServices;

namespace Prism.Audio;

/// <summary>Minimal Windows Core Audio (WASAPI) COM definitions used for capture.</summary>
internal static class CoreAudioInterop
{
    public const int ERender = 0;
    public const int ECapture = 1;
    public const int EConsole = 0;
    public const int ClsCtxAll = 23;
    public const int ShareModeShared = 0;
    public const int StreamFlagsLoopback = 0x00020000;
    public const uint BufferFlagsSilent = 0x2;
    public const int AudioClientBufferEmpty = 0x08890001;
    public const ushort WaveFormatPcm = 1;
    public const ushort WaveFormatIeeeFloat = 3;
    public const ushort WaveFormatExtensible = 0xFFFE;

    public static readonly Guid IidAudioClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
    public static readonly Guid IidAudioCaptureClient = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
    public static readonly Guid SubTypeIeeeFloat = new("00000003-0000-0010-8000-00aa00389b71");
    public static readonly Guid SubTypePcm = new("00000001-0000-0010-8000-00aa00389b71");

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    public class MMDeviceEnumerator
    {
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr audioSessionGuid);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint padding);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr eventHandle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }

    /// <summary>The parts of WAVEFORMATEX(TENSIBLE) needed to decode samples.</summary>
    public readonly record struct SampleFormat(int Channels, int SampleRate, int BitsPerSample, bool IsFloat)
    {
        public int BlockAlign => Channels * BitsPerSample / 8;

        public bool IsSupported => (IsFloat && BitsPerSample == 32) || (!IsFloat && BitsPerSample is 16 or 32);

        public static SampleFormat Read(IntPtr waveFormat)
        {
            ushort tag = (ushort)Marshal.ReadInt16(waveFormat, 0);
            int channels = Marshal.ReadInt16(waveFormat, 2);
            int sampleRate = Marshal.ReadInt32(waveFormat, 4);
            int bits = Marshal.ReadInt16(waveFormat, 14);
            bool isFloat = tag == WaveFormatIeeeFloat;
            if (tag == WaveFormatExtensible)
            {
                var subType = Marshal.PtrToStructure<Guid>(waveFormat + 24);
                isFloat = subType == SubTypeIeeeFloat;
            }
            return new SampleFormat(channels, sampleRate, bits, isFloat);
        }
    }
}
