using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace EchoMix.AudioHost.Audio;

/// Plays one-shot sound-effect pads mixed additively on top of whatever's already playing - independent of
/// either deck's crossfader position or per-deck EQ/filter/gain, but still affected by master volume, so
/// lowering that also lowers pads.
public sealed class SoundEffectMixer : ISampleProvider
{
    private readonly MixingSampleProvider mixer;

    public WaveFormat WaveFormat { get; }

    public SoundEffectMixer(ISampleProvider baseSource)
    {
        WaveFormat = baseSource.WaveFormat;
        mixer = new MixingSampleProvider(WaveFormat) { ReadFully = true };
        mixer.AddMixerInput(baseSource);
    }

    public void Play(string filePath, float volume = 1f)
    {
        var reader = new AudioFileReader(filePath) { Volume = System.Math.Clamp(volume, 0f, 2f) };
        ISampleProvider src = reader;
        if (src.WaveFormat.Channels == 1)
            src = new MonoToStereoSampleProvider(src);
        if (src.WaveFormat.SampleRate != WaveFormat.SampleRate)
            src = new WdlResamplingSampleProvider(src, WaveFormat.SampleRate);

        var toDispose = src;
        void OnEnded(object? sender, SampleProviderEventArgs e)
        {
            if (e.SampleProvider != toDispose)
                return;
            reader.Dispose();
            mixer.MixerInputEnded -= OnEnded;
        }

        mixer.MixerInputEnded += OnEnded;
        mixer.AddMixerInput(src);
    }

    public int Read(float[] buffer, int offset, int count) => mixer.Read(buffer, offset, count);
}
