namespace EchoMix.Shared;

/// The local named-pipe name is scoped per FFXIV client process (its own process ID), not a single fixed name
/// - without this, two game clients running on the same PC (a common way to dual-box) would both find the
/// first one's pipe already listening and silently share a single AudioHost instance instead of each getting
/// their own, which breaks anything that needs two independent roles at once (e.g.
public static class PipeNaming
{
    public static string ForProcess(int processId) => $"EchoMixAudioHost_{processId}";
}
