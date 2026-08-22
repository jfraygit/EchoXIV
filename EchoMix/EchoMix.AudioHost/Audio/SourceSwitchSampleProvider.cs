using NAudio.Wave;

namespace EchoMix.AudioHost.Audio;

/// Reads from either the normal deck mix (primary) or an alternate source (secondary, e.g. Spotify Mode's
/// captured audio) - never both.
public sealed class SourceSwitchSampleProvider : ISampleProvider
{
    private readonly ISampleProvider primary;
    private volatile ISampleProvider? secondary;
    private volatile bool useSecondary;

    public SourceSwitchSampleProvider(ISampleProvider primary)
    {
        this.primary = primary;
        WaveFormat = primary.WaveFormat;
    }

    public WaveFormat WaveFormat { get; }

    /// Set this before flipping UseSecondary on; clear it (null) after flipping back off so nothing keeps
    /// holding a reference to a torn-down capture source.
    public ISampleProvider? Secondary
    {
        get => secondary;
        set => secondary = value;
    }

    /// Silently stays on the primary if no secondary has been set - guards against a caller flipping this on
    /// before Secondary is assigned.
    public bool UseSecondary
    {
        get => useSecondary;
        set => useSecondary = value;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var active = useSecondary ? secondary : null;
        return (active ?? primary).Read(buffer, offset, count);
    }
}
