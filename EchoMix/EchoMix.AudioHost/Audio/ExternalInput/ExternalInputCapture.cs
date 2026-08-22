using System;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace EchoMix.AudioHost.Audio.ExternalInput;

/// Captures audio from a DJ-selected Windows recording (input) device - a physical hardware mixer/audio
/// interface plugged into their PC, or a virtual audio cable mirroring another app's output - so External
/// Input Mode can broadcast it the same way Spotify Mode broadcasts Spotify's own captured audio.
public sealed class ExternalInputCapture : IExternalAudioSource
{
    private const int MaxRecoveryAttempts = 5;
    private static readonly TimeSpan[] RecoveryDelays =
    {
        TimeSpan.Zero, TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(300),
        TimeSpan.FromMilliseconds(600), TimeSpan.FromMilliseconds(1200),
    };

    private readonly MMDevice device;
    private readonly object captureGate = new();
    private readonly ManualResetEventSlim stoppedSignal = new(false);
    private WasapiCapture capture;
    private volatile bool running;
    private volatile bool recovering;
    private volatile bool disposed;

    public string DeviceName { get; }

    /// IExternalAudioSource's generic display name - just DeviceName under another name, so MixerEngine's
    /// primary-slot handling can treat this and a process capture identically.
    public string SourceName => DeviceName;

    /// Fixed at construction from the device's format at that moment - a recovery restart reuses this same
    /// value rather than re-reading a possibly-changed one, since WaveProvider below is permanently bound to
    /// it and everything MixerEngine already built downstream of WaveProvider.ToSampleProvider() assumes it
    /// never changes mid-capture.
    public WaveFormat WaveFormat { get; }

    /// False once capture has stopped and every recovery attempt has also failed - most commonly the device
    /// being genuinely unplugged or disabled, as opposed to the brief mid-capture hiccups TryRecoverAsync
    /// exists to absorb.
    public bool IsRunning => running;

    /// True the whole time TryRecoverAsync is between attempts (delay or in-flight device open) - `running`
    /// alone flips false the instant a drop happens, well before recovery has had a chance, and MixerEngine's
    /// status loop polls on a ~33ms cadence.
    public bool IsAlive => running || recovering;

    public BufferedWaveProvider WaveProvider { get; }

    /// Takes ownership of `device` - disposed alongside the capture itself rather than left for the caller,
    /// since AudioInputDevices.FindById hands over a fresh MMDevice specifically for this capture's use.
    public ExternalInputCapture(MMDevice device)
    {
        this.device = device;
        DeviceName = device.FriendlyName;
        capture = new WasapiCapture(device);
        WaveFormat = capture.WaveFormat;

        WaveProvider = new BufferedWaveProvider(WaveFormat)
        {
            BufferDuration = TimeSpan.FromSeconds(1),
            DiscardOnBufferOverflow = true,
        };

        AttachHandlers(capture);
    }

    private void AttachHandlers(WasapiCapture target)
    {
        target.DataAvailable += (_, e) =>
        {
            if (running && e.BytesRecorded > 0 && target.WaveFormat.Equals(WaveFormat))
                WaveProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);
        };
        target.RecordingStopped += (_, e) => OnRecordingStopped(target, e.Exception);
    }

    public void Start()
    {
        running = true;
        capture.StartRecording();
    }

    /// Only reacts if `stopped` is still the CURRENT capture instance - a stale event from a capture object
    /// already replaced (or torn down) by a previous recovery/Dispose must not kick off a second, redundant
    /// recovery race.
    private void OnRecordingStopped(WasapiCapture stopped, Exception? error)
    {
        bool isCurrent;
        lock (captureGate)
            isCurrent = ReferenceEquals(stopped, capture);
        if (!isCurrent)
            return;

        var wasRunning = running;
        running = false;

        if (disposed)
        {
            stoppedSignal.Set();
            return;
        }

        if (wasRunning)
        {
            recovering = true;
            _ = TryRecoverAsync(error);
        }
    }

    private async Task TryRecoverAsync(Exception? firstError)
    {
        try
        {
            for (var attempt = 0; attempt < MaxRecoveryAttempts && !disposed; attempt++)
            {
                var delay = RecoveryDelays[Math.Min(attempt, RecoveryDelays.Length - 1)];
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay);

                lock (captureGate)
                {
                    if (disposed)
                        return;

                    try
                    {
                        var fresh = new WasapiCapture(device);
                        AttachHandlers(fresh);
                        fresh.StartRecording();

                        var old = capture;
                        capture = fresh;
                        try { old.Dispose(); } catch { }

                        running = true;
                        return;
                    }
                    catch
                    {
                    }
                }
            }

        }
        finally
        {
            recovering = false;
        }
    }

    /// Every step guarded individually and independently - a Dispose must never throw (this one is called
    /// straight from IpcServer's message handler, which has no try/catch of its own around individual
    /// commands, so an exception here would otherwise take down the whole IPC connection to the plugin, not
    /// just this capture).
    public void Dispose()
    {
        disposed = true;
        var wasRunning = running;
        running = false;

        WasapiCapture toStop;
        lock (captureGate)
            toStop = capture;

        if (wasRunning)
        {
            stoppedSignal.Reset();
            try { toStop.StopRecording(); } catch { }
            if (!stoppedSignal.Wait(TimeSpan.FromSeconds(2)))
                Console.WriteLine($"[EchoMix.AudioHost] {DeviceName}: capture thread didn't confirm it stopped within 2s - disposing anyway.");
        }

        lock (captureGate)
        {
            try { toStop.Dispose(); } catch { }
            try { device.Dispose(); } catch { }
        }

        stoppedSignal.Dispose();
    }
}
