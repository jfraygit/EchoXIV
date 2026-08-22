using System;
using NAudio.Wave;

namespace EchoMix.AudioHost.Audio.ExternalInput;

/// Common shape for anything External Input Mode's primary slot can capture from - a Windows recording device
/// (ExternalInputCapture) or a specific application's own audio output (Spotify.ProcessLoopbackCapture, the
/// same class Spotify Mode already uses) - so MixerEngine's downstream Trim/Eq/Filter/Gain chain (see
/// MixerEngine.BuildExternalInputChain) doesn't need to care which one it's actually reading from.
public interface IExternalAudioSource : IDisposable
{
    WaveFormat WaveFormat { get; }
    BufferedWaveProvider WaveProvider { get; }

    /// True while this source should be treated as live - false once it's genuinely gone (device unplugged,
    /// target application closed) and callers should stop relying on it.
    bool IsAlive { get; }

    /// What to show the DJ - a device's friendly name, or the captured application's display name.
    string SourceName { get; }
}
