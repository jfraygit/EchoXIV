namespace EchoMix.Plugin.UI;

/// One group of lines inside a release - Added, Changed, Fixed, Removed, Notes.
public sealed record ChangelogGroup(string Heading, string[] Lines);

/// One release's worth of user-facing notes, shown in Settings > Changelog.
public sealed record ChangelogEntry(string Version, ChangelogGroup[] Groups);

/// Newest first - Entries[0] is always "the latest changelog," which is what DjDeckWindow compares
/// Configuration.LastSeenChangelogVersion against to decide whether to show the "new update" badge.
public static class ChangelogData
{
    public static readonly ChangelogEntry[] Entries =
    {
        new("2.0.0.2", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "Badges on DJ profiles, under the profile picture. Hover one to see what it is for and the date you got it.",
                "The EchoMix 1.0 badge, for every DJ who had a listing before this release. It cannot be earned after that.",
            }),
            new ChangelogGroup("Changed", new[]
            {
                "The DJ list is split into pages of nine, or six while the menu is open.",
            }),
        }),
        new("2.0.0.1", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "A Refresh button on the DJ list.",
            }),
            new ChangelogGroup("Changed", new[]
            {
                "The DJ list is shuffled rather than ordered by likes and follows, and reshuffles every six hours. DJs who are live still come first.",
                "Most Followed and Most Liked now come first under Browse > Stats.",
            }),
            new ChangelogGroup("Fixed", new[]
            {
                "A DJ who had set no genres had their About text shown where their genres go.",
                "Genres on the DJ list and on profiles showed in whatever case they were typed.",
                "A DJ who ended their show stayed marked live on the DJ list until you restarted the plugin.",
            }),
        }),
        new("2.0.0.0", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "EchoMix has been redesigned from the ground up. Everything now lives behind a menu down the left: Mixer, Library, Broadcast, Listen, Browse and Settings.",
                "An opening animation when you first open the window.",
                "A Stats tab under Browse. It tracks shows played, time on air, listener hours, biggest room, days active and people reached, for this month or all time.",
                "Your all-time stats now show on your own profile, and on any DJ's.",
                "Deck colour themes under Settings > Appearance, with 28 presets including seasonal ones.",
                "Pass Host, so you can hand a running show to a co-host.",
                "The arrow keys move through the menu and through the current screen's own list.",
                "Your show's running time now sits at the top of the window while you are live.",
                "The Library now lists what is loaded and queued on each deck, with a button to clear either one.",
                "The top of the window tells you if AudioHost has stopped responding, instead of leaving you with numbers that quietly stopped updating.",
            }),
            new ChangelogGroup("Changed", new[]
            {
                "Song requests moved to Broadcast > Requests, and the menu shows a count when one is waiting.",
                "Joining a show as a co-host now opens the decks, instead of leaving you on the listener screen with no way back.",
                "Availability on your listing is picked as a start and end time instead of typed out.",
                "Every visualizer now follows your deck colours. They used to fade into a fixed purple whatever you had chosen.",
                "Like and Follow now show whether they are already on.",
                "Report a Bug moved to Settings > About.",
                "Notifications no longer land on top of each other when more than one arrives, and they size themselves to their message instead of cutting it off.",
            }),
            new ChangelogGroup("Removed", new[]
            {
                "The Volume Mixer, and the Aetherphone field on listings.",
            }),
            new ChangelogGroup("Fixed", new[]
            {
                "Your faders, gain and master volume read zero for the first few seconds after the plugin started.",
                "Closing Spotify during a show left Spotify Mode switched on and your broadcast silent, with nothing saying so.",
                "Turning on public listing while already live did not put your show in Live Shows.",
            }),
            new ChangelogGroup("Notes", new[]
            {
                "The 1.0 look is still there under Settings > Appearance, and switching takes effect straight away.",
            }),
        }),
        new("1.3.1.0", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "A Web Listen Link, so anyone can tune into your show in a browser without EchoMix.",
                "/emix, which jumps straight to the DJ Deck view.",
            }),
            new ChangelogGroup("Changed", new[]
            {
                "EchoMix no longer opens at the title screen or character select.",
                "Genres capitalize every word, including on profiles saved before this update.",
            }),
        }),
        new("1.3.0.5", new[]
        {
            new ChangelogGroup("Fixed", new[]
            {
                "AudioHost needed a manual restart if it stopped responding while still connected.",
            }),
        }),
        new("1.3.0.4", new[]
        {
            new ChangelogGroup("Fixed", new[]
            {
                "AudioHost now retries if it fails to start. This mostly affects Linux players running under Wine or Proton.",
            }),
        }),
        new("1.3.0.2", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "20 visualizer styles, which you can choose separately for your own view and for your listeners.",
                "10 avatar frame styles and 10 DJ name effects in the profile editor.",
                "Linked Characters, so an alt can share your listing. Broadcasts, likes and follows from either count toward the same profile, up to five characters.",
            }),
            new ChangelogGroup("Changed", new[]
            {
                "The DJ List reshuffles daily at noon Eastern, so a new DJ is not stuck at the end of it forever. Whoever is live still comes first.",
            }),
            new ChangelogGroup("Fixed", new[]
            {
                "The bug report box ran off the edge instead of wrapping as you typed.",
            }),
        }),
        new("1.3.0.0", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "Auto-Join Nearby Shows (Beta). It joins a nearby public Proximity show on its own and leaves when you walk away.",
                "A Master Volume slider under Settings > General > Audio Engine.",
            }),
        }),
        new("1.2.2.7", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "A (None) option in the Line In device and application pickers, so you can clear one without picking another.",
            }),
            new ChangelogGroup("Fixed", new[]
            {
                "External Input Mode did not mute the game's own music and ambience, so it played over your line-in the whole time.",
                "The image crop tool ignored the zoom level you chose before saving.",
                "AudioHost stayed open in the background after the game crashed or was force-closed.",
            }),
        }),
        new("1.2.2.6", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "External Input Mode can capture an application's audio instead of a recording device.",
            }),
            new ChangelogGroup("Fixed", new[]
            {
                "Turning off External Input Mode could crash the game.",
            }),
        }),
        new("1.2.2.4", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "A second Line In device for External Input Mode. Both are summed into your broadcast, with Deck B's own dials levelling the second one.",
            }),
            new ChangelogGroup("Changed", new[]
            {
                "External Input Mode recovers on its own after a brief capture drop instead of going silent for the rest of the show.",
            }),
            new ChangelogGroup("Fixed", new[]
            {
                "Genres saved before the auto-capitalize update still displayed lowercase.",
            }),
        }),
        new("1.2.2.3", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "/el, which jumps straight to the Listener view.",
            }),
            new ChangelogGroup("Fixed", new[]
            {
                "The Now Live and Listener Joined notifications cut off long names.",
            }),
        }),
        new("1.2.2.2", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "Apartments as well as houses in Saved Venues, including the Visit button.",
                "A notification when your connection drops while broadcasting, and whether it recovered.",
            }),
            new ChangelogGroup("Changed", new[]
            {
                "The genre filter only offers real genres instead of every tag anyone has ever typed.",
                "Genres are capitalized for you when you enter them.",
            }),
            new ChangelogGroup("Fixed", new[]
            {
                "Listeners did not rejoin automatically after your connection recovered.",
            }),
        }),
        new("1.2.2.1", new[]
        {
            new ChangelogGroup("Changed", new[]
            {
                "You can rename a live show without stopping and restarting it.",
            }),
            new ChangelogGroup("Fixed", new[]
            {
                "A connection hiccup could kill your show outright. It now gets itself back.",
                "A listener's saved volume showed the right number but played at full volume until the slider was moved.",
                "The volume popup drifted around the screen when clicked near the slider.",
                "A show could keep showing as live long after the DJ stopped broadcasting.",
            }),
        }),
        new("1.2.2.0", new[]
        {
            new ChangelogGroup("Changed", new[]
            {
                "Common Venues is now Saved Venues. You pick a real Data Center, World, Housing Area, Ward and Plot instead of typing it.",
                "You can choose a saved venue on the Broadcast tab when setting up a show.",
                "A publicly listed Proximity show needs a venue address, so listeners can find it.",
                "The DJ Profile editor saves when you go back, instead of needing a Save Listing button.",
            }),
            new ChangelogGroup("Notes", new[]
            {
                "Your old Common Venues tags have been cleared. They were never a real address, and can be re-added as Saved Venues.",
            }),
        }),
        new("1.2.1.9", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "An optional Discord name on Report a Bug, so the developer can follow up with you directly.",
            }),
        }),
        new("1.2.1.8", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "A lock icon on the Join button for password-protected shows.",
            }),
            new ChangelogGroup("Fixed", new[]
            {
                "Joining a password-protected show from Live Shows failed with no explanation.",
            }),
        }),
        new("1.2.1.7", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "A View Other Shows button while listening, so you can see what else is live without leaving.",
            }),
            new ChangelogGroup("Changed", new[]
            {
                "Your show image carries over to future shows instead of needing a re-upload each time.",
                "You can zoom with the scroll wheel while positioning an image.",
            }),
        }),
        new("1.2.1.5", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "A notification when someone joins your show, public or private, with its own toggle in Settings.",
            }),
            new ChangelogGroup("Fixed", new[]
            {
                "Deleting a song from a playlist left the file behind, so re-uploading it got renamed with a (2).",
                "The visualizer shifted down the moment a track with a known BPM loaded.",
            }),
        }),
        new("1.2.1.4", new[]
        {
            new ChangelogGroup("Fixed", new[]
            {
                "Spotify Mode's audio degraded over a long broadcast.",
            }),
        }),
        new("1.2.1.3", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "A Top Live DJs spotlight above the DJ List, showing the three live DJs with the most listeners.",
                "A search box on the DJ List.",
                "An Edit button on the DJ List for jumping straight into your own listing.",
                "A back to top button that fades in once you have scrolled down.",
            }),
            new ChangelogGroup("Changed", new[]
            {
                "DJ List cards show each DJ's own name colour and effect.",
                "Whoever is live comes first in the DJ List.",
                "You can drag an image to position it in the crop instead of it always centring.",
                "Save Listing moved to the bottom right of the form.",
            }),
            new ChangelogGroup("Fixed", new[]
            {
                "The DJ List and Live Shows grids clipped off the bottom of the window once there was enough to scroll.",
                "A new avatar or banner uploaded immediately instead of waiting for Save Listing.",
            }),
        }),
        new("1.2.1.2", new[]
        {
            new ChangelogGroup("Changed", new[]
            {
                "Live Shows, the DJ List, DJ Profiles and the listing editor have all been redesigned.",
                "The listing editor shows a live preview of your card while you edit it.",
                "You can like and follow from the full profile page, not just the card.",
                "Settings got the same pass, and now uses the full window width instead of half of it.",
                "You can pick a show image before going live, not only after.",
                "Text fields, dropdowns and colour pickers stand out from the panel behind them.",
            }),
            new ChangelogGroup("Fixed", new[]
            {
                "The window border sat on top of other Dalamud windows that were actually focused.",
                "The Deck view sat closer to the window's left edge than its right.",
            }),
        }),
        new("1.2.1.1", new[]
        {
            new ChangelogGroup("Changed", new[]
            {
                "Relay maintenance notices are a small notification instead of taking over the window.",
                "DJ List genre tags cap at two lines with a +N chip, so every card keeps the same height.",
            }),
            new ChangelogGroup("Fixed", new[]
            {
                "Save Listing could stick on Saving with no way to tell whether it would ever finish.",
                "The window border disappeared wherever another Dalamud window overlapped it.",
                "A scrollbar flashed on the right edge while the window minimized or restored.",
            }),
        }),
        new("1.2.1.0", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "Follow, with a notification and a one-click Join the moment a DJ you follow goes live.",
                "A Notifications setting for choosing where that notification appears on screen.",
                "Auto-DJ, which fades into the other deck as the current track ends. Off by default.",
                "A genre filter on the DJ List and Live Shows.",
                "A separate Name Colour for your profile.",
            }),
            new ChangelogGroup("Changed", new[]
            {
                "The DJ List and DJ Profile pages have been redesigned.",
            }),
        }),
        new("1.2.0.4", new[]
        {
            new ChangelogGroup("Fixed", new[]
            {
                "Audio quality degraded while co-hosting and monitoring during a long session.",
                "The tempo slider briefly could not be clicked after un-minimizing the window.",
            }),
        }),
        new("1.2.0.3", new[]
        {
            new ChangelogGroup("Fixed", new[]
            {
                "Avatar photos had rounded corners inside the square-cornered Corners and Gradient frames.",
                "The Double and Glow frame previews ran into the edge of the edit form.",
            }),
        }),
        new("1.2.0.2", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "An Up Next list in the Host Lobby, showing what is queued on the on-stage DJ's decks.",
                "Co-hosts can send a song request to the on-stage DJ from their own playlist.",
            }),
            new ChangelogGroup("Changed", new[]
            {
                "Waiting as a co-host shows the on-stage DJ's real decks, seek bars and visualizers.",
            }),
            new ChangelogGroup("Fixed", new[]
            {
                "Go Live and Connect showed no error when you left a required field blank.",
                "A wrong host password showed a generic message instead of the real reason.",
                "An unreachable relay showed raw error text instead of a clear message.",
                "Double-clicking Go Live or Connect started two connection attempts at once.",
                "The Host Lobby stayed open after you closed the main window.",
                "An accepted song request showed a garbled ID instead of the song title.",
            }),
        }),
        new("1.2.0.1", new[]
        {
            new ChangelogGroup("Removed", new[]
            {
                "The Report flag on DJ List cards. It is still on the full profile page.",
            }),
            new ChangelogGroup("Fixed", new[]
            {
                "A card going LIVE NOW shifted its buttons compared to its neighbours.",
                "A gap of bare background showed between an avatar photo and its frame.",
            }),
        }),
        new("1.2.0.0", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "A Social tab, with two ways to be found: Live Shows and the DJ List.",
                "Public Listing, so listeners can browse into your show without a room code or password.",
                "A name and an image for your public show.",
                "The DJ List, a browsable directory of DJ profiles separate from who is live.",
                "A listing carrying your DJ name, bio, avatar, banner, genres, venues and a weekly schedule.",
                "Avatar frame and DJ name customization, with a colour and an animated style.",
                "Likes on profiles, and a jump straight into that DJ's show while they are live.",
                "A Report option on shows and listings.",
            }),
            new ChangelogGroup("Changed", new[]
            {
                "A publicly listed show reads as a Venue with a real address, or as Global. Venue shows get a Visit button through Lifestream.",
                "Another DJ's listing always renders in fixed colours, so your own theme never changes how it looks.",
                "One listing per character. The public listing ban list applies to the DJ List too.",
            }),
            new ChangelogGroup("Fixed", new[]
            {
                "Several icon-only buttons rendered slightly off-centre.",
                "The Discord invite link had expired.",
            }),
        }),
        new("1.1.7.2", new[]
        {
            new ChangelogGroup("Fixed", new[]
            {
                "The window was sized wrong and cut off content if your Dalamud Global Font Scale was anything other than 100%.",
                "The DJ Deck showed an unwanted scrollbar on some display and font setups.",
            }),
        }),
        new("1.1.7.1", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "A notice when the relay needs to restart, so losing connection a moment later is not a surprise.",
            }),
            new ChangelogGroup("Changed", new[]
            {
                "Report a Bug captures more about your setup, which helps with problems that only happen on some machines.",
            }),
        }),
        new("1.1.7.0", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "Full theme customization under Settings > General > Deck Colors.",
                "A Visualizer settings tab, with separate style and sensitivity for you and for your listeners.",
                "Five visualizer styles: Filled Area, Mirrored Bars, Mirrored Filled Area, Dots and Blocks.",
                "A live timer showing how long a broadcast has been running, for hosts and listeners.",
            }),
            new ChangelogGroup("Changed", new[]
            {
                "The listener's volume control moved into a header icon, making room for a bigger visualizer.",
                "Deck B is hidden while Spotify Mode is on, since it cannot be used.",
                "Reopening EchoMix returns to your current role's view instead of always the DJ Deck.",
                "The Line In tab is now part of Broadcast settings.",
            }),
            new ChangelogGroup("Fixed", new[]
            {
                "A Spotify Mode audio glitch could stay stuck for the rest of a show.",
                "The new update dot on the Changelog tab was clipped.",
            }),
        }),
        new("1.1.6.3", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "A Report Bug button. It sends your session log, plugin version and status to the developer.",
            }),
            new ChangelogGroup("Changed", new[]
            {
                "The Discord invite moved to Settings > General under Community.",
            }),
        }),
        new("1.1.6.2", new[]
        {
            new ChangelogGroup("Fixed", new[]
            {
                "Audio quality degraded in the live feed itself, so your listeners heard it too.",
            }),
        }),
        new("1.1.6.1", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "Spotify controls on the minimized window: right-click to skip, Shift+right-click to go back, Shift+click to play or pause.",
                "A Listeners list in the Host Lobby, showing everyone tuned in.",
            }),
            new ChangelogGroup("Fixed", new[]
            {
                "Spotify Mode's audio quality could degrade and stay that way for the rest of the show.",
            }),
        }),
        new("1.1.6.0", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "Song Requests. Listeners can send a song from their own PC for you to hear.",
                "A Requests tab next to the Playlist, with a count of how many are waiting.",
                "A whitelist and blacklist under Settings > Broadcast for who can send requests.",
                "A crossfader curve selector: Cut, Power or Linear.",
            }),
            new ChangelogGroup("Notes", new[]
            {
                "Listeners can send one request a minute. Requested songs are deleted when the show ends and never join your library.",
            }),
        }),
        new("1.1.5.0", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "A Welcome screen on startup for choosing between DJ and Listener.",
                "A streamlined Join a Show screen for listeners, instead of digging through Settings.",
            }),
            new ChangelogGroup("Changed", new[]
            {
                "When a show ends you are returned to Join a Show instead of the full DJ Deck.",
                "The minimize button does nothing while you are in Settings.",
            }),
        }),
        new("1.1.4.1", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "Shift-drag a deck fader to move the other deck's the opposite way by the same amount.",
                "Shift-drag a knob to audition a change. It snaps back when you let go.",
            }),
            new ChangelogGroup("Removed", new[]
            {
                "The DECK A and DECK B labels. The colours already say which is which.",
            }),
            new ChangelogGroup("Fixed", new[]
            {
                "The header icons shifted position when switching between the Deck view and Settings.",
                "The crossfader drifted out of alignment with the tempo faders.",
            }),
        }),
        new("1.1.4.0", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "Sync, which tempo-matches a deck to the other and lines the beats up.",
                "Automatic BPM detection, with Edit BPM for correcting it.",
                "A tempo fader under each deck for nudging its speed by hand, up to 16%.",
            }),
        }),
        new("1.1.3.0", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "External Input Mode. Broadcast from a hardware mixer, audio interface or other software instead of the decks.",
            }),
            new ChangelogGroup("Notes", new[]
            {
                "External Input Mode does not play back locally, since you are already monitoring the real mix through your own gear. Sound pads still play.",
                "Listeners see Live Input in Deck A's place, not your device name.",
            }),
        }),
        new("1.1.2.2", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "An Autoplay toggle on each deck, for stopping the next queued song starting on its own.",
                "Remove Sound Effect in a sound pad's right-click menu.",
            }),
            new ChangelogGroup("Changed", new[]
            {
                "Spotify Mode shows the song title and artist with no Spotify login required.",
                "You can no longer seek Spotify's playback. That is what dropping the login cost, and it buys song info that works for everyone rather than a handful of accounts.",
            }),
        }),
        new("1.1.2.1", new[]
        {
            new ChangelogGroup("Changed", new[]
            {
                "Sound pads mention middle-click to loop in their tooltip.",
                "Playlist songs mention right-click to set gain in their tooltip.",
            }),
            new ChangelogGroup("Fixed", new[]
            {
                "The minimized window drifted further out of position every time you minimized it.",
                "A visual glitch in the deck display while minimizing or restoring.",
            }),
        }),
        new("1.1.2.0", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "Song Preview. Middle-click a queued song to hear it yourself before it plays. Your listeners keep hearing the current track.",
                "Current Track Dampen and Preview Song Volume under Settings > General, for tuning the preview to your own headphones.",
            }),
            new ChangelogGroup("Fixed", new[]
            {
                "The Listener window's minimized box did not match the DJ's own.",
                "The Listener window's title and seek bar overlapped the visualizer, and Deck B's title was the wrong colour.",
            }),
        }),
        new("1.1.1.0", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "A multi-host lobby. Other DJs can join your room as co-hosts, each with their own full deck.",
                "Give Stage, for handing the stage to any connected co-host. Listeners cut over automatically.",
                "The Host Lobby panel, showing who is on stage and who is waiting, with its own monitor volume.",
                "A Next Song button on both decks.",
                "This Changelog tab, with an indicator when there is something new.",
            }),
            new ChangelogGroup("Changed", new[]
            {
                "Co-hosts hear the live stage mixed with their own deck, so they can cue up before taking over.",
                "Listeners see both decks' visualizers and song info side by side instead of one blended view.",
                "The minimized view shows both decks instead of one blended visualizer.",
                "Deck titles sit next to their own display instead of floating above the deck.",
                "New listeners start at 50% volume, and their volume is remembered for next time.",
                "Mute EchoMix in background now defaults to off.",
            }),
            new ChangelogGroup("Fixed", new[]
            {
                "The window drifted position after repeatedly minimizing and restoring.",
                "A crash after restoring the window from minimized.",
            }),
        }),
        new("1.1.0.0", new[]
        {
            new ChangelogGroup("Added", new[]
            {
                "Spotify Mode. Broadcast or locally play whatever is playing in Spotify.",
                "Spotify Mode shows the title and duration on Deck A, with a seek bar and playback control for Premium accounts.",
                "Support for .ogg uploads and playback.",
            }),
            new ChangelogGroup("Changed", new[]
            {
                "Deck A's Gain, Trim, EQ and Filter work on Spotify Mode's audio.",
                "The game's music and ambience mute while Spotify Mode is on.",
                "The header buttons are one group: Lock, Proximity, Discord, Spotify, Settings, Minimize, Close.",
            }),
        }),
    };

    public static string LatestVersion => Entries[0].Version;

    /// Whether the newest entry has not been read yet, which is what the indicator on Settings and on its
    /// Changelog row reports.
    public static bool HasUnseen(Configuration configuration) =>
        configuration.LastSeenChangelogVersion != LatestVersion;
}
