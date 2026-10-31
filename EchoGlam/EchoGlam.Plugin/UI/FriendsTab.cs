using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoGlam.Game;
using EchoGlam.Shared;
using EchoGlam.UI.Controls;
using EchoGlam.UI.Foundation;

namespace EchoGlam.UI;

/// Who may see your glamours, and who has asked to.
public sealed class FriendsTab
{
    private readonly Plugin plugin;

    private string search = string.Empty;

    /// Which friend's card is showing its confirm-remove state, by id.
    private string confirming = string.Empty;

    /// Which of the two views the grid is showing: the people you know, or the people you have asked.
    private bool showingWaiting;

    public FriendsTab(Plugin plugin) => this.plugin = plugin;

    private Friendships Friendships => plugin.Friendships;

    private LookSharing Looks => plugin.Looks;

    /// Raised with a board profile id when somebody's card is clicked, so the window can show their page
    /// where other people's pages are shown.
    public event Action<string>? OpenProfile;


    /// Radius of the status lamp's core.
    private const float LampDesign = 5f;

    /// How far the lamp's halo carries, as a multiple of the core.
    private const float LampHalo = 2.4f;

    /// How fast the lamp breathes, in radians a second.
    private const float PulseSpeed = 1.6f;


    /// Air kept clear at the right-hand end of a band, on top of the halo - see the note where the halo is
    /// deducted.
    private const float GutterDesign = 16f;

    /// A person's monogram on a card.
    private const float DiscDesign = 40f;

    /// The same, in the requests band, where the row is shorter.
    private const float RequestDiscDesign = 34f;

    /// One row of the requests band.
    private const float RequestRowDesign = 44f;

    /// How many requests the band shows before it starts counting the rest.
    private const int RequestsShown = 3;

    /// Narrowest a person card may be.
    private const float CardDesign = 232f;

    /// How long a list has to be before a search field earns its place in the view band.
    private const int SearchFrom = 6;

    private const float SearchDesign = 240f;

    /// How tall a person card is.
    private static float CardHeight =>
        (Surface.PadDesign * 2f * UiHelpers.Scale) + (DiscDesign * UiHelpers.Scale) + Sp.Md + ActionRow;

    /// The row along the bottom of a card: a pill at one end, the marks at the other.
    private static float ActionRow => MathF.Max(Metrics.TouchMin, Pill.Height);


    public void Draw()
    {
        if (landed is { } found)
        {
            landed = null;
            looking = null;

            if (found.ProfileId is { } id)
            {
                profiles[found.Character] = id;
                notice = Message.None;
                OpenProfile?.Invoke(id);
            }
            else if (found.Failed)
            {
                notice = Message.Failed("Couldn't reach the board to look for their profile.");
            }
            else
            {
                profiles[found.Character] = null;
                notice = Missing(found.Character);
            }
        }

        var halo = EchoButton.GlowReach;

        var available = ImGui.GetContentRegionAvail() - new Vector2(halo, halo);
        var origin = ImGui.GetCursorScreenPos();

        var status = DrawStatus(available.X);

        if (!Friendships.SharingEnabled
            && Friendships.Friends.Count == 0
            && Friendships.Incoming.Count == 0
            && Friendships.Outgoing.Count == 0)
        {
            ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + status));

            Notice.Empty(
                available.X, FontAwesomeIcon.UserFriends,
                "Friends see what you're wearing",
                "And you see theirs. Nobody else does, and nobody can look you up until you turn "
                + "sharing on. Then right-click somebody in game and choose Add EchoGlam Friend.");

            return;
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + status));
        var requests = DrawRequests(available.X);

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + status + requests));
        var band = DrawViewBand(available.X);

        var consumed = status + requests + band;

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + consumed));

        DrawPeople(available.X, MathF.Max(Metrics.RowTall * 2f, available.Y - consumed));
    }


    /// What is true right now, what it means, and the switch.
    private float DrawStatus(float width)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var scale = UiHelpers.Scale;

        var sharing = Friendships.SharingEnabled;
        var lamp = LampDesign * scale;

        var block = (Text.DisplaySize * 1.2f) + (Text.CaptionSize * 1.45f);
        var row = MathF.Max(block, Metrics.RowTall);
        var right = origin.X + width - (GutterDesign * scale);


        const string label = "Share";

        var toggle = EchoToggle.Width(label);

        ImGui.SetCursorScreenPos(
            new Vector2(right - toggle, origin.Y + ((row - EchoToggle.Height) / 2f)));

        var wanted = sharing;

        if (EchoToggle.Draw(
                "##echoglamsharing", label, ref wanted,
                "Friends see the glamours you wear, and you see theirs. Nobody else does. "
                + "Turning this off pauses it - it doesn't remove your friends.")
            && wanted != sharing)
        {
            Friendships.SetSharing(wanted);
            confirming = string.Empty;
        }

        right -= toggle + Sp.Xxl;

        var drawn = Looks.Drawn;
        var publishing = Looks.SharingNow;

        MetaCell[] counts =
        [
            new MetaCell(
                "Friends", Friendships.Friends.Count.ToString(),
                Tooltip: "People you have both agreed to share with."),
            new MetaCell(
                "Sharing", publishing.ToString(),
                publishing > 0 ? Theme.Accent : Col.TextMuted,
                "Friends the relay is holding a look for: logged in with sharing on, whether or "
                + "not they're in front of you."),
            new MetaCell(
                "On Screen", drawn.ToString(),
                drawn > 0 ? Col.Success : Col.TextMuted,
                "Friends whose glamour EchoGlam is drawing for you this second."),
        ];

        var tiles = Meta.Width(counts);

        ImGui.SetCursorScreenPos(
            new Vector2(right - tiles, origin.Y + ((row - Meta.TileHeight) / 2f)));

        Meta.Tiles(tiles, counts);

        right -= tiles + Sp.Xxl;


        var reach = lamp * LampHalo;
        var centre = new Vector2(origin.X + reach, 0f);

        var textLeft = centre.X + lamp + Sp.Md;
        var room = MathF.Max(Sp.Xxxl, right - textLeft);
        var textTop = origin.Y + ((row - block) / 2f);
        var headlineHeight = Text.DisplaySize * 1.2f;

        var tone = sharing ? Col.Success : Col.TextMuted;

        using (Text.PushDisplay())
        {
            var headline = Text.Truncate(sharing ? "Sharing Is On" : "Sharing Is Off", room);

            drawList.AddText(new Vector2(textLeft, textTop), Col.U32(Col.Text), headline);

            centre.Y = textTop + (ImGui.GetTextLineHeight() / 2f);
        }

        var pulse = sharing
            ? 0.5f + (0.5f * MathF.Sin((float)ImGui.GetTime() * PulseSpeed))
            : 0f;

        for (var ring = 3; ring >= 1; ring--)
        {
            drawList.AddCircleFilled(
                centre,
                lamp * (1f + (ring * 0.4f * (sharing ? 0.8f + (0.35f * pulse) : 1f))),
                Col.U32(tone with { W = sharing ? 0.05f + (0.05f * pulse) : 0.03f }), 24);
        }

        drawList.AddCircleFilled(centre, lamp, Col.U32(tone), 24);

        using (Text.PushCaption())
        {
            drawList.AddText(
                new Vector2(textLeft, textTop + headlineHeight), Col.U32(Col.TextDim),
                Text.Truncate(
                    sharing
                        ? "Right-click somebody in game and choose Add EchoGlam Friend."
                        : "Nobody can look you up while this is off. Your friends are kept.",
                    room));
        }

        var bottom = origin.Y + row + DrawRelayMessage(drawList, origin, width, origin.Y + row);

        var ruleY = bottom + Sp.Lg;

        drawList.AddRectFilled(
            new Vector2(origin.X, ruleY), new Vector2(origin.X + width, ruleY + Stroke.Hair),
            Col.U32(Col.EdgeRest with { W = 0.55f }));

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, row));

        return ruleY + Stroke.Hair + Sp.Lg - origin.Y;
    }

    /// The last thing the relay said, with a way to put it away.
    private float DrawRelayMessage(ImDrawListPtr drawList, Vector2 origin, float width, float top)
    {
        var live = notice.Any
            ? notice
            : Friendships.Message.Length > 0
                ? Friendships.Failed
                    ? Message.Failed(Friendships.Message)
                    : Message.Ok(Friendships.Message)
                : Message.None;

        if (live.Any)
            held = live;

        var mark = Metrics.TouchMin;
        var full = Sp.Sm + mark;

        var shown = Motion.Toward("friendsmessage", live.Any, 16f);

        if (shown <= 0.002f || !held.Any)
            return 0f;

        var height = full * shown;
        var y = top + (Sp.Sm * shown);

        drawList.PushClipRect(
            new Vector2(origin.X, top), new Vector2(origin.X + width, top + height), true);

        float textWidth;

        using (Text.PushCaption())
        {
            var text = Text.Truncate(held.Text, MathF.Max(Sp.Xxxl, width - mark - Sp.Md));
            textWidth = Text.Width(text);

            drawList.AddText(
                new Vector2(origin.X, y + ((mark - Text.CaptionSize) / 2f)),
                Col.U32(held.Colour with { W = held.Colour.W * shown }), text);
        }

        drawList.PopClipRect();

        if (shown < 0.92f)
            return height;

        var cursor = ImGui.GetCursorScreenPos();

        ImGui.SetCursorScreenPos(new Vector2(origin.X + textWidth + Sp.Xs, y));

        if (EchoButton.BareIcon(
                "##dropfriendmessage", plugin.Fonts.Icon, FontAwesomeIcon.Times, mark,
                "Put this away.", colourOverride: Col.TextMuted))
        {
            notice = Message.None;
            Friendships.ClearMessage();
        }

        ImGui.SetCursorScreenPos(cursor);

        return height;
    }

    /// Something this page itself has to say, as opposed to something the relay said.
    private Message notice = Message.None;

    /// Whichever of the two was last showing, kept so the line has something to draw while it eases shut.
    private Message held = Message.None;


    /// Who has asked, above everything else, and only when somebody has.
    private float DrawRequests(float width)
    {
        if (Friendships.Incoming.Count == 0)
            return 0f;

        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var scale = UiHelpers.Scale;
        var pad = Surface.PadDesign * scale;

        var list = Friendships.Incoming.ToList();
        var shown = Math.Min(list.Count, RequestsShown);
        var overflow = list.Count - shown;

        var head = Text.CaptionSize * 1.15f;
        var rowHeight = RequestRowDesign * scale;

        var height = pad + head + Sp.Md
                     + (shown * rowHeight) + ((shown - 1) * Sp.Xs)
                     + (overflow > 0 ? Sp.Sm + head : 0f)
                     + pad;

        var size = new Vector2(width, height);

        Surface.Paint(origin, size, SurfaceKind.Card, Theme.Accent, inside: true);

        using (Text.PushCaption())
        {
            Text.Tracked(
                drawList, new Vector2(origin.X + pad, origin.Y + pad),
                list.Count == 1
                    ? "SOMEBODY WANTS TO BE FRIENDS"
                    : $"{list.Count} PEOPLE WANT TO BE FRIENDS",
                Col.U32(Theme.Accent), 1.8f, width - (pad * 2f));
        }

        var y = origin.Y + pad + head + Sp.Md;

        for (var i = 0; i < shown; i++)
        {
            DrawRequestRow(
                drawList, list[i], new Vector2(origin.X + pad, y), width - (pad * 2f), rowHeight);

            y += rowHeight + Sp.Xs;
        }

        if (overflow > 0)
        {
            using (Text.PushCaption())
            {
                drawList.AddText(
                    new Vector2(origin.X + pad, y - Sp.Xs + Sp.Sm), Col.U32(Col.TextMuted),
                    Text.Truncate(
                        overflow == 1
                            ? "One more waiting. Answer these and it moves up."
                            : $"{overflow} more waiting. Answer these and they move up.",
                        width - (pad * 2f)));
            }
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);

        return height + Sp.Xl;
    }

    private void DrawRequestRow(
        ImDrawListPtr drawList, FriendEntry entry, Vector2 min, float width, float height)
    {
        var disc = RequestDiscDesign * UiHelpers.Scale;
        var right = min.X + width;

        var accept = EchoButton.ContentSize("Accept").X;
        var decline = EchoButton.ContentSize("Decline").X;
        var buttons = accept + Sp.Sm + decline;

        var cursor = ImGui.GetCursorScreenPos();

        ImGui.SetCursorScreenPos(
            new Vector2(right - buttons, min.Y + ((height - Metrics.Row) / 2f)));

        if (EchoButton.Draw(
                $"##reqyes{entry.Id}", "Accept", new Vector2(accept, Metrics.Row),
                enabled: !Friendships.Busy, primary: true,
                tooltip: "You'll see each other's glamours."))
            Friendships.Answer(entry.Id, accept: true);

        ImGui.SameLine(0f, Sp.Sm);

        if (EchoButton.Draw(
                $"##reqno{entry.Id}", "Decline", new Vector2(decline, Metrics.Row),
                enabled: !Friendships.Busy,
                tooltip: "They aren't told, and they can ask again."))
            Friendships.Answer(entry.Id, accept: false);

        ImGui.SetCursorScreenPos(cursor);

        var name = Name(entry.Character);

        Avatar.Draw(
            drawList, null, null, name,
            new Vector2(min.X, min.Y + ((height - disc) / 2f)), disc);

        var textLeft = min.X + disc + Sp.Lg;

        TwoLines(
            drawList, new Vector2(textLeft, min.Y), height, name, World(entry.Character),
            MathF.Max(Sp.Xl, right - buttons - Sp.Lg - textLeft));
    }


    /// Which list, how long it is, and a way to search it.
    private float DrawViewBand(float width)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();

        var row = MathF.Max(Metrics.Row, Metrics.Field);

        ReadOnlySpan<string> views = ["Friends", "Waiting"];

        ImGui.SetCursorScreenPos(origin);

        var waiting = EchoSegment.Draw(
            "##friendviews", views, showingWaiting ? 1 : 0, row,
            [
                "People you have both agreed to share with.",
                "Requests you have sent that nobody has answered yet.",
            ]) == 1;

        if (waiting != showingWaiting)
        {
            showingWaiting = waiting;

            search = string.Empty;
            confirming = string.Empty;
        }

        var all = Listed();

        var searchable = all.Count > SearchFrom;

        if (!searchable && search.Length > 0)
            search = string.Empty;

        var searchRight = origin.X;

        if (searchable)
        {
            var searchWidth = SearchDesign * UiHelpers.Scale;
            var left = origin.X + EchoSegment.Width(views) + Sp.Xxl;
            var cursor = ImGui.GetCursorScreenPos();

            ImGui.SetCursorScreenPos(new Vector2(left, origin.Y + ((row - Metrics.Field) / 2f)));

            UiHelpers.SearchField(
                "##friendsearch", showingWaiting ? "Search Requests" : "Search Friends",
                ref search, searchWidth, Metrics.Field, plugin.Fonts.Icon);

            ImGui.SetCursorScreenPos(cursor);

            searchRight = left + searchWidth;
        }

        var total = all.Count;
        var visible = Shown().Count;
        var noun = showingWaiting ? "Waiting" : total == 1 ? "Friend" : "Friends";

        var count = total == 0
            ? string.Empty
            : visible == total
                ? $"{total} {noun}"
                : $"{visible} of {total}";

        if (count.Length > 0)
        {
            using (Text.PushCaption())
            {
                var measured = Text.Width(count);

                drawList.AddText(
                    new Vector2(
                        origin.X + width - measured,
                        origin.Y + ((row - Text.CaptionSize) / 2f)),
                    Col.U32(Col.TextMuted with { W = 0.75f }),
                    Text.Truncate(
                        count, MathF.Max(0f, origin.X + width - Sp.Lg - searchRight)));
            }
        }

        var ruleY = origin.Y + row + Sp.Md;

        drawList.AddRectFilled(
            new Vector2(origin.X, ruleY), new Vector2(origin.X + width, ruleY + Stroke.Hair),
            Col.U32(Col.EdgeRest with { W = 0.55f }));

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, row));

        return ruleY + Stroke.Hair + Sp.Lg - origin.Y;
    }


    private IReadOnlyList<FriendEntry> Listed() =>
        showingWaiting ? Friendships.Outgoing : Friendships.Friends;

    /// The active list, filtered and sorted.
    private List<FriendEntry> Shown()
    {
        var frame = ImGui.GetFrameCount();

        if (shownFrame == frame && shownWaiting == showingWaiting)
            return shown;

        shownFrame = frame;
        shownWaiting = showingWaiting;

        if (confirming.Length == 0)
            promotedWhenArmed = null;
        else
            promotedWhenArmed ??= Captured();

        var held = promotedWhenArmed;

        shown =
        [
            .. Listed()
                .Where(entry => search.Length == 0
                                || entry.Character.Contains(search, StringComparison.OrdinalIgnoreCase))

                .OrderByDescending(entry => held is null
                    ? Looks.IsDrawing(entry.Character)
                    : held.Contains(entry.Character))
                .ThenBy(entry => entry.Character, StringComparer.OrdinalIgnoreCase),
        ];

        return shown;
    }

    /// Who is on screen right now, as a set that will not change under a confirmation.
    private HashSet<string> Captured()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in Listed())
        {
            if (Looks.IsDrawing(entry.Character))
                set.Add(entry.Character);
        }

        return set;
    }

    /// The promoted set as it stood when a removal was armed, or null when nothing is armed.
    private HashSet<string>? promotedWhenArmed;

    private int shownFrame = -1;
    private bool shownWaiting;
    private List<FriendEntry> shown = [];

    /// The grid.
    private void DrawPeople(float width, float height)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        var open = ImGui.BeginChild("##friendlist", new Vector2(width, height), false);
        ImGui.PopStyleVar();

        if (open)
        {
            var list = Shown();

            if (list.Count == 0)
                DrawNothing(ImGui.GetContentRegionAvail().X);
            else
                DrawGrid(list);
        }

        ImGui.EndChild();
    }

    /// A GRID OF PEOPLE, NOT A COLUMN OF LINES.
    private void DrawGrid(List<FriendEntry> list)
    {
        var gap = Sp.Lg;
        var scale = UiHelpers.Scale;

        var width = ImGui.GetContentRegionAvail().X;

        var (columns, cardWidth) = Layout.Fit(width, CardDesign * scale, gap);
        var cardHeight = CardHeight;
        var size = new Vector2(cardWidth, cardHeight);

        var rows = (list.Count + columns - 1) / columns;
        var total = (rows * (cardHeight + gap)) - gap;

        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        ImGui.Dummy(new Vector2(width, total));

        for (var i = 0; i < list.Count; i++)
        {
            var min = origin + new Vector2(
                (i % columns) * (cardWidth + gap),
                (i / columns) * (cardHeight + gap));

            if (showingWaiting)
                DrawWaitingCard(drawList, list[i], min, size);
            else
                DrawFriendCard(drawList, list[i], min, size);
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + total));
    }

    /// One friend.
    private void DrawFriendCard(
        ImDrawListPtr drawList, FriendEntry entry, Vector2 min, Vector2 size)
    {
        var blocked = Friendships.IsBlocked(entry.Character);
        var confirm = string.Equals(confirming, entry.Id, StringComparison.Ordinal);
        var live = !blocked && Looks.IsDrawing(entry.Character);
        var sharing = !blocked && Looks.IsSharing(entry.Character);

        var (hovered, clicked) = CardHit($"##friendcard{entry.Id}", min, size);

        if (clicked && !confirm)
            OpenProfileFor(entry.Character);

        var tone =
            confirm ? Col.Danger
            : blocked ? Col.Caution
            : live ? (hovered ? Vector4.Lerp(Col.Success, Vector4.One, 0.25f) : Col.Success)
            : hovered ? Theme.Accent
            : (Vector4?)null;

        Surface.Paint(min, size, SurfaceKind.Card, tone, inside: true);

        var pad = Surface.PadDesign * UiHelpers.Scale;
        var disc = DiscDesign * UiHelpers.Scale;
        var right = min.X + size.X - pad;
        var actionTop = min.Y + pad + disc + Sp.Md;
        var name = Name(entry.Character);

        Avatar.Draw(drawList, null, null, name, min + new Vector2(pad, pad), disc);

        var textLeft = min.X + pad + disc + Sp.Lg;

        TwoLines(
            drawList, new Vector2(textLeft, min.Y + pad), disc, name, World(entry.Character),
            MathF.Max(Sp.Xl, right - textLeft));

        if (confirm)
        {
            var keep = EchoButton.ContentSize("Keep").X;
            var remove = EchoButton.ContentSize("Remove").X;
            var buttonY = actionTop + ((ActionRow - Metrics.Row) / 2f);
            var cursor = ImGui.GetCursorScreenPos();

            ImGui.SetCursorScreenPos(new Vector2(right - keep, buttonY));

            if (EchoButton.Draw($"##keep{entry.Id}", "Keep", new Vector2(keep, Metrics.Row)))
                confirming = string.Empty;

            ImGui.SetCursorScreenPos(
                new Vector2(right - keep - Sp.Sm - remove, buttonY));

            if (EchoButton.Draw(
                    $"##rm{entry.Id}", "Remove", new Vector2(remove, Metrics.Row),
                    accentOverride: Col.Danger, tooltip: "Ends it for both of you."))
            {
                Friendships.Remove(entry.Id);
                confirming = string.Empty;
            }

            ImGui.SetCursorScreenPos(cursor);
            return;
        }

        var icon = Metrics.TouchMin;
        var iconY = actionTop + ((ActionRow - icon) / 2f);
        var place = ImGui.GetCursorScreenPos();

        ImGui.SetCursorScreenPos(new Vector2(right - icon, iconY));

        if (EchoButton.BareIcon(
                $"##unfriend{entry.Id}", plugin.Fonts.Icon, FontAwesomeIcon.UserMinus, icon,
                "Remove this friend.",
                colourOverride: hovered ? Col.Danger : Col.TextMuted))
            confirming = entry.Id;

        ImGui.SetCursorScreenPos(new Vector2(right - (icon * 2f) - Sp.Xs, iconY));

        if (EchoButton.BareIcon(
                $"##block{entry.Id}", plugin.Fonts.Icon,
                blocked ? FontAwesomeIcon.EyeSlash : FontAwesomeIcon.Eye, icon,
                blocked
                    ? "Hidden. Click to see their glamours again."
                    : "Hide their glamours without unfriending them.",
                colourOverride: blocked ? Col.Caution : hovered ? Col.Text : Col.TextMuted))
            Friendships.SetBlocked(entry.Character, !blocked);

        ImGui.SetCursorScreenPos(place);

        var (label, pillTone, tip) = Status(blocked, live, sharing);

        Pill.DrawAt(
            new Vector2(min.X + pad, actionTop + ((ActionRow - Pill.Height) / 2f)),
            label, pillTone, tip, Footnote(entry, "Added", live || sharing));
    }

    /// One request you sent.
    private void DrawWaitingCard(
        ImDrawListPtr drawList, FriendEntry entry, Vector2 min, Vector2 size)
    {
        var (hovered, clicked) = CardHit($"##waitingcard{entry.Id}", min, size);

        if (clicked)
            OpenProfileFor(entry.Character);

        Surface.Paint(min, size, SurfaceKind.Card, hovered ? Theme.Accent : null, inside: true);

        var pad = Surface.PadDesign * UiHelpers.Scale;
        var disc = DiscDesign * UiHelpers.Scale;
        var right = min.X + size.X - pad;
        var actionTop = min.Y + pad + disc + Sp.Md;
        var name = Name(entry.Character);

        Avatar.Draw(drawList, null, null, name, min + new Vector2(pad, pad), disc);

        var textLeft = min.X + pad + disc + Sp.Lg;

        TwoLines(
            drawList, new Vector2(textLeft, min.Y + pad), disc, name, World(entry.Character),
            MathF.Max(Sp.Xl, right - textLeft));

        var icon = Metrics.TouchMin;
        var place = ImGui.GetCursorScreenPos();

        ImGui.SetCursorScreenPos(
            new Vector2(right - icon, actionTop + ((ActionRow - icon) / 2f)));

        if (EchoButton.BareIcon(
                $"##withdraw{entry.Id}", plugin.Fonts.Icon, FontAwesomeIcon.Times, icon,
                "Take the request back.",
                colourOverride: hovered ? Col.Danger : Col.TextMuted))
            Friendships.Remove(entry.Id);

        ImGui.SetCursorScreenPos(place);

        Pill.DrawAt(
            new Vector2(min.X + pad, actionTop + ((ActionRow - Pill.Height) / 2f)),
            "Waiting", Pill.Tone.Caution,
            "They haven't answered yet. It shows in their Friends tab within a minute of them "
            + "being asked - there is nothing more to do from this end.",
            Footnote(entry, "Sent", Looks.IsSharing(entry.Character)));
    }

    /// Which of the four things is true about a friend, and the sentence behind it.
    private static (string Label, Pill.Tone Tone, string Tooltip) Status(
        bool blocked, bool live, bool sharing)
    {
        if (blocked)
        {
            return ("Hidden", Pill.Tone.Caution,
                "You won't see their glamours until you turn the eye back on. They aren't told, "
                + "and you are still friends.");
        }

        if (live)
        {
            return ("On Screen", Pill.Tone.Success,
                "EchoGlam is drawing their glamour for you right now.");
        }

        if (sharing)
        {
            return ("Sharing", Pill.Tone.Accent,
                "They're logged in with sharing on. You'll see their glamour when they're in front "
                + "of you.");
        }

        return ("Not Sharing", Pill.Tone.Neutral,
            "The relay has nothing from them - they're logged out, or sharing is off at their "
            + "end.");
    }

    /// The dated half of a person's tooltip: when this started, and when they were last about.
    private string Footnote(FriendEntry entry, string verb, bool about)
    {
        var when = entry.WhenUtc.ToLocalTime().ToString("d MMM yyyy");

        var seen = about
            ? "just now"
            : Ago(Friendships.LastSeen(entry.Character) ?? entry.WhenUtc);

        return $"{verb} {when}\nLast seen {seen}";
    }

    /// How long ago, in the largest unit that still says something.
    private static string Ago(DateTime whenUtc)
    {
        var span = DateTime.UtcNow - whenUtc;

        return span switch
        {
            { TotalMinutes: < 2 } => "a moment ago",
            { TotalHours: < 1 } => $"{(int)span.TotalMinutes} minutes ago",
            { TotalHours: < 2 } => "an hour ago",
            { TotalDays: < 1 } => $"{(int)span.TotalHours} hours ago",
            { TotalDays: < 2 } => "yesterday",
            { TotalDays: < 7 } => $"{(int)span.TotalDays} days ago",
            _ => whenUtc.ToLocalTime().ToString("d MMM yyyy"),
        };
    }


    /// Board profile ids by character key.
    private readonly Dictionary<string, string?> profiles = new(StringComparer.OrdinalIgnoreCase);

    /// The character a lookup is in flight for, so a second click cannot start a second one.
    private string? looking;

    private sealed record Resolved(string Character, string? ProfileId)
    {
        /// Whether the lookup threw rather than coming back empty.
        public bool Failed { get; init; }
    }

    /// Set by a finished lookup and read on the next frame - see the note in Draw.
    private volatile Resolved? landed;

    /// Sends the window to somebody's board profile, finding it first if need be.
    private void OpenProfileFor(string characterKey)
    {
        if (profiles.TryGetValue(characterKey, out var known))
        {
            if (known is null)
            {
                notice = Missing(characterKey);
            }
            else
            {
                notice = Message.None;
                OpenProfile?.Invoke(known);
            }

            return;
        }

        if (looking is not null)
            return;

        looking = characterKey;
        Resolve(characterKey);
    }

    private static Message Missing(string characterKey) =>
        Message.Warn(
            $"{Name(characterKey)} hasn't published a glamour, so there's no profile to open.");

    /// Finds somebody's board profile from their character name.
    private void Resolve(string characterKey)
    {
        var name = Name(characterKey);
        var world = World(characterKey);

        _ = Task.Run(async () =>
        {
            try
            {
                var page = await GalleryClient.BrowseAsync(
                    new GalleryQuery
                    {
                        Sort = GlamourSort.Newest,
                        Text = name,

                        PageSize = 50,
                    },
                    plugin.Gallery.OwnerKey,
                    CancellationToken.None).ConfigureAwait(false);

                var match = page.Entries.FirstOrDefault(entry =>
                    string.Equals(entry.AuthorName, name, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(entry.AuthorWorld, world, StringComparison.OrdinalIgnoreCase)
                    && entry.AuthorId.Length > 0);

                landed = new Resolved(characterKey, match?.AuthorId);
            }
            catch (Exception ex)
            {
                Plugin.Log.Debug(ex, "[EchoGlam] Could not look up a friend's board profile.");
                landed = new Resolved(characterKey, null) { Failed = true };
            }
        });
    }

    /// What the grid says when there is nobody in it.
    private void DrawNothing(float width)
    {
        if (showingWaiting)
        {
            Notice.Empty(
                width, FontAwesomeIcon.PaperPlane, "Nobody's waiting",
                search.Length > 0
                    ? $"No request you've sent is to anybody called \"{search}\"."
                    : "Requests you send show here until they're answered.");

            return;
        }

        if (search.Length > 0)
        {
            Notice.Empty(
                width, FontAwesomeIcon.Search, "Nobody matches that",
                $"No friend's name contains \"{search}\".");

            return;
        }

        if (!Friendships.Synced)
        {
            Notice.Busy(width, "Checking", "Asking the relay who you know.");
            return;
        }

        Notice.Empty(
            width, FontAwesomeIcon.UserPlus, "No friends yet",
            "Right-click somebody in game and choose Add EchoGlam Friend.");
    }


    /// Submits the card's own hit area and says what the pointer did to it.
    private static (bool Hovered, bool Clicked) CardHit(string id, Vector2 min, Vector2 size)
    {
        var cursor = ImGui.GetCursorScreenPos();

        ImGui.SetCursorScreenPos(min);
        ImGui.InvisibleButton(id, size, ImGuiButtonFlags.MouseButtonLeft);

        var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
        var clicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);

        ImGui.SetItemAllowOverlap();

        ImGui.SetCursorScreenPos(cursor);

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        return (hovered, clicked);
    }

    /// A name over its world, vertically centred in a height the caller owns.
    private static void TwoLines(
        ImDrawListPtr drawList, Vector2 min, float height, string primary, string secondary,
        float room)
    {
        var line = ImGui.GetTextLineHeight();
        var block = line + Sp.Xxs + (Text.CaptionSize * 1.15f);
        var top = min.Y + ((height - block) / 2f);

        drawList.AddText(new Vector2(min.X, top), Col.U32(Col.Text), Text.Truncate(primary, room));

        using (Text.PushCaption())
        {
            Text.Tracked(
                drawList, new Vector2(min.X, top + line + Sp.Xxs),
                secondary.ToUpperInvariant(), Col.U32(Col.TextDim), 1.4f, room);
        }
    }

    private static string Name(string characterKey)
    {
        var at = characterKey.LastIndexOf('@');
        return at > 0 ? characterKey[..at] : characterKey;
    }

    private static string World(string characterKey)
    {
        var at = characterKey.LastIndexOf('@');
        return at > 0 && at < characterKey.Length - 1 ? characterKey[(at + 1)..] : "Unknown world";
    }

}
