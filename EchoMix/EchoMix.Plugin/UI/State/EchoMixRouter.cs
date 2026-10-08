namespace EchoMix.Plugin.UI.State;

/// Which screen the UI is currently showing.
public enum EchoMixView
{
    Deck,
    Settings,
    Listener,
    Welcome,
    JoinShow,
    BrowseShows,
    DjList,
    DjProfile,
    DjProfileEdit,
}

/// Navigation state, shared by both the 1.0 window (DjDeckWindow) and the 2.0 shell.
public sealed class EchoMixRouter
{
    public EchoMixView CurrentView { get; set; } = EchoMixView.Deck;

    /// Non-null while a fade-out is in flight.
    public EchoMixView? PendingView { get; set; }

    public float ContentAlpha { get; set; } = 1f;

    /// Where the cog returns to when Settings is toggled closed - Settings is reachable from both Deck and
    /// (while listening) Listener, so it can't be hardcoded.
    public EchoMixView LastNonSettingsView { get; set; } = EchoMixView.Deck;

    /// BrowseShows is reachable from two different places (a listener browsing from Join a Show, or a DJ
    /// checking their own listing from the Deck header), so its Back button can't hardcode one destination.
    public EchoMixView ViewBeforeBrowseShows { get; set; } = EchoMixView.Deck;

    /// DjProfileEdit's Back/Cancel target depends on how it was opened - DjList (via "Add Listing", a fresh
    /// profile with nothing to go back to) or DjProfile (via "Edit", so cancelling returns to viewing it
    /// rather than dropping to the whole grid).
    public EchoMixView ViewBeforeDjProfileEdit { get; set; } = EchoMixView.DjList;

    /// Collapses whichever of Deck/Listener is active down to a small draggable visualizer box.
    public bool IsMinimized { get; set; }

    /// Counts up from 0 the moment Welcome becomes the current view, driving the DJ/Listener card labels'
    /// one-shot per-letter reveal rather than it replaying every frame.
    public float WelcomeViewSeconds { get; set; }

    /// Session-scoped, deliberately NOT persisted (unlike Configuration.LastChosenRole): whether Welcome has
    /// already been shown/decided during *this* plugin load.
    public bool HasClearedWelcomeThisSession { get; set; }

    /// Edge-detects AudioHost's listen connection coming up/going down so the UI can switch itself into/out
    /// of the Listener view automatically - that view reflects "you are currently listening," it isn't
    /// somewhere you navigate to by hand.
    public bool WasListening { get; set; }


    /// Which rail destination the shell is showing.
    public Shell.ShellDestination Destination { get; set; } = Shell.ShellDestination.Mix;

    /// Whether the rail is pinned open.
    public bool RailExpanded { get; set; } = true;

    /// Eased rail width in pixels, so expanding/collapsing animates rather than snapping.
    public float RailWidthCurrent { get; set; }

    /// Points the shell at the destination containing `view`, and sets 1.0's pending view at the same time,
    /// from the one mapping table.
    public void RequestView(EchoMixView view)
    {
        PendingView = view;
        Destination = Shell.ShellRoutes.DestinationFor(view);
    }
}
