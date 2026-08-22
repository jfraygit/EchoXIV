namespace EchoMix.Plugin.UI;

/// One release's worth of user-facing highlights, shown in the Settings > Changelog tab.
public sealed record ChangelogEntry(string Version, string[] Highlights);

/// Newest first - Entries[0] is always "the latest changelog," which is what DjDeckWindow compares
/// Configuration.LastSeenChangelogVersion against to decide whether to show the "new update" badge.
public static class ChangelogData
{
    public static readonly ChangelogEntry[] Entries =
    {
        new("1.3.1.0", new[]
        {
            "Added a Web Listen Link - broadcasting hosts can generate a public link so anyone can tune into a live show right in their browser, no EchoMix required.",
            "Added /emix - jumps straight to the DJ Deck view, no matter what's currently on screen, the same way /el already does for the Listener view.",
            "EchoMix no longer opens or draws its window at the title screen or character select - it appears again once actually logged into a character, matching the rest of the Echo plugin suite.",
            "Genres now capitalize every word instead of just the first one, so \"heavy metal\" displays as \"Heavy Metal\" - applies to genres already saved on an existing profile too, not just newly-typed ones.",
        }),
        new("1.3.0.5", new[]
        {
            "AudioHost now recovers automatically if it ever stops responding while still technically connected, instead of needing a manual restart.",
        }),
        new("1.3.0.4", new[]
        {
            "AudioHost now retries a few times if it fails to start, before giving up - including force-closing a stuck instance first if one's left over. Mostly relevant to players running the game under Wine/Proton on Linux, where the audio driver can occasionally lose a one-time startup race and get stuck instead of coming up - a fresh start generally clears it.",
        }),
        new("1.3.0.2", new[]
        {
            "The DJ List now reshuffles once a day (at noon Eastern) instead of staying in the order profiles were created - new DJs used to land at the very end of an ever-growing list and stay there. Whoever's currently live still floats to the front regardless of the day's shuffle.",
            "Added 10 new avatar frame styles (Ticks, Chain, Brackets, Stitch, Blocks, Sentry, Anchor, Pendulum, Glitch, Confetti) and 10 new DJ name effects (Outline, Underline, Embossed, Split, Typewriter, Marquee, Glitch, Cascade, Heatwave, Blink), available in the DJ Profile editor alongside the originals.",
            "The bug report box now wraps as you type instead of running off the edge.",
            "Added 20 new visualizer styles (Waveform, Radial, Rings, Polygon, Peak Bars, Waterfall, Embers, Heat Strip, Orbit Dots, Skyline, Pulse Line, Starfield, Matrix Rain, Kaleidoscope, Comet Ride, Mesh, Pyramid Bars, Ripple Field, Bounce Balls, Aurora) alongside the original seven - pick one under Settings > General > Visualizer, separately for the DJ's own view and for listeners.",
            "Added Linked Characters to the DJ Profile editor - link an alt to a listing with a short one-time code generated from the main character, so broadcasts, likes, and follows from either one count toward the same profile. Up to 5 linked characters per listing, each with full edit access. An optional toggle shows the linked names publicly on the profile page under Also Known As.",
        }),
        new("1.3.0.0", new[]
        {
            "Added Auto-Join Nearby Shows (Beta) - a toggle on the Join a Show screen that automatically joins a live, public, Proximity-mode show once nearby, and leaves again on walking away, instead of finding and clicking a room manually. One show at a time; turning it on overrides manual joining until it's switched back off. A toast confirms each auto-join and auto-leave, with its own on/off switch under Settings > General > Notifications. Still early - the exact join/leave distance and timing will keep getting tuned.",
            "Added a Master Volume slider under Settings > General > Audio Engine, as a way to see and correct it if it's ever off - it previously had no control in the interface at all, so a change to it had no way to be noticed or fixed without editing a config file directly.",
        }),
        new("1.2.2.7", new[]
        {
            "Fixed External Input Mode not muting the game's BGM/Ambience the way Spotify Mode already does - the game's own music kept playing over a DJ's line-in or application capture the whole time External Input Mode was active.",
            "AudioHost (the plugin's separate background audio process) now closes itself right away if the game disappears without a clean exit, such as a crash or being closed from Task Manager, instead of lingering in the background afterward.",
            "The Line In device and application pickers (Settings > Line In) now offer a (None) option, so a previously selected device or application can be cleared without having to pick a different one to replace it.",
            "Fixed the image crop tool (DJ avatar, DJ banner, show/venue images) ignoring the zoom level chosen before hitting Use This - it always cropped the fully-zoomed-out framing regardless of how far in the preview had actually zoomed.",
        }),
        new("1.2.2.6", new[]
        {
            "External Input Mode can now capture a whole application's own audio instead of a Windows recording device (Settings > Line In) - for DJs who route their mixing software the way OBS's own Window Capture audio source does, rather than through a physical or virtual audio device.",
            "Fixed a crash: turning off External Input Mode could freeze and crash the game. Two separate causes were involved - the audio capture being torn down before its own background thread had actually finished with it, and a background health check that could try to stop the same capture a second time while a DJ's own stop was still in progress. Turning off External Input Mode no longer risks either one.",
        }),
        new("1.2.2.4", new[]
        {
            "Fixed genres not actually showing capitalized on the DJ List card or full profile page for any genre saved before the auto-capitalize update - that fix only ever applied to newly-typed genres, so anything already saved lowercase stayed lowercase no matter what. Every profile's genres now display properly capitalized regardless of when they were saved, and reopening a profile to edit it shows them capitalized too.",
            "External Input Mode now supports a second Line In device (Settings > Line In) for controllers or mixing software that route separate decks to separate Windows devices - both are captured and summed together into the broadcast, with Deck B's own Gain/Trim/EQ/Filter dials leveling the second one independently.",
            "External Input Mode now automatically recovers from a brief capture drop (e.g. some DJ software's Automix feature briefly reconfiguring its own audio engine on every transition) instead of dying outright and needing a manual restart. This narrows the gap rather than removing it outright - audio still can't be produced from a source that's briefly not emitting any - but the broadcast comes back on its own instead of staying silent.",
        }),
        new("1.2.2.3", new[]
        {
            "Added /el - jumps straight to the Listener view, no matter what's currently on screen, without needing to disable and re-enable the plugin just to get back to it.",
            "Fixed the \"Now Live\" and \"Listener Joined\" toast notifications clipping their message when a DJ name or character name was long enough to wrap onto more than one line.",
        }),
        new("1.2.2.2", new[]
        {
            "Fixed listeners not automatically rejoining a show after the host's own connection to the relay recovered on its own - the host's reconnect was working, but listeners had already given up waiting by the time the room came back, since a normal end-of-show and a dropped connection look identical to them. Listeners now keep retrying for the same window the host's own reconnect gets.",
            "Losing connection to the relay while broadcasting now shows a toast noting it's happening and reconnecting (and whether it made it back or gave up), instead of text buried in the Broadcast tab's Go Live panel that was easy to miss.",
            "Genres added to a DJ Profile are now automatically capitalized when entered starting with a lowercase letter.",
            "The Live Shows and DJ List genre filter now only offers real genres, instead of every one-off tag anyone's ever typed into a profile - a DJ's own Genres tags on their profile stay untouched either way.",
            "Saved Venues (and the Broadcast tab's venue address) now support apartments as well as houses - a House/Apartment choice is available when adding an address, with an extra option for housing areas that have a second apartment building. The Visit button now works for apartment venues too.",
        }),
        new("1.2.2.1", new[]
        {
            "A live show can now be renamed without stopping and restarting it - updating the Show Name field under Public Listing takes effect right away.",
            "Fixed a show sometimes dying outright when a host's connection to the relay hiccupped, with no way to tell what happened besides listeners going quiet. EchoMix now notices a stalled connection quickly and automatically tries to get the show back on its own, instead of leaving the host to notice and go live again manually.",
            "Fixed a listener's saved volume not actually taking effect when joining a show - it displayed the right value, but played at full volume until the slider was nudged. It's applied correctly from the moment of joining now.",
            "Fixed the volume popup sometimes drifting around the screen when clicked near the slider instead of directly on it.",
            "Fixed a rare issue where a show could keep showing as live in View Live Shows long after the DJ had actually stopped broadcasting.",
        }),
        new("1.2.2.0", new[]
        {
            "Common Venues on a DJ Profile is now Saved Venues - instead of a free-text tag, a real Data Center, World, Housing Area, Ward, and Plot is picked from dropdowns, plus a name for it. The profile shows just the name, with the full address on hover.",
            "Saved venues are now selectable right from the Broadcast tab when setting up a show, instead of retyping the address every time - manual entry is still available too. Common Venues text tags from before this update have been cleared, since they weren't a real address to begin with - they can be re-added as Saved Venues from the DJ Profile.",
            "A publicly-listed Proximity show now requires a venue address, so listeners can actually find it. A Global show can still list an address too, but it's optional.",
            "The DJ Profile editor no longer has a separate Save Listing button - changes now save automatically the moment Back is clicked.",
        }),
        new("1.2.1.9", new[]
        {
            "Report a Bug now has an optional field for a Discord name, so the developer can follow up directly about what was reported instead of hoping a reply in a public channel gets seen.",
        }),
        new("1.2.1.8", new[]
        {
            "Fixed joining a password-protected show from the Live Shows grid silently failing with no explanation - it now prompts for the password, and shows a real error if it's wrong.",
            "Password-protected shows now show a lock icon on their Join button as an up-front sign that a password's needed.",
        }),
        new("1.2.1.7", new[]
        {
            "A show's image now sticks around for every future show once it's been picked - no need to re-upload it before every future show, until it's changed.",
            "Added a \"View Other Shows\" button while listening (right under Leave Show) to check out what else is live without leaving the current show - it's easy to come back to.",
            "Scrolling now zooms in/out while positioning an avatar, banner, or show image, in addition to dragging to reposition it.",
        }),
        new("1.2.1.5", new[]
        {
            "Fixed deleting a song from a playlist leaving its file behind - re-uploading the same file afterward no longer gets renamed with a \"(2)\" since the old copy is actually gone now.",
            "Fixed the DJ Deck visualizer shifting down slightly the moment a track with a known BPM loaded - the BPM line now always reserves its space.",
            "Added a toast (and a toggle for it in General settings, under Notifications) that lets a host know when someone joins a show, public or private.",
        }),
        new("1.2.1.4", new[]
        {
            "Fixed Spotify Mode's audio gradually degrading during long broadcasts (sometimes clearing up on its own after a while) - the audio filter chain could occasionally settle into a state that silently slowed down processing during quiet passages. It now stays clear of that for the whole broadcast.",
        }),
        new("1.2.1.3", new[]
        {
            "DJ List cards now show each DJ's own name color and animated effect (Pulse, Rainbow, Wave, and the rest) - the same customization already available on their full profile page, instead of every card rendering the same flat color.",
            "The DJ List now puts whoever's currently live at the front (everyone else keeps their usual order), and got a search box next to the genre filter to jump straight to a DJ by name.",
            "Added an Edit button next to the DJ List's filter/search row (only shown for a DJ with a listing) to jump straight into editing it without hunting for the card in the grid.",
            "Added a Top Live DJs spotlight above the DJ List grid - the (up to) three live DJs with the most listeners orbit together with a fading comet trail, sized and lit by how many listeners they've got. Click one to jump straight to their profile.",
            "Fixed the DJ List and Live Shows grids silently clipping off the bottom of the window once there were enough cards to need scrolling - both scroll properly now.",
            "Avatar, banner, and show images can now be dragged to reposition within the crop before saving, instead of always being auto-cropped dead center.",
            "Fixed picking a new avatar/banner uploading it immediately instead of waiting for Save Listing - it's staged like every other field now and only actually saved when Save Listing is clicked.",
            "Moved the Save Listing button to the bottom-right of the Edit Listing form.",
            "Added a floating \"back to top\" button that fades in after scrolling down in the DJ List or Live Shows grid - eases smoothly back to the top instead of scrolling all the way back up by hand.",
        }),
        new("1.2.1.2", new[]
        {
            "Fixed the window border (and the Host Lobby/Song Request drawers' borders) from v1.2.1.1 sitting on top of other Dalamud windows even when they weren't actually focused - it now respects real window focus order, without losing its left/right edges in the process.",
            "Redesigned the Social section - Live Shows, DJ List, DJ Profiles, and the Add/Edit Listing form all got a pass. Both grids now fill the window width cleanly at a fixed column count instead of leaving a gap on the right, and gained proper empty/loading/error states with an icon and message instead of a single line of gray text.",
            "Live Show cards now match DJ List cards with a glowing border and gradient tint (orange for Venue shows, cyan for Global), rounded and depth-shaded artwork, and a cleaner info layout - the venue/Global label moved onto the artwork itself, and the address only takes up room when there actually is one instead of always reserving blank space (every card in a row still matches height regardless of whether its own address happens to show).",
            "DJ Profiles can now be liked and followed directly from the full profile page, not just from the DJ List card, with a like/follower count line under the name. The weekly availability list is now its own tinted panel, centered under the avatar, and a long time note wraps onto its own line instead of getting cut off.",
            "The Add/Edit Listing form was rebuilt as a two-column layout with a live preview card showing exactly what the listing will look like while editing it, tinted sections instead of plain dividers, and a proper pill-style tag editor for Genres and Common Venues.",
            "Settings and the Live Shows/DJ List switch now use a sliding, glowing underline to show which tab is selected instead of plain folder-style tabs or bare text - and the Live Shows/DJ List switch itself is smoother now too, with the underline and the grid beneath it animating together instead of snapping partway through.",
            "Gave Settings the same design pass Social already got - every section is now a tinted panel, sliders and toggle switches are hand-drawn instead of native ImGui controls, and mode pickers (Listen vs. Join as DJ, song request access) are a sliding segmented control instead of plain radio buttons. General, Broadcast, and Spotify now use the full window width in two columns instead of leaving half of it empty.",
            "A show image can now be picked before ever going live, not just after - it uploads automatically the instant of going live.",
            "Text fields, dropdowns, and color pickers across the whole plugin now have a visibly darker, bordered background so they stand out from the panel behind them instead of blending in.",
            "Fixed the Deck view (and Settings' two-column tabs) sitting a few pixels closer to the window's left edge than its right - both sides now give up the same margin instead of just one.",
        }),
        new("1.2.1.1", new[]
        {
            "Relay maintenance notices now appear as a small toast (the same style as the \"DJ went live\" notification) instead of taking over the whole window - dismiss it early with the Got It button, or it fades away on its own.",
            "DJ List genre tags are now capped to two lines per card, with a \"+N\" chip for anything past that, so every card in the grid keeps the same height and button layout no matter how many genres a DJ lists.",
            "Fixed Save Listing occasionally getting stuck on \"Saving...\" with no way to tell whether it would ever finish - it now times out with a clear error after 20 seconds if AudioHost never responds.",
            "Fixed a brief scrollbar flash on the right edge of the DJ Deck window while it un-minimizes or maximizes.",
            "Fixed the window's border (and the Host Lobby/Song Request side drawers' borders) disappearing wherever another Dalamud window happened to overlap it, instead of staying visible on top.",
        }),
        new("1.2.1.0", new[]
        {
            "Added Follow to the DJ List - a bell icon right next to Like. Following a DJ gives a brief on-screen notification (with a one-click Join button) the moment they start a public show. The notification fades itself in and out on its own and never steals focus.",
            "Added a Notifications setting (Settings > General) to choose where that notification appears on screen - a 3x3 grid with a live preview showing exactly where it'll show up before committing to a spot.",
            "Added a genre filter to both the DJ List and Live Shows grids.",
            "Added Auto-DJ - an opt-in toggle next to the crossfader that automatically fades into the other deck as the current track nears its end, so a set can keep going without manually mixing every transition. Right-click it to adjust how many seconds early it starts fading. Off by default; turning it on hands autoplay for both decks over to Auto-DJ as well, since it's now the one deciding when the next track starts.",
            "Redesigned the DJ List and DJ Profile pages with a more modern look - gradient-tinted cards, a glowing border and traveling highlight for whichever DJs are currently live, pill-style genre tags, a sharper banner image and DJ name, an isolated bio section, and a cleaner left/middle/right layout for Genres, Venues, and Aetherphone #.",
            "Added a separate Name Color option to DJ profiles (next to Name Effect when editing a listing) - colors the DJ name independently from the avatar frame's color. Only affects the full profile page, not the DJ List card.",
        }),
        new("1.2.0.4", new[]
        {
            "Fixed audio quality gradually degrading while co-hosting and monitoring the on-stage DJ during a longer session - the monitoring buffer's cushion against network hiccups was thinner than it needed to be.",
            "Fixed a rare warning/hitch where the tempo slider could briefly get a zero-width hit box right after un-minimizing the window.",
        }),
        new("1.2.0.3", new[]
        {
            "Fixed avatar photos not matching their own frame's corners - Corners and Gradient are genuinely square-cornered styles, so the photo underneath now stays square-cornered for those two instead of the rounded corners every other frame style gets.",
            "Nudged the avatar frame preview (in the DJ List edit form) to the right a bit - the Double and Glow styles' outer rings were running into the form's left edge.",
        }),
        new("1.2.0.2", new[]
        {
            "Waiting as a co-host now actually shows the show: Deck A/B display the on-stage DJ's real title, seek bar, elapsed/remaining time, and live visualizer instead of idle decks - switches back the moment of going live.",
            "Added an \"Up Next\" list to the Host Lobby drawer, showing what's queued up on the on-stage DJ's decks while waiting.",
            "Co-hosts can now send a song request straight to the on-stage DJ from their own Playlist - a Request button next to each track sends it through the same Requests tab a listener's request would, tagged \"(co-host)\" so it's easy to tell apart.",
            "Fixed Go Live and Connect showing no error at all when a required field (host password, listener password, room code) was left blank - previously either nothing happened or the hint was easy to miss.",
            "Fixed a wrong host password discarding the relay's real rejection reason in favor of a generic \"Registration rejected\" message.",
            "Fixed a relay-unreachable timeout showing the raw \"operation was canceled\" text instead of a clear message.",
            "Fixed rapid double-clicking Go Live/Connect/Join as DJ being able to launch two connection attempts at once.",
            "Fixed the Host Lobby drawer staying open after closing the main window.",
            "Fixed an accepted song request showing a garbled ID instead of the actual song title on the deck that loaded it.",
        }),
        new("1.2.0.1", new[]
        {
            "Removed the Report flag from DJ List cards - it's still on the full profile page, just no longer duplicated on the card too.",
            "A DJ List card going LIVE NOW no longer shifts its Like/View buttons compared to its neighbors - every card now reserves the same space for that line whether it's live or not.",
            "Fixed a small gap of bare background showing between an avatar photo and its own colored frame.",
        }),
        new("1.2.0.0", new[]
        {
            "Added a Social tab (new globe icon in the header) with two new ways to be found: Live Shows and the DJ List.",
            "Added Public Listing to Broadcast settings: checking \"List this show in 'View Live Shows'\" lets any listener browse straight into the show, no room code or password needed. Misuse (harassment, offensive content, impersonation, etc.) will get public-listing access revoked.",
            "A public show can be given a name, and an image uploaded for it once live - anything uploaded is auto-cropped/resized to fit.",
            "A publicly listed show reads as a Venue (with a structured FFXIV address - Data Center, World, Housing Area, Ward, Plot) when Proximity Audio is on, or as a Global show otherwise. Listeners get a one-click Visit button on Venue shows (via Lifestream, if installed) alongside Join.",
            "Added a Report option (flag icon) on every public show card for reporting abuse.",
            "Added the DJ List: a persistent, browsable directory of DJ profiles, completely separate from who's currently live - reach it via the new \"DJ List\" toggle right next to \"Live Shows\" in the Social tab.",
            "A listing can be created with a DJ name, bio, avatar, banner, genres, common venues, and a weekly availability schedule - each day can carry its own optional time note, e.g. \"8-10pm EST\".",
            "An avatar's frame can be customized with a chosen color and a choice of styles - Solid, Dashed, Double, Gradient, Pulse, Chase, or Rainbow.",
            "A DJ name can get its own animated flair on the profile page - Pulse, Rainbow, Wave, Gradient, Glow, Shimmer, Chase, or Flicker.",
            "Listeners can like a profile, see a running like count, and jump straight into a show with a LIVE NOW badge and Join button whenever that DJ is actively broadcasting.",
            "Added an optional Aetherphone # field - lists an in-game Aetherphone number on a profile as a click-to-copy chip, so a listener can grab it and paste it straight into their own Aetherphone's Add Contact screen.",
            "Added a Report option for abusive DJ List listings too, available both from a profile's card in the browse grid and from the full profile page.",
            "Only one DJ List listing is allowed per character, and the same ban list that governs public show listings applies here too - a banned character's listing won't show up in the browse grid.",
            "Genre/venue/Aetherphone # chips, the availability display, and a DJ's name on their card now always render in a fixed color scheme regardless of the Deck A/Deck B/Blend colors customized in Settings, so a personal theme never affects how another DJ's public listing looks.",
            "Fixed several icon-only buttons across the whole plugin (deck transport controls, the heart Like button, tag-list +/x buttons, and more) rendering slightly off-center within their own button box.",
            "Fixed the Discord invite link, which had expired.",
        }),
        new("1.1.7.2", new[]
        {
            "Fixed a layout bug affecting anyone with Dalamud's Global Font Scale set to anything other than 100% - the DJ Deck and Host Lobby panel could end up sized incorrectly, with content getting cut off or needing to scroll, and the Host Lobby drifting away from the main window. Both now stay correctly sized no matter what that setting is.",
            "Fixed the DJ Deck occasionally showing an unwanted scrollbar for some display/font setups.",
        }),
        new("1.1.7.1", new[]
        {
            "Added a heads-up notice that takes over the window - whatever's being looked at, expanded or minimized - whenever the relay needs to restart for an update, so a dropped connection a few seconds later doesn't come as a surprise. It reconnects on its own shortly after.",
            "Report a Bug now automatically captures a lot more behind-the-scenes diagnostic info (display resolution, DPI scaling, GPU, Dalamud version, and more) to help track down issues faster, especially ones that only show up on certain setups.",
        }),
        new("1.1.7.0", new[]
        {
            "Added a live timer showing how long a broadcast has been running, visible to both the host (next to the LIVE indicator) and listeners (next to the DJ's name) - a listener joining mid-show sees the real elapsed time, not a timer starting from zero.",
            "Added a Visualizer settings tab with independent style and sensitivity controls for the DJ's own view and for listeners, plus five new visualizer styles - Filled Area, Mirrored Bars, Mirrored Filled Area, Dots, and Blocks - alongside the original Bars and Smooth Line.",
            "Listeners can now fully customize their own visualizer's look and how much it reacts to the music, separately from the DJ's own.",
            "Moved the listener's volume control into a header icon with a popup slider, freeing up room for a bigger, more reactive default visualizer.",
            "Deck B is now hidden entirely - for both the host and every listener, expanded and minimized views alike - while Spotify Mode is active, since it can't be used in that mode. Just the song and artist are shown in its place.",
            "Added full theme customization - Deck A, Deck B, and Blend colors can be set in Settings > General > Deck Colors, which now affects everything themed in the app, including the window border and the EchoMix wordmark.",
            "Reopening EchoMix (via /echomix or the taskbar icon) now returns to the current role's view - the DJ Deck, or a Listener's Join a Show/live view - instead of always opening to the DJ Deck.",
            "Merged the Line In tab into Broadcast settings.",
            "Fixed a rare Spotify Mode audio glitch that could get stuck for the rest of a show, previously only fixable by toggling Spotify Mode off and back on.",
            "Fixed the \"new update\" notification dot on the Changelog tab getting clipped instead of sitting fully on the tab.",
        }),
        new("1.1.6.3", new[]
        {
            "Added a Report Bug button (top-right of the header, where the Discord icon used to be) - sends the AudioHost session log, plugin version, and current status straight to the developer, with an optional description of what happened.",
            "Moved the Discord invite into Settings > General under a new Community section, as a plain clickable link.",
        }),
        new("1.1.6.2", new[]
        {
            "Fixed another way a broadcast's audio quality could degrade and stay that way until a restart - this time in the live feed itself, so listeners no longer hear it too. Extends the fix from 1.1.6.1 to cover the broadcast/listening pipeline, not just Spotify Mode's own capture.",
        }),
        new("1.1.6.1", new[]
        {
            "Added quick Spotify Mode controls to the minimized window: right-click Deck A's half to skip to the next track, Shift+right-click to go back a track, and Shift+click to play/pause - all without restoring the full view. See Settings > Spotify for a reminder of the shortcuts.",
            "Added a Listeners section to the Host Lobby drawer, listing everyone currently tuned in to the show by character name.",
            "Fixed a rare issue where Spotify Mode's audio quality could degrade and stay that way for the rest of the show - it now detects when this happens and recovers on its own.",
        }),
        new("1.1.6.0", new[]
        {
            "Added Song Requests: listeners can upload a song from their own PC for the DJ to hear - a new icon in the top-left of the Listener window lets them pick a file and send it over.",
            "Requests show up in a new \"Requests\" tab right next to the Playlist tab, with the requester's name and a badge showing how many are waiting - load one straight to Deck A or B, or decline it.",
            "Added a Whitelist/Blacklist to Settings > Broadcast to control who's allowed to send song requests, by character name.",
            "Listeners are limited to one song request per minute, with a clear on-screen message telling them how long to wait.",
            "Requested songs only stick around for the length of the show - they're deleted the moment the broadcast ends, never added to the library.",
            "Added a crossfader curve selector: three small toggles under the crossfader pick Cut, Power (the crossfader's original feel, and still the default), or Linear.",
        }),
        new("1.1.5.0", new[]
        {
            "Added a Welcome screen when EchoMix starts up: pick DJ to go straight to the deck, or Listener to jump into a small, streamlined Join a Show screen - just a room code and password, instead of digging through Settings.",
            "Added \"Show Welcome screen on plugin start\" to Settings > General to skip it if preferred - it's on by default.",
            "If the DJ ends the show (or a listener's connection drops) while listening, they're now sent back to the Join a Show screen instead of the full DJ Deck.",
            "The minimize button no longer does anything while in Settings, preventing an accidental shrink mid-configuration.",
        }),
        new("1.1.4.1", new[]
        {
            "Shift-drag a deck's fader to push the other deck's fader by the same amount in the opposite direction - a quick way to rebalance both decks' levels at once. A regular drag still only moves the one being dragged.",
            "Shift-drag a High/Mid/Low/Filter/Gain knob to audition a change - it snaps back to where it started the moment it's released, instead of staying at the new position.",
            "Removed the \"DECK A\"/\"DECK B\" labels above each deck to free up room - the color coding already makes it obvious which is which.",
            "Fixed the header icons (Discord, Spotify Mode, Line In, Host Lobby, Minimize, etc.) shifting position when switching between the Deck view and Settings, or when opening the Changelog tab specifically.",
            "Fixed the crossfader drifting out of alignment with the tempo faders under each deck.",
        }),
        new("1.1.4.0", new[]
        {
            "Added Sync: click the link icon on a deck's transport row to tempo-match it to the other deck - it also nudges playback so the beats actually land together, not just matching BPM.",
            "Songs get their BPM detected automatically the first time they're added to the library (existing library songs get analyzed too) - right-click a song and use \"Edit BPM\" if the detected value is off.",
            "Added a manual tempo fader under each deck's display, same look as the other faders - drag to nudge a deck's speed by hand (up to +/-16%), right-click to snap back to normal. Using it while Sync is on turns Sync off for that deck, same as a real mixer.",
        }),
        new("1.1.3.0", new[]
        {
            "Added External Input Mode: broadcast audio from an external hardware mixer, audio interface, or separate mixing software instead of the decks. Pick a Windows recording device in Settings > Line In, then use the new plug icon up top to go live with it.",
            "External Input Mode never plays back through local speakers - the real mix is already being monitored through the external gear, so EchoMix only taps it for the broadcast. Sound pads still play (and are heard) locally as normal.",
            "Listeners just see \"Live Input\" in Deck A's spot while External Input Mode is on - the specific device name isn't shown to them.",
        }),
        new("1.1.2.2", new[]
        {
            "Spotify Mode's song title and artist now show up automatically, with no Spotify login required - it reads straight from Spotify's own desktop app instead of a Spotify account.",
            "Seeking within Spotify's own playback is no longer available, since that required the login above - the trade-off for song info that now works for every DJ, every time (instead of a handful of accounts, as Spotify's own rules had shrunk it to).",
            "Added an Autoplay toggle to each deck, next to Jump to Cue - turning it off keeps the next queued song from starting on its own the moment the current one ends.",
            "Added a \"Remove Sound Effect\" option to a sound pad's right-click menu, to clear one out without needing to overwrite it with a new upload.",
        }),
        new("1.1.2.1", new[]
        {
            "Fixed the minimized deck window (and the Listener's own minimized box) drifting further out of position every time it was minimized.",
            "Fixed an occasional visual glitch in the deck display while minimizing or restoring the window.",
            "Sound pads now mention middle-click to toggle looping in their tooltip.",
            "Playlist songs now show a tooltip noting that right-click sets that song's gain.",
        }),
        new("1.1.2.0", new[]
        {
            "Added a Song Preview cue: middle-click any upcoming queued song to hear it locally before it plays - only the DJ hears it, listeners keep hearing the current track the whole time.",
            "The current track automatically quiets down while previewing, and comes right back the moment preview stops (or it finishes on its own).",
            "Added \"Current Track Dampen %\" and \"Preview Song Volume %\" to Settings > General, to tune the preview to a personal headphone mix instead of a fixed guess.",
            "Fixed the Listener window's minimized box to match the DJ's own minimized deck view exactly.",
            "Fixed the Listener window's title/seek bar/timer overlapping the visualizer, Deck A's and Deck B's visualizers ending up different sizes, and Deck B's title showing the wrong color.",
        }),
        new("1.1.1.0", new[]
        {
            "Added a multi-host DJ lobby: go live with a separate host password, and other DJs can join the room as co-hosts instead of just listening - they keep their own full deck the whole time.",
            "Added \"Give Stage\": whoever's currently live can hand the stage to any connected co-host at any time - listeners cut over to the new DJ automatically.",
            "Co-hosts waiting their turn hear the live stage's audio mixed in with their own deck, so they can cue up and match tempo before taking over.",
            "Added the Host Lobby panel - a slide-out drawer next to the deck showing who's on stage and who's waiting as a co-host, with its own Monitor Volume control.",
            "Listeners now see both decks' own live visualizers and real song info (title, seek bar, elapsed/remaining time) side by side, instead of one blended view - in both the full Listener window and its minimized box.",
            "Added a \"Next Song\" button to both decks, between Play and Jump-to-Cue, to skip straight to whatever's queued next.",
            "The minimized deck view now shows both Deck A's and Deck B's own visualizers and track info stacked together, instead of just one blended visualizer.",
            "Deck A's and Deck B's titles now sit next to their own digital display instead of floating above the whole deck.",
            "New listeners now start at 50% volume by default instead of full volume, and the volume preference is remembered the next time a show is joined.",
            "\"Mute EchoMix in background\" now defaults off.",
            "Fixed the window occasionally drifting position after repeatedly minimizing and restoring it.",
            "Fixed a crash that could happen right after restoring the window from its minimized state.",
            "Added this Changelog tab, with a little indicator marking when there's something new to read.",
        }),
        new("1.1.0.0", new[]
        {
            "Added Spotify Mode: broadcast (or just locally play) whatever's playing in Spotify instead of the EchoMix playlist.",
            "Spotify Mode shows the song title and duration right on Deck A's display, with a draggable seek bar and full playback control for Spotify Premium accounts.",
            "Deck A's Gain, Trim, EQ, and Filter dials now work on Spotify Mode's audio too, instead of sitting dead.",
            "The game's own BGM and ambience now mute automatically while Spotify Mode is active, same as normal deck playback.",
            "Added .ogg file support for uploads and playback.",
            "Reworked the header buttons into a single consistent group: Lock, Proximity, Discord, Spotify, Settings, Minimize, Close.",
        }),
    };

    public static string LatestVersion => Entries[0].Version;
}
