namespace EchoMix.Plugin.Ipc;

/// How the audio engine is doing, as reported to the user.
public enum AudioHostHealth
{
    /// Connected and pushing status.
    Connected,

    /// The pipe is open but nothing has arrived for a while.
    NotResponding,

    /// No connection.
    Disconnected,
}
