using System.Collections.Generic;
using Dalamud.Interface;
using EchoMix.Plugin.UI.State;

namespace EchoMix.Plugin.UI.Shell;

/// The 2.0 navigation destinations, and the single table mapping them onto 1.0's view modes.
public enum ShellDestination
{
    Mix,
    Library,
    Broadcast,
    Listen,
    Browse,
    Settings,
}

public static class ShellRoutes
{
    /// 1.0's Settings sub-tab order, from DjDeckWindow's own tab strip: General, Library, Broadcast, Listen,
    /// Spotify, Changelog.
    public const int LegacyTabGeneral = 0;
    public const int LegacyTabLibrary = 1;
    public const int LegacyTabBroadcast = 2;
    public const int LegacyTabListen = 3;
    public const int LegacyTabSpotify = 4;
    public const int LegacyTabChangelog = 5;

    public readonly record struct RailItem(
        ShellDestination Destination,
        string Label,
        FontAwesomeIcon Icon,
        bool DjOnly);

    /// The rail, in order.
    public static readonly RailItem[] Rail =
    {
        new(ShellDestination.Mix, "Mixer", FontAwesomeIcon.SlidersH, DjOnly: true),
        new(ShellDestination.Library, "Library", FontAwesomeIcon.Music, DjOnly: true),
        new(ShellDestination.Broadcast, "Broadcast", FontAwesomeIcon.BroadcastTower, DjOnly: true),
        new(ShellDestination.Listen, "Listen", FontAwesomeIcon.Headphones, DjOnly: false),
        new(ShellDestination.Browse, "Browse", FontAwesomeIcon.Compass, DjOnly: false),
        new(ShellDestination.Settings, "Settings", FontAwesomeIcon.Cog, DjOnly: false),
    };

    /// Which 1.0 body renders for a destination that hasn't been redesigned yet, and which Settings sub-tab
    /// to force when it lands on the Settings view.
    public static (EchoMixView View, int? SettingsTab) LegacyBodyFor(ShellDestination destination, bool isListening)
        => destination switch
        {
            ShellDestination.Mix => (EchoMixView.Deck, null),
            ShellDestination.Library => (EchoMixView.Settings, LegacyTabLibrary),
            ShellDestination.Broadcast => (EchoMixView.Settings, LegacyTabBroadcast),
            ShellDestination.Listen => isListening
                ? (EchoMixView.Listener, null)
                : (EchoMixView.JoinShow, null),
            ShellDestination.Browse => (EchoMixView.BrowseShows, null),
            ShellDestination.Settings => (EchoMixView.Settings, LegacyTabGeneral),
            _ => (EchoMixView.Deck, null),
        };

    /// Routes a 1.0 view back to the destination that now contains it, so shared code which navigates by view
    /// (the relay response pump sets pendingView when a profile save resolves, for instance) moves the shell
    /// too rather than silently desyncing the two looks.
    public static ShellDestination DestinationFor(EchoMixView view)
        => view switch
        {
            EchoMixView.Deck => ShellDestination.Mix,
            EchoMixView.Settings => ShellDestination.Settings,
            EchoMixView.Listener => ShellDestination.Listen,
            EchoMixView.JoinShow => ShellDestination.Listen,
            EchoMixView.Welcome => ShellDestination.Listen,
            EchoMixView.BrowseShows => ShellDestination.Browse,
            EchoMixView.DjList => ShellDestination.Browse,
            EchoMixView.DjProfile => ShellDestination.Browse,
            EchoMixView.DjProfileEdit => ShellDestination.Browse,
            _ => ShellDestination.Mix,
        };

    /// Destinations whose legacy body still wants the full window width, so the rail collapses to a thin edge
    /// affordance for them rather than squeezing a layout that was hand-tuned for 1072px.
    public static readonly HashSet<ShellDestination> LegacyWantsFullWidth = new();
}
