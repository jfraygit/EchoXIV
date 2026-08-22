namespace EchoSim.Game;

/// Which relay this build talks to.
public static class RelayEndpoints
{
    /// The public board.
    public const string Live = "https://echoxiv.com/api/echosim";

    /// The sandbox.
    public const string Dev = "https://echoxiv.com/api/echosim-dev";

#if DEBUG
    /// Whether this build is allowed to switch relays at all.
    public const bool CanChoose = true;

    private const string Default = Dev;
#else
    public const bool CanChoose = false;

    private const string Default = Live;
#endif

    /// The relay every client in this build talks to.
    public static string BaseUrl { get; private set; } = Default;

    /// Whether BaseUrl is currently the live board.
    public static bool UsingLive => BaseUrl == Live;

    /// Points this session at one relay or the other.
    public static void Choose(bool live)
    {
#if DEBUG
        BaseUrl = live ? Live : Dev;
#else
        _ = live;
#endif
    }
}
