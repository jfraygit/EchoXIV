using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace EchoMix.AudioHost.Audio.Spotify;

/// Captures only one process's (and its child processes') audio output, via the Windows 10 2004+ "process
/// loopback" WASAPI extension - as opposed to NAudio's regular WasapiLoopbackCapture, which taps an entire
/// output device and would pick up game sound, voice chat, notification dings, everything else playing on the
/// system.
public sealed class ProcessLoopbackCapture : ExternalInput.IExternalAudioSource
{
    private const int SampleRate = 44100;
    private const int Channels = 2;
    private const int BitsPerSample = 32;
    private const int BlockAlign = Channels * BitsPerSample / 8;

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);

    private readonly int targetProcessId;
    private IAudioClient? audioClient;
    private IAudioCaptureClient? captureClient;
    private IntPtr formatPtr;
    private Thread? captureThread;
    private volatile bool running;

    public BufferedWaveProvider WaveProvider { get; } = new(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels))
    {
        BufferDuration = TimeSpan.FromSeconds(1),
        DiscardOnBufferOverflow = true,
    };

    private const float NearFullThreshold = 0.9f;
    private static readonly TimeSpan OverflowRecoveryWindow = TimeSpan.FromSeconds(2);
    private DateTime? nearFullSinceUtc;

    private static readonly TimeSpan DiscardLogInterval = TimeSpan.FromSeconds(30);
    private DateTime discardLogWindowStartUtc = DateTime.UtcNow;
    private int packetsDiscardedInWindow;
    private int packetsProcessedInWindow;

    /// False once the capture loop has stopped for any reason other than Dispose being called - most commonly
    /// the target process (Spotify) exiting mid-capture.
    public bool IsRunning => running;

    /// IExternalAudioSource's generic liveness check.
    public bool IsAlive => IsRunning;

    /// IExternalAudioSource's display name - "Spotify" for Spotify Mode, or whatever display name the caller
    /// picked for an External Input Mode application capture.
    public string SourceName { get; }

    public ProcessLoopbackCapture(int targetProcessId, string sourceName)
    {
        this.targetProcessId = targetProcessId;
        SourceName = sourceName;
    }

    /// Finds the root of a target process's own process tree (some apps, like Spotify's Chromium-based
    /// desktop client, spawn several helper processes) so INCLUDE_TARGET_PROCESS_TREE below actually covers
    /// every child that might be the one rendering audio, not just whichever one happened to match by name
    /// first.
    public static bool TryFindProcessId(string processName, out int processId, out string? error)
    {
        Process[] candidates;
        try
        {
            candidates = Process.GetProcessesByName(processName);
        }
        catch
        {
            candidates = Array.Empty<Process>();
        }

        if (candidates.Length == 0)
        {
            processId = 0;
            error = $"{processName} isn't running - launch it and try again.";
            return false;
        }

        try
        {
            var parentById = GetParentProcessIds(candidates.Select(p => p.Id));
            var candidateIds = candidates.Select(p => p.Id).ToHashSet();

            foreach (var candidate in candidates)
            {
                if (!parentById.TryGetValue(candidate.Id, out var parentId) || !candidateIds.Contains(parentId))
                {
                    processId = candidate.Id;
                    error = null;
                    return true;
                }
            }
        }
        catch
        {
        }

        processId = candidates.Min(p => p.Id);
        error = null;
        return true;
    }

    public async Task<(bool Ok, string? Error)> StartAsync()
    {
        try
        {
            var activationParams = new AUDIOCLIENT_ACTIVATION_PARAMS
            {
                ActivationType = AUDIOCLIENT_ACTIVATION_TYPE.AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK,
                ProcessLoopbackParams = new AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS
                {
                    TargetProcessId = (uint)targetProcessId,
                    ProcessLoopbackMode = PROCESS_LOOPBACK_MODE.PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE,
                },
            };

            var activationParamsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<AUDIOCLIENT_ACTIVATION_PARAMS>());
            try
            {
                Marshal.StructureToPtr(activationParams, activationParamsPtr, false);

                var propvariant = new PROPVARIANT_BLOB
                {
                    vt = VT_BLOB,
                    cbSize = (uint)Marshal.SizeOf<AUDIOCLIENT_ACTIVATION_PARAMS>(),
                    pBlobData = activationParamsPtr,
                };
                var propvariantPtr = Marshal.AllocHGlobal(Marshal.SizeOf<PROPVARIANT_BLOB>());
                try
                {
                    Marshal.StructureToPtr(propvariant, propvariantPtr, false);

                    var handler = new ActivationCompletionHandler();
                    var riid = typeof(IAudioClient).GUID;
                    ActivateAudioInterfaceAsync(VirtualAudioDeviceProcessLoopback, ref riid, propvariantPtr, handler, out _);

                    var (hr, iface) = await handler.Task.ConfigureAwait(false);
                    if (hr != 0 || iface == null)
                        return (false, $"Windows denied audio capture for this process (0x{hr:X8}).");

                    audioClient = (IAudioClient)iface;
                }
                finally
                {
                    Marshal.FreeHGlobal(propvariantPtr);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(activationParamsPtr);
            }

            var format = new WAVEFORMATEX
            {
                wFormatTag = WAVE_FORMAT_IEEE_FLOAT,
                nChannels = Channels,
                nSamplesPerSec = SampleRate,
                wBitsPerSample = BitsPerSample,
                nBlockAlign = BlockAlign,
                nAvgBytesPerSec = SampleRate * BlockAlign,
                cbSize = 0,
            };
            formatPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WAVEFORMATEX>());
            Marshal.StructureToPtr(format, formatPtr, false);

            audioClient.Initialize(AUDCLNT_SHAREMODE_SHARED, AUDCLNT_STREAMFLAGS_LOOPBACK, 2_000_000, 0, formatPtr, IntPtr.Zero);

            var captureGuid = typeof(IAudioCaptureClient).GUID;
            audioClient.GetService(ref captureGuid, out var captureObj);
            captureClient = (IAudioCaptureClient)captureObj;

            audioClient.Start();

            running = true;
            captureThread = new Thread(CaptureLoop) { IsBackground = true, Name = $"EchoMix-ProcessCapture-{SourceName}" };
            captureThread.Start();

            return (true, null);
        }
        catch (DllNotFoundException)
        {
            return (false, "This needs Windows 10 (2004) or later.");
        }
        catch (EntryPointNotFoundException)
        {
            return (false, "This needs Windows 10 (2004) or later.");
        }
        catch (Exception ex)
        {
            return (false, $"Couldn't start capturing {SourceName}'s audio: {ex.Message}");
        }
    }

    private void CaptureLoop()
    {
        var buffer = new byte[BlockAlign * SampleRate];        while (running)
        {
            try
            {
                captureClient!.GetNextPacketSize(out var framesAvailable);
                if (framesAvailable == 0)
                {
                    Thread.Sleep(5);
                    continue;
                }

                captureClient.GetBuffer(out var dataPtr, out var numFrames, out var flags, out _, out _);
                var byteCount = checked((int)numFrames * BlockAlign);
                if (buffer.Length < byteCount)
                    buffer = new byte[byteCount];

                if ((flags & AUDCLNT_BUFFERFLAGS_SILENT) != 0)
                    Array.Clear(buffer, 0, byteCount);
                else
                    Marshal.Copy(dataPtr, buffer, 0, byteCount);

                captureClient.ReleaseBuffer(numFrames);

                packetsProcessedInWindow++;
                if (WaveProvider.BufferedBytes + byteCount > WaveProvider.BufferLength)
                    packetsDiscardedInWindow++;

                WaveProvider.AddSamples(buffer, 0, byteCount);
                CheckBufferHealth();
                LogDiscardsIfDue();
            }
            catch
            {
                running = false;
            }
        }
    }

    /// Resets WaveProvider once it's been sitting chronically near-full for longer than
    /// OverflowRecoveryWindow - see the field's own doc comment.
    private void CheckBufferHealth()
    {
        var nearFullCeiling = TimeSpan.FromTicks((long)(WaveProvider.BufferDuration.Ticks * NearFullThreshold));
        if (WaveProvider.BufferedDuration < nearFullCeiling)
        {
            nearFullSinceUtc = null;
            return;
        }

        nearFullSinceUtc ??= DateTime.UtcNow;
        if (DateTime.UtcNow - nearFullSinceUtc.Value < OverflowRecoveryWindow)
            return;

        Console.WriteLine($"[EchoMix.AudioHost] {SourceName}'s capture buffer looked stuck (chronic overflow) - resetting it.");
        WaveProvider.ClearBuffer();
        nearFullSinceUtc = null;
    }

    /// See the discard-tracking fields' own doc comment - this is the milder, otherwise invisible sibling of
    /// CheckBufferHealth's chronic-overflow reset above.
    private void LogDiscardsIfDue()
    {
        var now = DateTime.UtcNow;
        if (now - discardLogWindowStartUtc < DiscardLogInterval)
            return;

        if (packetsDiscardedInWindow > 0)
        {
            Console.WriteLine($"[EchoMix.AudioHost] {SourceName}'s capture buffer discarded {packetsDiscardedInWindow} of {packetsProcessedInWindow} incoming audio packet(s) in the last ~{DiscardLogInterval.TotalSeconds:0}s (buffer running full - producer/consumer clock drift).");
        }

        discardLogWindowStartUtc = now;
        packetsDiscardedInWindow = 0;
        packetsProcessedInWindow = 0;
    }

    public void Dispose()
    {
        running = false;

        captureThread?.Join();

        try { audioClient?.Stop(); } catch { }

        if (captureClient != null)
            Marshal.ReleaseComObject(captureClient);
        if (audioClient != null)
            Marshal.ReleaseComObject(audioClient);

        if (formatPtr != IntPtr.Zero)
            Marshal.FreeHGlobal(formatPtr);
    }


    private const string VirtualAudioDeviceProcessLoopback = "VAD\\Process_Loopback";
    private const ushort WAVE_FORMAT_IEEE_FLOAT = 3;
    private const int AUDCLNT_SHAREMODE_SHARED = 0;
    private const int AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
    private const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x2;
    private const ushort VT_BLOB = 65;

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        [In] ref Guid riid,
        IntPtr activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation activationOperation);

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    private enum AUDIOCLIENT_ACTIVATION_TYPE
    {
        AUDIOCLIENT_ACTIVATION_TYPE_DEFAULT = 0,
        AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK = 1,
    }

    private enum PROCESS_LOOPBACK_MODE
    {
        PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE = 0,
        PROCESS_LOOPBACK_MODE_EXCLUDE_TARGET_PROCESS_TREE = 1,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS
    {
        public uint TargetProcessId;
        public PROCESS_LOOPBACK_MODE ProcessLoopbackMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AUDIOCLIENT_ACTIVATION_PARAMS
    {
        public AUDIOCLIENT_ACTIVATION_TYPE ActivationType;
        public AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS ProcessLoopbackParams;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPVARIANT_BLOB
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public uint cbSize;
        public uint padding;
        public IntPtr pBlobData;
    }

    [ComImport]
    [Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceCompletionHandler
    {
        [PreserveSig]
        int ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
    }

    [ComImport]
    [Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceAsyncOperation
    {
        void GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
    }

    [ComImport]
    [Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        void Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr audioSessionGuid);
        void GetBufferSize(out uint numBufferFrames);
        void GetStreamLatency(out long latency);
        void GetCurrentPadding(out uint numPaddingFrames);
        void IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);
        void GetMixFormat(out IntPtr deviceFormat);
        void GetDevicePeriod(out long defaultDevicePeriod, out long minimumDevicePeriod);
        void Start();
        void Stop();
        void Reset();
        void SetEventHandle(IntPtr eventHandle);
        void GetService([In] ref Guid interfaceId, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport]
    [Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        void GetBuffer(out IntPtr dataBuffer, out uint numFramesToRead, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        void ReleaseBuffer(uint numFramesRead);
        void GetNextPacketSize(out uint numFramesInNextPacket);
    }

    private sealed class ActivationCompletionHandler : IActivateAudioInterfaceCompletionHandler
    {
        private readonly TaskCompletionSource<(int Hr, object? Iface)> tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<(int Hr, object? Iface)> Task => tcs.Task;

        public int ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation)
        {
            try
            {
                activateOperation.GetActivateResult(out var hr, out var iface);
                tcs.TrySetResult((hr, iface));
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }

            return 0;        }
    }


    private static Dictionary<int, int> GetParentProcessIds(IEnumerable<int> pids)
    {
        var result = new Dictionary<int, int>();
        var wanted = pids.ToHashSet();

        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
            return result;

        try
        {
            var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (Process32First(snapshot, ref entry))
            {
                do
                {
                    if (wanted.Contains((int)entry.th32ProcessID))
                        result[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID;
                } while (Process32Next(snapshot, ref entry));
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return result;
    }

    private const uint TH32CS_SNAPPROCESS = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll")]
    private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll")]
    private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
