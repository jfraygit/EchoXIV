using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoGlam.Game;
using EchoGlam.UI.Controls;
using EchoGlam.UI.Foundation;

namespace EchoGlam.UI;

/// The outfits pane: what you have saved, and the one thing that puts them on by itself.
public sealed class OutfitsPane
{
    private readonly Plugin plugin;

    /// Needed for two things only: the appearance a save carries, and telling the editor its sliders are
    /// stale after an outfit has written a new face.
    private readonly AppearanceEditor appearance;

    public OutfitsPane(Plugin plugin, AppearanceEditor appearance)
    {
        this.plugin = plugin;
        this.appearance = appearance;
    }

    private OutfitStore Store => plugin.Outfits;

    private OutfitRuleStore Rules => plugin.Rules;

    private Wardrobe Wardrobe => plugin.Wardrobe;

    private ItemCatalogue Items => plugin.Items;


    /// Name typed into the box, whether the look came from the character or the clipboard.
    private string outfitName = string.Empty;

    /// Whether a saved outfit carries the appearance as well as the gear.
    private bool saveAppearance;

    /// A Glamourer design that has been pasted and read, waiting to be named and kept.
    private GlamourerLook? pendingImport;

    /// The last thing this pane has to say, and the copy held so the line can animate shut.
    private Message notice = Message.None;
    private Message held = Message.None;

    /// Filter over the saved list.
    private string outfitSearch = string.Empty;

    /// Newest first rather than alphabetical.
    private bool outfitsByDate;

    /// Which outfit's delete mark has been armed, if any.
    private string? pendingDelete;

    /// Which outfit's conditions are open, or null for the saved list.
    private string? rulesFor;

    /// Whether the rule order is open.
    private bool orderOpen;

    /// State of the map picker, while adding or changing a place rule.
    private OutfitRule? pickingPlaceFor;
    private string zoneSearch = string.Empty;

    /// Null for everything, true for duties only, false for open world only.
    private bool? zoneDutyFilter;

    /// Filter over the fallback-outfit popup.
    private string fallbackSearch = string.Empty;

    /// Which outfit's animation is being chosen, or null.
    private string? pickingAnimationFor;
    private string animationSearch = string.Empty;

    /// Null for both, or one source of animations.
    private AnimationSource? animationFilter;

    /// Called when the bench switches to another pane.
    public void Leave() => pendingDelete = null;


    /// Radius of the status lamp's core, and how far its halo carries as a multiple of it.
    private const float LampDesign = 5f;
    private const float LampHalo = 2.4f;

    /// How fast the lamp breathes, in radians a second.
    private const float PulseSpeed = 1.6f;

    /// One gear icon in a row's leading strip, and how many of them a row shows.
    private const float GearDesign = 28f;
    private const int GearShown = 3;

    /// Which slots stand in for a look, best first.
    private static readonly GlamSlot[] GearOrder =
    [
        GlamSlot.Body, GlamSlot.Legs, GlamSlot.Head, GlamSlot.MainHand,
        GlamSlot.Feet, GlamSlot.Hands, GlamSlot.OffHand,
        GlamSlot.Ears, GlamSlot.Neck, GlamSlot.Wrists, GlamSlot.RingR, GlamSlot.RingL,
    ];

    /// How long the saved list has to be before a search field earns its place.
    private const int SearchFrom = 6;

    private const float SearchDesign = 200f;


    /// This frame's search results, and the inputs they were worked out from.
    private List<AnimationEntry> animationMatches = [];
    private (string Search, AnimationSource? Source)? animationKey;

    private List<Zone> zoneMatches = [];
    private (string Search, bool? Duty)? zoneKey;

    private List<AnimationEntry> AnimationMatches()
    {
        var key = (animationSearch, animationFilter);

        if (animationKey is { } last && last == key)
            return animationMatches;

        animationKey = key;
        animationMatches = [.. plugin.Animations.Search(animationSearch, animationFilter)];
        return animationMatches;
    }

    private List<Zone> ZoneMatches()
    {
        var key = (zoneSearch, zoneDutyFilter);

        if (zoneKey is { } last && last == key)
            return zoneMatches;

        zoneKey = key;
        zoneMatches = [.. plugin.Zones.Search(zoneSearch, zoneDutyFilter)];
        return zoneMatches;
    }

    /// How many outfits before the fallback popup grows a search box, and how many rows it shows before it
    /// scrolls.
    private const int FallbackSearchFrom = 8;
    private const int FallbackRows = 8;
    private const float FallbackDesign = 220f;

    private const string FallbackPopupId = "##echoglamfallback";


    public void Draw()
    {
        if (pickingPlaceFor is not null)
        {
            DrawZonePicker();
            return;
        }

        if (pickingAnimationFor is { } animating)
        {
            DrawAnimationPicker(animating);
            return;
        }

        if (rulesFor is { } editing)
        {
            DrawConditions(editing);
            return;
        }

        if (orderOpen)
        {
            DrawOrder();
            return;
        }

        DrawList();
    }

    private void DrawList()
    {
        var halo = EchoButton.GlowReach;

        var available = ImGui.GetContentRegionAvail() - new Vector2(halo, halo);
        var origin = ImGui.GetCursorScreenPos();

        var status = DrawStatus(available.X);

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + status));
        var save = DrawSaveBand(available.X);

        var consumed = status + save;

        if (Store.Count == 0)
        {
            ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + consumed));

            Notice.Empty(
                available.X, FontAwesomeIcon.Bookmark,
                "Nothing saved yet",
                Wardrobe.Active
                    ? "You have a look on. Name it above and it is kept - gear, dyes, and your "
                      + "appearance if you want it."
                    : "Put a look together in the slots on the left, then name it above to keep it.");

            return;
        }

        if (Store.Count <= SearchFrom && outfitSearch.Length > 0)
            outfitSearch = string.Empty;

        var list = Listed();

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + consumed));
        var band = DrawListBand(available.X, list.Count);

        consumed += band;

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + consumed));

        DrawRows(list, available.X, MathF.Max(Row.Height * 2f, available.Y - consumed));
    }


    /// What is dressing you, why, and the switch.
    private float DrawStatus(float width)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var scale = UiHelpers.Scale;

        var on = Rules.Enabled;
        var open = Motion.Toward("outfitsautomation", on, 13f);

        var closed = Metrics.RowTall;
        var banner = DressingRoomTab.BannerHeight;
        var row = closed + ((banner - closed) * open);

        var pad = Sp.Xl;

        var right = origin.X + width - (pad * open);

        var worn = plugin.Automation.Claimed;
        var surrendered = plugin.Automation.Surrendered;
        var driving = on && worn is not null && !surrendered;

        const string label = "Automatic";

        var toggle = EchoToggle.Width(label);
        var closedY = origin.Y + ((closed - EchoToggle.Height) / 2f);
        var switchY = closedY + ((origin.Y + pad - closedY) * open);

        var cursor = ImGui.GetCursorScreenPos();

        if (open > 0.01f)
        {
            Fade(open);

            Surface.Paint(
                origin, new Vector2(width, row), SurfaceKind.Raised,
                driving ? Theme.Accent : null, inside: true);

            Unfade();
        }

        ImGui.SetCursorScreenPos(new Vector2(right - toggle, switchY));

        var wanted = on;

        if (EchoToggle.Draw(
                "##outfitsautomatic", label, ref wanted,
                "Wears an outfit when its conditions are met, and puts it away again when they stop. "
                + "Open an outfit below to say when.")
            && wanted != on)
        {
            Rules.Enabled = wanted;
            pendingDelete = null;
        }

        ImGui.SetCursorScreenPos(cursor);


        var here = plugin.Zones.NameFor(Plugin.ClientState.TerritoryType);

        if (plugin.Housing.Current is { } house)
            here = $"{here}, {house.Address}";

        string headline;
        string meta;
        Vector4 tone;

        if (Rules.ReadOnly)
        {
            headline = "Rules Cannot Be Saved";
            meta = "outfit-rules.json could not be read. Move or delete it and reload the plugin.";
            tone = Col.Danger;
        }
        else if (worn is not null && !surrendered)
        {
            headline = worn;
            meta = $"Worn by a rule  ·  {here}";
            tone = Theme.Accent;
        }
        else if (worn is not null)
        {
            headline = worn;
            meta = $"You changed it by hand, so the rule let go  ·  {here}";
            tone = Col.Caution;
        }
        else if (Rules.Rules.Count == 0)
        {
            headline = "No Conditions Set";
            meta = "Open an outfit below and say when it should go on.";
            tone = Col.TextMuted;
        }
        else
        {
            headline = "Nothing Matches Here";
            meta = here;
            tone = Col.TextMuted;
        }

        drawList.PushClipRect(origin, new Vector2(origin.X + width, origin.Y + row), true);

        if (open > 0.01f)
        {
            Fade(open);

            var lamp = LampDesign * scale;
            var reach = lamp * LampHalo;

            var captionLine = Text.CaptionSize * 1.15f;
            var titleLine = Text.TitleSize * 1.2f;
            var block = captionLine + Sp.Xs + titleLine + Sp.Xs + captionLine;
            var top = origin.Y + ((row - block) / 2f);

            var centre = new Vector2(origin.X + pad + reach, 0f);
            var headlineTop = top + captionLine + Sp.Xs;

            var textLeft = centre.X + lamp + Sp.Md;
            var room = MathF.Max(Sp.Xxxl, right - toggle - Sp.Xl - textLeft);

            using (Text.PushCaption())
            {
                Text.Tracked(
                    drawList, new Vector2(textLeft, top), "AUTOMATIC OUTFITS",
                    Col.U32(driving ? Theme.Accent : Col.TextMuted), 1.8f, room);
            }

            using (Text.PushTitle())
            {
                drawList.AddText(
                    new Vector2(textLeft, headlineTop),
                    Col.U32(worn is not null && !surrendered ? Col.Text : Col.TextDim),
                    Text.Truncate(headline, room));

                var font = ImGui.GetFont();
                var cap = ImGui.FindGlyph(font, 'N');

                centre.Y = headlineTop + (cap.IsNull || font.FontSize <= 0f
                    ? ImGui.GetTextLineHeight() / 2f
                    : (cap.Y0 + cap.Y1) / 2f * (ImGui.GetFontSize() / font.FontSize));
            }

            using (Text.PushCaption())
            {
                drawList.AddText(
                    new Vector2(textLeft, headlineTop + titleLine + Sp.Xs),
                    Col.U32(tone == Col.Danger ? Col.Danger : Col.TextDim),
                    Text.Truncate(meta, room));
            }

            var pulse = driving
                ? 0.5f + (0.5f * MathF.Sin((float)ImGui.GetTime() * PulseSpeed))
                : 0f;

            for (var ring = 3; ring >= 1; ring--)
            {
                drawList.AddCircleFilled(
                    centre,
                    lamp * (1f + (ring * 0.4f * (driving ? 0.8f + (0.35f * pulse) : 1f))),
                    Col.U32(tone with { W = driving ? 0.05f + (0.05f * pulse) : 0.03f }), 24);
            }

            drawList.AddCircleFilled(centre, lamp, Col.U32(tone), 24);

            Unfade();
        }

        if (open < 0.99f)
        {
            Fade(1f - open);

            using (Text.PushCaption())
            {
                drawList.AddText(
                    new Vector2(origin.X, origin.Y + ((closed - Text.CaptionSize) / 2f)),
                    Col.U32(Rules.ReadOnly ? Col.Danger : Col.TextDim),
                    Text.Truncate(
                        Rules.ReadOnly
                            ? "outfit-rules.json could not be read, so rules will not be saved."
                            : "Off. Outfits only go on when you put them on.",
                        MathF.Max(Sp.Xxxl, right - toggle - Sp.Xl - origin.X)));
            }

            Unfade();
        }

        drawList.PopClipRect();


        if (open > 0.92f && Rules.Rules.Count > 0)
        {
            var orderWidth = EchoButton.ContentSize("Order", plugin.Fonts.Icon, FontAwesomeIcon.SortAmountDown).X;

            ImGui.SetCursorScreenPos(
                new Vector2(right - orderWidth, origin.Y + row - pad - Metrics.Row));

            if (EchoButton.Draw(
                    "##outfitsorder", "Order", new Vector2(orderWidth, Metrics.Row),
                    iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.SortAmountDown,
                    tooltip: "Which rule wins when more than one matches at once, and what you go "
                             + "back to when none of them do."))
            {
                orderOpen = true;
            }

            ImGui.SetCursorScreenPos(cursor);
        }

        var ruleY = origin.Y + row + Sp.Md;

        drawList.AddRectFilled(
            new Vector2(origin.X, ruleY), new Vector2(origin.X + width, ruleY + Stroke.Hair),
            Col.U32(Col.EdgeRest with { W = 0.55f * (1f - open) }));

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, row));

        return row + Sp.Md + Stroke.Hair + Sp.Lg;
    }

    /// Scales everything drawn through Col.U32 until Unfade.
    private static void Fade(float alpha) =>
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * Math.Clamp(alpha, 0f, 1f));

    private static void Unfade() => ImGui.PopStyleVar();


    /// One box to name a look, one button to keep it, and the two marks that move one between here and
    /// Glamourer.
    private float DrawSaveBand(float width)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var icons = plugin.Fonts.Icon;

        var importing = pendingImport is not null;
        var trimmed = outfitName.Trim();
        var replacing = trimmed.Length > 0 && Store.Exists(trimmed);

        var mark = Metrics.TouchMin;
        var head = MathF.Max(Field.LabelHeight, mark);
        var marks = (mark * 2f) + Sp.Xs;

        var label = Store.ReadOnly
            ? "OUTFITS CANNOT BE SAVED"
            : importing
                ? "FROM GLAMOURER"
                : Wardrobe.Active
                    ? "WHAT YOU ARE WEARING"
                    : "NOTHING ON TO SAVE";

        using (Text.PushCaption())
        {
            Text.Tracked(
                drawList, new Vector2(origin.X, origin.Y + ((head - Text.CaptionSize) / 2f)),
                label,
                Col.U32(Store.ReadOnly ? Col.Danger : importing ? Theme.Accent : Col.TextMuted),
                1.8f,
                MathF.Max(Sp.Xxxl, width - marks - Sp.Xl));
        }

        var cursor = ImGui.GetCursorScreenPos();

        ImGui.SetCursorScreenPos(new Vector2(origin.X + width - marks, origin.Y));

        if (EchoButton.BareIcon(
                "##glampaste", icons, FontAwesomeIcon.Paste, mark,
                Items.Ready
                    ? "Paste a Glamourer design.\n\nReads whatever is on your clipboard and shows you "
                      + "what it found, so a mis-paste costs nothing."
                    : "The item list is still loading.",
                colourOverride: Items.Ready && !Store.ReadOnly
                    ? Col.TextDim
                    : Col.Disabled(Col.TextMuted)))
        {
            if (Items.Ready && !Store.ReadOnly)
                PasteGlamourerDesign();
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X + width - mark, origin.Y));

        if (EchoButton.BareIcon(
                "##glamcopy", icons, FontAwesomeIcon.Copy, mark,
                Wardrobe.Active
                    ? "Copy for Glamourer.\n\nPuts what you are wearing on the clipboard as a design."
                    : "There is nothing on to copy yet.",
                colourOverride: Wardrobe.Active ? Col.TextDim : Col.Disabled(Col.TextMuted)))
        {
            if (Wardrobe.Active)
                CopyAsGlamourerDesign();
        }

        ImGui.SetCursorScreenPos(cursor);


        var saveLabel = replacing ? "Replace" : "Save";
        var saveIcon = replacing ? FontAwesomeIcon.Sync : FontAwesomeIcon.Save;

        var saveWidth = EchoButton.ContentSize(saveLabel, icons, saveIcon).X;

        var pieces = pendingImport?.Pieces ?? 0;

        var ready = trimmed.Length > 0
                    && !Store.ReadOnly
                    && (importing ? pieces > 0 : Wardrobe.Active);

        var rowY = origin.Y + head + Sp.Sm;
        var fieldWidth = MathF.Max(Sp.Xxxl, width - saveWidth - Sp.Md);

        ImGui.SetCursorScreenPos(new Vector2(origin.X, rowY));

        Field.PushStyle();
        ImGui.SetNextItemWidth(fieldWidth);
        ImGui.InputTextWithHint("##outfitname", "Name this outfit...", ref outfitName, 60);

        var fieldHeight = ImGui.GetItemRectSize().Y;

        Field.PopStyle();

        ImGui.SetCursorScreenPos(
            new Vector2(origin.X + width - saveWidth, rowY + ((fieldHeight - Metrics.Row) / 2f)));

        if (EchoButton.Draw(
                "##outfitsave", saveLabel, new Vector2(saveWidth, Metrics.Row),
                iconFont: icons, icon: saveIcon, enabled: ready, primary: ready,
                tooltip: Store.ReadOnly
                    ? "outfits.json could not be read, so nothing will be saved."
                    : importing && pieces == 0
                        ? "No gear in that design could be matched, so there is nothing to keep."
                        : !importing && !Wardrobe.Active
                            ? "There is nothing on to save yet."
                            : replacing
                                ? "An outfit with this name already exists and will be overwritten."
                                : null))
        {
            Keep(trimmed);
        }

        var bottom = rowY + fieldHeight;

        bottom += DrawSaveDetail(drawList, origin, width, bottom);
        bottom += DrawMessage(drawList, origin, width, bottom);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, bottom - origin.Y));

        return bottom - origin.Y + Sp.Xl;
    }

    /// What Save is about to write, and the one choice about it.
    private float DrawSaveDetail(ImDrawListPtr drawList, Vector2 origin, float width, float top)
    {
        var importing = pendingImport is not null;
        var naming = outfitName.Trim().Length > 0;
        var wants = importing || (naming && Wardrobe.Active);

        var shown = Motion.Toward("outfitsdetail", wants, 16f);

        if (shown <= 0.002f)
            return 0f;

        var line = Text.CaptionSize * 1.45f;
        var mark = Metrics.TouchMin;

        var lines = new List<(string Text, Vector4 Colour)>();

        if (pendingImport is { } look)
        {
            var emptied = look.Look.Count - look.Pieces;

            lines.Add((
                look.Pieces == 0
                    ? "No gear in this design could be matched."
                    : look.Appearance is not null
                        ? $"{look.Pieces} pieces, with appearance."
                        : $"{look.Pieces} pieces.",
                look.Pieces == 0 ? Col.Danger : Col.TextDim));

            if (emptied > 0)
                lines.Add(($"{emptied} empty slots. Wearing it takes those off.", Col.TextMuted));

            foreach (var note in look.Notes)
                lines.Add((note, Col.TextMuted));
        }
        else
        {
            var slots = Wardrobe.Current.Count;

            lines.Add((
                saveAppearance && AppearanceToSave() is not null
                    ? $"{slots} slot{(slots == 1 ? "" : "s")}, with your face, hair and colours."
                    : $"{slots} slot{(slots == 1 ? "" : "s")}. Your own appearance is left alone.",
                Col.TextDim));
        }

        var rowHeight = importing ? mark : MathF.Max(mark, EchoToggle.Height);
        var full = Sp.Sm + (lines.Count * line) + Sp.Xs + rowHeight;
        var height = full * shown;
        var y = top + (Sp.Sm * shown);

        drawList.PushClipRect(
            new Vector2(origin.X, top), new Vector2(origin.X + width, top + height), true);

        using (Text.PushCaption())
        {
            for (var i = 0; i < lines.Count; i++)
            {
                var (text, colour) = lines[i];

                drawList.AddText(
                    new Vector2(origin.X, y + (i * line)),
                    Col.U32(colour with { W = colour.W * shown }),
                    Text.Truncate(text, width - mark - Sp.Md));
            }
        }

        drawList.PopClipRect();

        if (shown < 0.92f)
            return height;

        var controlY = y + (lines.Count * line) + Sp.Xs;
        var cursor = ImGui.GetCursorScreenPos();

        if (pendingImport is not null)
        {
            ImGui.SetCursorScreenPos(new Vector2(origin.X, controlY));

            if (EchoButton.Draw(
                    "##glamdiscard", "Discard", new Vector2(0f, mark),
                    iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Times,
                    tooltip: "Forget the pasted design and go back to saving what you are wearing."))
            {
                pendingImport = null;
            }
        }
        else
        {
            var want = saveAppearance;

            ImGui.SetCursorScreenPos(new Vector2(origin.X, controlY));

            if (EchoToggle.Draw(
                    "##saveappearance", "Include Appearance", ref want,
                    "Saves your face, hair and colours with the gear. Off means the outfit only "
                    + "changes what you are wearing and leaves your character's own look alone."))
            {
                saveAppearance = want;
            }
        }

        ImGui.SetCursorScreenPos(cursor);

        return height;
    }

    /// The last thing this pane said, with a way to put it away.
    private float DrawMessage(ImDrawListPtr drawList, Vector2 origin, float width, float top)
    {
        if (notice.Any)
            held = notice;

        var mark = Metrics.TouchMin;
        var full = Sp.Sm + mark;
        var shown = Motion.Toward("outfitsmessage", notice.Any, 16f);

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
                "##dropoutfitmessage", plugin.Fonts.Icon, FontAwesomeIcon.Times, mark,
                "Put this away.", colourOverride: Col.TextMuted))
        {
            notice = Message.None;
        }

        ImGui.SetCursorScreenPos(cursor);

        return height;
    }

    /// The appearance a save would carry, or null when there is nothing to read one from.
    private CustomizeSet? AppearanceToSave() =>
        appearance.Draft ?? Wardrobe.CurrentAppearance ?? Wardrobe.BaseAppearance;

    /// Writes what the box is naming, from wherever it came.
    private void Keep(string name)
    {
        if (name.Length == 0)
            return;

        if (pendingImport is { } look)
        {
            Store.Save(name, look.Look, look.Appearance);
            pendingImport = null;
            outfitName = string.Empty;
            notice = Message.Ok($"Saved \"{name}\" from Glamourer.");
            return;
        }

        Store.Save(name, Wardrobe.Current, saveAppearance ? AppearanceToSave() : null);
        outfitName = string.Empty;
    }

    /// Reads whatever is on the clipboard as a Glamourer design.
    private void PasteGlamourerDesign()
    {
        notice = Message.None;

        string clipboard;

        try
        {
            clipboard = ImGui.GetClipboardText();
        }
        catch (Exception)
        {
            notice = Message.Failed("The clipboard could not be read. Try again.");
            return;
        }

        var look = GlamourerDesign.Read(clipboard, Items, AppearanceToSave(), out var error);

        if (look is null)
        {
            notice = Message.Failed(error);
            return;
        }

        pendingImport = look;
        outfitName = look.Name;
    }

    /// Puts what the character is wearing on the clipboard as a Glamourer design.
    private void CopyAsGlamourerDesign()
    {
        notice = Message.None;

        var set = appearance.Draft ?? Wardrobe.CurrentAppearance;

        try
        {
            ImGui.SetClipboardText(GlamourerDesign.Write("EchoGlam Look", Wardrobe.Current, set));
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[EchoGlam] Could not write a Glamourer design to the clipboard");
            notice = Message.Failed("The clipboard could not be written to. Try again.");
            return;
        }

        notice = Message.Ok(
            set is not null
                ? "Copied, with appearance. Paste it into Glamourer."
                : "Copied. Paste it into Glamourer.");
    }


    /// A way to search the list, a way to order it, and how long it is.
    private float DrawListBand(float width, int visible)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();

        var row = MathF.Max(Metrics.Row, Metrics.Field);
        var x = origin.X;

        var searchable = Store.Count > SearchFrom;

        if (searchable)
        {
            var searchWidth = SearchDesign * UiHelpers.Scale;

            ImGui.SetCursorScreenPos(new Vector2(x, origin.Y + ((row - Metrics.Field) / 2f)));

            UiHelpers.SearchField(
                "##outfitsearch", "Search Outfits", ref outfitSearch, searchWidth,
                Metrics.Field, plugin.Fonts.Icon);

            x += searchWidth + Sp.Xxl;
        }

        if (Store.Count > 1)
        {
            ReadOnlySpan<string> sorts = ["A-Z", "Newest"];

            ImGui.SetCursorScreenPos(new Vector2(x, origin.Y));

            outfitsByDate = EchoSegment.Draw(
                "##outfitsort", sorts, outfitsByDate ? 1 : 0, row,
                ["Sorted by name.", "Sorted by when they were saved."]) == 1;
        }

        var total = Store.Count;

        var count = visible == total
            ? $"{total} {(total == 1 ? "Outfit" : "Outfits")}"
            : $"{visible} of {total}";

        using (Text.PushCaption())
        {
            drawList.AddText(
                new Vector2(
                    origin.X + width - Text.Width(count),
                    origin.Y + ((row - Text.CaptionSize) / 2f)),
                Col.U32(Col.TextMuted), count);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, row));

        return row + Sp.Md;
    }

    /// What the list is showing this frame, filtered and ordered.
    private List<Outfit> Listed()
    {
        var all = Store.All.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(outfitSearch))
            all = all.Where(o => o.Name.Contains(outfitSearch, StringComparison.OrdinalIgnoreCase));

        return outfitsByDate
            ? [.. all.OrderByDescending(o => o.SavedAtUtc)]
            : [.. all];
    }

    /// The saved outfits, in a recess of their own.
    private void DrawRows(List<Outfit> list, float width, float height)
    {
        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = new Vector2(width, height);

        Surface.Paint(min, size, SurfaceKind.Well);

        var pad = new Vector2(Sp.Lg, Sp.Lg);

        ImGui.SetCursorScreenPos(min + pad);

        if (list.Count == 0)
        {
            using (Text.PushCaption())
            {
                drawList.AddText(
                    min + pad, Col.U32(Col.TextMuted),
                    Text.Truncate("No outfit matches that.", width - (pad.X * 2f)));
            }

            ImGui.SetCursorScreenPos(min);
            ImGui.Dummy(size);
            return;
        }

        if (ImGui.BeginChild(
                "##outfitlist", size - (pad * 2f), false, ImGuiWindowFlags.AlwaysVerticalScrollbar))
        {
            var rowWidth = ImGui.GetContentRegionAvail().X;
            var top = ImGui.GetCursorScreenPos();

            for (var i = 0; i < list.Count; i++)
            {
                ImGui.SetCursorScreenPos(new Vector2(top.X, top.Y + (i * (Row.Height + Sp.Xs))));
                DrawOutfitRow(list[i], rowWidth);
            }
        }

        ImGui.EndChild();

        ImGui.SetCursorScreenPos(min);
        ImGui.Dummy(size);
    }

    /// One saved outfit: what it looks like, what it is, and the ways to act on it.
    private void DrawOutfitRow(Outfit outfit, float width)
    {
        var drawList = ImGui.GetWindowDrawList();
        var icons = plugin.Fonts.Icon;

        var armed = pendingDelete == outfit.Name;
        var ruleCount = Rules.CountFor(outfit.Name);

        var worn = Rules.Enabled
                   && !plugin.Automation.Surrendered
                   && string.Equals(plugin.Automation.Claimed, outfit.Name, StringComparison.OrdinalIgnoreCase);

        var row = Row.Draw(
            $"##outfit{outfit.Name}", width, Row.Height,
            accent: worn && !armed ? Theme.Accent : null,
            tone: armed ? Col.Danger : null);

        var after = ImGui.GetCursorScreenPos();

        if (!armed && row.RightClicked)
        {
            rulesFor = outfit.Name;
            pendingDelete = null;
        }


        var gear = GearDesign * UiHelpers.Scale;
        var strip = (GearShown * gear) + ((GearShown - 1) * Sp.Xxs);
        var gearY = row.CentreY(gear);
        var drawn = 0;

        foreach (var slot in GearOrder)
        {
            if (drawn >= GearShown)
                break;

            uint itemId = 0;

            foreach (var piece in outfit.Slots)
            {
                if (piece.Slot != slot)
                    continue;

                itemId = piece.ItemId;
                break;
            }

            if (itemId == 0)
                continue;

            ItemIcon.Draw(
                drawList, Items.Resolve(itemId),
                new Vector2(row.TextLeft + (drawn * (gear + Sp.Xxs)), gearY), gear);

            drawn++;
        }

        for (; drawn < GearShown; drawn++)
        {
            ItemIcon.Draw(
                drawList, GlamItem.None,
                new Vector2(row.TextLeft + (drawn * (gear + Sp.Xxs)), gearY), gear, 0.4f);
        }


        var mark = Metrics.TouchMin;
        var x = row.ContentRight;
        var overControl = false;

        if (armed)
        {
            x -= mark;
            ImGui.SetCursorScreenPos(new Vector2(x, row.CentreY(mark)));

            if (EchoButton.BareIcon(
                    $"##keep{outfit.Name}", icons, FontAwesomeIcon.Times, mark, "Keep it."))
                pendingDelete = null;

            overControl |= ImGui.IsItemHovered();

            x -= Sp.Sm + mark;
            ImGui.SetCursorScreenPos(new Vector2(x, row.CentreY(mark)));

            if (EchoButton.BareIcon(
                    $"##gone{outfit.Name}", icons, FontAwesomeIcon.Check, mark,
                    "Delete it for good.", colourOverride: Col.Danger))
            {
                Store.Delete(outfit.Name);

                Rules.Forget(outfit.Name);
                pendingDelete = null;
            }

            overControl |= ImGui.IsItemHovered();
        }
        else
        {
            var wearWidth = EchoButton.ContentSize("Wear").X;

            x -= wearWidth;
            ImGui.SetCursorScreenPos(new Vector2(x, row.CentreY(Metrics.Row)));

            if (EchoButton.Draw(
                    $"##wear{outfit.Name}", "Wear", new Vector2(wearWidth, Metrics.Row),
                    tooltip: "Put this outfit on.", primary: row.Hovered))
                Wear(outfit);

            overControl |= ImGui.IsItemHovered();

            x -= Sp.Md + mark;

            if (row.Hovered)
            {
                ImGui.SetCursorScreenPos(new Vector2(x, row.CentreY(mark)));

                if (EchoButton.BareIcon(
                        $"##cond{outfit.Name}", icons, FontAwesomeIcon.SlidersH, mark,
                        "When this wears itself, and what it plays on the way in."))
                    rulesFor = outfit.Name;

                overControl |= ImGui.IsItemHovered();
            }

            x -= Sp.Xs + mark;

            if (row.Hovered)
            {
                ImGui.SetCursorScreenPos(new Vector2(x, row.CentreY(mark)));

                if (EchoButton.BareIcon(
                        $"##del{outfit.Name}", icons, FontAwesomeIcon.TrashAlt, mark,
                        "Delete this outfit.", colourOverride: Col.Danger))
                    pendingDelete = outfit.Name;

                overControl |= ImGui.IsItemHovered();
            }
        }


        var textLeft = row.TextLeft + strip + Sp.Lg;
        var room = MathF.Max(Sp.Xxl, x - Sp.Md - textLeft);

        if (armed)
        {
            var top = Row.PrimaryTop(row);

            drawList.AddText(
                new Vector2(textLeft, top), Col.U32(Col.Danger),
                Text.Truncate($"Delete \"{outfit.Name}\"?", room));

            using (Text.PushCaption())
            {
                drawList.AddText(
                    new Vector2(textLeft, top + ImGui.GetTextLineHeight() + Sp.Xxs),
                    Col.U32(Col.Danger with { W = 0.75f }),
                    Text.Truncate("This cannot be undone", room));
            }
        }
        else
        {
            if (worn)
            {
                var pill = Pill.Measure("WORN");

                if (room > pill + Sp.Xxl)
                {
                    room -= pill + Sp.Md;

                    Pill.Paint(
                        drawList,
                        new Vector2(textLeft + room + Sp.Md, row.CentreY(Pill.Height)),
                        "WORN", Pill.Tone.Accent);
                }
            }

            Row.TwoLines(row, textLeft, room, outfit.Name, Summarise(outfit, ruleCount));
        }

        if (row.Hovered && !overControl && !armed)
        {
            UiHelpers.WrappedTooltip(
                "Right-click to set when this wears itself.",
                $"Saved {outfit.SavedAtUtc.ToLocalTime():d MMM yyyy, HH:mm}"
                + (worn ? "\nA rule is wearing it now" : string.Empty));
        }

        ImGui.SetCursorScreenPos(after);
    }

    /// The second line of an outfit's row: what it is, in words.
    private static string Summarise(Outfit outfit, int ruleCount)
    {
        var parts = new List<string>
        {
            $"{outfit.Slots.Count} slot{(outfit.Slots.Count == 1 ? "" : "s")}",
        };

        if (outfit.HasAppearance)
            parts.Add("with appearance");

        if (ruleCount > 0)
            parts.Add($"{ruleCount} condition{(ruleCount == 1 ? "" : "s")}");

        if (outfit.HasAnimation)
            parts.Add(outfit.AnimationName ?? "plays an animation");

        return string.Join("  ·  ", parts);
    }


    private void DrawConditions(string name)
    {
        var width = ImGui.GetContentRegionAvail().X - EchoButton.GlowReach;
        var origin = ImGui.GetCursorScreenPos();

        if (SubHeader(name, width))
        {
            rulesFor = null;
            return;
        }

        var outfit = Store.All.FirstOrDefault(o =>
            string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));

        if (outfit is null)
        {
            rulesFor = null;
            return;
        }

        var drawList = ImGui.GetWindowDrawList();
        var y = ImGui.GetCursorScreenPos().Y;


        y += Section(drawList, new Vector2(origin.X, y), "When To Wear It", width);

        if (!Rules.Enabled)
        {
            using (Text.PushCaption())
            {
                drawList.AddText(
                    new Vector2(origin.X, y), Col.U32(Col.Caution),
                    Text.Truncate("Automatic outfits are switched off, so none of these fire yet.", width));
            }

            y += Text.CaptionSize * 1.45f;
        }

        var mine = Rules.For(name).ToList();

        if (mine.Count == 0)
        {
            using (Text.PushCaption())
            {
                drawList.AddText(
                    new Vector2(origin.X, y), Col.U32(Col.TextMuted),
                    Text.Truncate("None yet. This outfit only goes on when you put it on.", width));
            }

            y += Text.CaptionSize * 1.45f;
        }
        else
        {
            var changed = false;

            foreach (var rule in mine)
            {
                ImGui.SetCursorScreenPos(new Vector2(origin.X, y));

                if (DrawRuleRow(rule, Rules.IndexOf(rule), width, showOutfit: false, showOrder: false))
                {
                    changed = true;
                    break;
                }

                y += Row.Height + Sp.Xs;
            }

            if (!changed && mine.Count > 1)
            {
                using (Text.PushCaption())
                {
                    drawList.AddText(
                        new Vector2(origin.X, y + Sp.Xs), Col.U32(Col.TextMuted),
                        Text.Truncate("When more than one matches, the highest priority wins.", width));
                }

                y += Sp.Xs + (Text.CaptionSize * 1.45f);
            }
        }

        y += Sp.Md;

        var gap = Sp.Sm;
        var each = MathF.Max(Sp.Xxxl, (width - (gap * 2f)) / 3f);
        var blocked = Rules.ReadOnly;

        ImGui.SetCursorScreenPos(new Vector2(origin.X, y));

        if (EchoButton.Draw(
                "##addzone", "Map Or Duty", new Vector2(each, Metrics.RowTall),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.MapMarkerAlt,
                enabled: plugin.Zones.Ready && !blocked,
                tooltip: plugin.Zones.Ready
                    ? "One place you pick - a city, a zone, a particular dungeon, or your own house."
                    : "The game's map list could not be read."))
        {
            var rule = new OutfitRule { Outfit = name, Trigger = OutfitTrigger.Zone };
            Rules.Add(rule);
            pickingPlaceFor = rule;
            zoneSearch = string.Empty;
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X + each + gap, y));

        if (EchoButton.Draw(
                "##addduty", "Any Duty", new Vector2(each, Metrics.RowTall),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Dungeon,
                enabled: !blocked,
                tooltip: "Any instanced content at all, rather than one you name."))
        {
            Rules.Add(new OutfitRule { Outfit = name, Trigger = OutfitTrigger.Duty });
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X + ((each + gap) * 2f), y));

        if (EchoButton.Draw(
                "##addcombat", "In Combat", new Vector2(each, Metrics.RowTall),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.FistRaised,
                enabled: !blocked,
                tooltip: "While you are fighting, anywhere. It waits a few seconds before changing "
                         + "back, so it cannot swap between two pulls."))
        {
            Rules.Add(new OutfitRule { Outfit = name, Trigger = OutfitTrigger.Combat });
        }

        y += Metrics.RowTall + Sp.Xxl;


        ImGui.SetCursorScreenPos(new Vector2(origin.X, y));
        DrawAnimation(outfit, origin.X, y, width);
    }

    /// The animation played when this outfit goes on and comes back off.
    private void DrawAnimation(Outfit outfit, float left, float top, float width)
    {
        var drawList = ImGui.GetWindowDrawList();
        var name = outfit.Name;
        var y = top;

        y += Section(drawList, new Vector2(left, y), "Animation", width);

        var label = outfit.HasAnimation
            ? outfit.AnimationName ?? $"Animation {outfit.AnimationId}"
            : "Nothing";

        ImGui.SetCursorScreenPos(new Vector2(left, y));

        if (EchoButton.Draw(
                "##pickanim", label, new Vector2(0f, Metrics.Row),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Running,
                selected: outfit.HasAnimation,
                enabled: plugin.Animations.Ready && !Store.ReadOnly,
                tooltip: plugin.Animations.Ready
                    ? "Plays once, every time this outfit goes on. Right-click to clear it."
                    : "The game's animation list could not be read."))
        {
            pickingAnimationFor = name;
            animationSearch = string.Empty;
        }

        if (EchoButton.RightClicked())
            Store.SetAnimation(name, 0, null);

        if (outfit.HasAnimation)
        {
            ImGui.SameLine(0f, Sp.Sm);

            if (EchoButton.Draw(
                    "##testanim", "Test", new Vector2(0f, Metrics.Row),
                    iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Play,
                    tooltip: "Plays it now, without changing what you are wearing."))
            {
                plugin.Animations.Request(outfit.AnimationId);
            }
        }

        y += Metrics.Row + Sp.Lg;

        if (outfit.HasAnimation)
        {
            var first = outfit.ChangeAfterAnimation;

            ImGui.SetCursorScreenPos(new Vector2(left, y));

            if (EchoToggle.Draw(
                    "##animatefirst", "Change After The Animation", ref first,
                    "On, the animation plays and you are already wearing the outfit when it ends - a "
                    + "transformation. Off, the outfit changes straight away and the animation follows "
                    + "it. On delays the change by however long the animation runs."
                    + "\n\nEither way it plays again when a rule takes the outfit back off."))
            {
                Store.SetAnimateFirst(name, first);
            }

            y += MathF.Max(EchoToggle.Height, Metrics.TouchMin) + Sp.Lg;

            if (outfit.ChangeAfterAnimation)
            {
                var hold = outfit.HoldSeconds;

                using (Text.PushCaption())
                {
                    Text.Tracked(
                        drawList, new Vector2(left, y), "WAIT BEFORE CHANGING",
                        Col.U32(Col.TextMuted), 1.8f, width);

                    var reading = $"{hold:0.0}s";

                    drawList.AddText(
                        new Vector2(left + width - Text.Width(reading), y),
                        Col.U32(Theme.Accent), reading);
                }

                y += Field.LabelHeight;

                ImGui.SetCursorScreenPos(new Vector2(left, y));

                if (EchoSlider.Draw(
                        "##animhold", ref hold, 0.2f, 5f, width, "{0:0.0}s",
                        Outfit.DefaultHoldSeconds, [Outfit.DefaultHoldSeconds]))
                {
                    Store.SetAnimationHold(name, hold);
                }

                if (ImGui.IsItemHovered())
                {
                    UiHelpers.WrappedTooltip(
                        "How long the animation gets before the clothes change. It waits longer if "
                        + "the animation is still running, never less than this.");
                }

                y += Metrics.TouchMin + Sp.Lg;
            }
        }

        using (Text.PushCaption())
        {
            ImGui.SetCursorScreenPos(new Vector2(left, y));
            ImGui.PushStyleColor(ImGuiCol.Text, Col.TextMuted);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
            ImGui.TextUnformatted(
                "You and your EchoGlam friends see this, the same as the glamour itself. Nobody "
                + "else does. The game cuts it short if you are moving or fighting.");
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();
        }
    }


    /// The priority list, and the fallback as its last line.
    private void DrawOrder()
    {
        var width = ImGui.GetContentRegionAvail().X - EchoButton.GlowReach;
        var origin = ImGui.GetCursorScreenPos();

        if (SubHeader("Rule Order", width))
        {
            orderOpen = false;
            return;
        }

        var drawList = ImGui.GetWindowDrawList();
        var top = ImGui.GetCursorScreenPos().Y;
        var available = ImGui.GetContentRegionAvail().Y - EchoButton.GlowReach;

        using (Text.PushCaption())
        {
            drawList.AddText(
                new Vector2(origin.X, top), Col.U32(Col.TextDim),
                Text.Truncate(
                    "Tried from the top. The first one that matches is what you wear.", width));
        }

        var y = top + (Text.CaptionSize * 1.45f) + Sp.Lg;

        var fallbackHeight = Field.LabelHeight + Metrics.RowTall + Sp.Xs + (Text.CaptionSize * 1.45f);
        var listHeight = MathF.Max(Row.Height, available - (y - top) - Sp.Xxl - fallbackHeight);

        var rules = Rules.Rules.ToList();

        ImGui.SetCursorScreenPos(new Vector2(origin.X, y));

        if (rules.Count == 0)
        {
            using (Text.PushCaption())
            {
                drawList.AddText(
                    new Vector2(origin.X, y), Col.U32(Col.TextMuted),
                    Text.Truncate("No conditions on any outfit yet.", width));
            }

            y += Text.CaptionSize * 1.45f;
        }
        else
        {
            var min = new Vector2(origin.X, y);
            var size = new Vector2(width, listHeight);

            Surface.Paint(min, size, SurfaceKind.Well);

            var pad = new Vector2(Sp.Lg, Sp.Lg);

            ImGui.SetCursorScreenPos(min + pad);

            if (ImGui.BeginChild(
                    "##rulelist", size - (pad * 2f), false, ImGuiWindowFlags.AlwaysVerticalScrollbar))
            {
                var rowWidth = ImGui.GetContentRegionAvail().X;
                var listTop = ImGui.GetCursorScreenPos();

                for (var i = 0; i < rules.Count; i++)
                {
                    ImGui.SetCursorScreenPos(
                        new Vector2(listTop.X, listTop.Y + (i * (Row.Height + Sp.Xs))));

                    if (DrawRuleRow(rules[i], i, rowWidth, showOutfit: true, showOrder: true))
                        break;
                }
            }

            ImGui.EndChild();

            y += listHeight;
        }

        y += Sp.Xxl;


        y += Section(drawList, new Vector2(origin.X, y), "When Nothing Matches", width);

        var original = Rules.Original;

        ImGui.SetCursorScreenPos(new Vector2(origin.X, y));

        if (EchoButton.Draw(
                "##fallback", original ?? "What I Have On", new Vector2(0f, Metrics.RowTall),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Undo,
                selected: original is not null,
                tooltip: original is not null
                    ? $"Goes back to \"{original}\" when no rule matches. Right-click to clear it."
                    : "Goes back to the look you had on before a rule dressed you. Click to pick an "
                      + "outfit instead."))
        {
            fallbackSearch = string.Empty;
            ImGui.OpenPopup(FallbackPopupId);
        }

        if (EchoButton.RightClicked())
            Rules.Original = null;

        DrawFallbackPopup();

        using (Text.PushCaption())
        {
            drawList.AddText(
                new Vector2(origin.X, y + Metrics.RowTall + Sp.Xs), Col.U32(Col.TextMuted),
                Text.Truncate(
                    original is not null
                        ? "Not a rule: it has no condition and never competes for priority."
                        : "Takes the glamour off and gives you your own gear back.",
                    width));
        }
    }

    private void DrawFallbackPopup()
    {
        if (!ImGui.BeginPopup(FallbackPopupId))
            return;

        var width = FallbackDesign * UiHelpers.Scale;
        var all = Store.All;

        if (PickRow(
                "##fallbacknone", width, Metrics.RowTall, "What I Have On", null,
                Rules.Original is null,
                "Goes back to the look you had on before a rule dressed you."))
        {
            Rules.Original = null;
            ImGui.CloseCurrentPopup();
        }

        if (all.Count == 0)
        {
            ImGui.EndPopup();
            return;
        }

        ImGui.Dummy(new Vector2(width, Sp.Sm));

        if (all.Count > FallbackSearchFrom)
        {
            UiHelpers.SearchField(
                "##fallbacksearch", "Search Outfits", ref fallbackSearch, width,
                Metrics.Field, plugin.Fonts.Icon);
        }
        else if (fallbackSearch.Length > 0)
        {
            fallbackSearch = string.Empty;
        }

        var matches = string.IsNullOrWhiteSpace(fallbackSearch)
            ? all
            : [.. all.Where(o => o.Name.Contains(fallbackSearch, StringComparison.OrdinalIgnoreCase))];

        if (matches.Count == 0)
        {
            using (Text.PushCaption())
                ImGui.TextColored(Col.TextMuted, "Nothing matches that.");

            ImGui.EndPopup();
            return;
        }

        var rows = Math.Min(matches.Count, FallbackRows);
        var listHeight = (rows * (Metrics.RowTall + Sp.Xs)) - Sp.Xs;

        var scrolls = matches.Count > FallbackRows;
        var childWidth = width + (scrolls ? ImGui.GetStyle().ScrollbarSize : 0f);

        if (ImGui.BeginChild("##fallbacklist", new Vector2(childWidth, listHeight), false))
        {
            var top = ImGui.GetCursorScreenPos();

            for (var i = 0; i < matches.Count; i++)
            {
                ImGui.SetCursorScreenPos(
                    new Vector2(top.X, top.Y + (i * (Metrics.RowTall + Sp.Xs))));

                if (!PickRow(
                        $"##fallback{matches[i].Name}", width, Metrics.RowTall, matches[i].Name, null,
                        string.Equals(Rules.Original, matches[i].Name, StringComparison.OrdinalIgnoreCase),
                        null))
                    continue;

                Rules.Original = matches[i].Name;
                ImGui.CloseCurrentPopup();
                break;
            }
        }

        ImGui.EndChild();
        ImGui.EndPopup();
    }

    /// One condition.
    private bool DrawRuleRow(OutfitRule rule, int id, float width, bool showOutfit, bool showOrder)
    {
        var drawList = ImGui.GetWindowDrawList();
        var icons = plugin.Fonts.Icon;

        var active = ReferenceEquals(plugin.Automation.ActiveRule, rule) && Rules.Enabled;

        var row = Row.Draw(
            $"##rulerow{id}", width, Row.Height, accent: active ? Theme.Accent : null);

        var after = ImGui.GetCursorScreenPos();

        var mark = Metrics.TouchMin;
        var x = row.ContentRight;

        x -= mark;

        if (row.Hovered)
        {
            ImGui.SetCursorScreenPos(new Vector2(x, row.CentreY(mark)));

            if (EchoButton.BareIcon(
                    $"##ruledel{id}", icons, FontAwesomeIcon.TrashAlt, mark,
                    "Remove this condition.", colourOverride: Col.Danger))
            {
                Rules.Remove(rule);
                ImGui.SetCursorScreenPos(after);
                return true;
            }
        }

        x -= Sp.Xs + mark;
        ImGui.SetCursorScreenPos(new Vector2(x, row.CentreY(mark)));

        if (EchoButton.BareIcon(
                $"##ruleon{id}", icons,
                rule.Enabled ? FontAwesomeIcon.ToggleOn : FontAwesomeIcon.ToggleOff, mark,
                rule.Enabled ? "On. Click to stop it firing." : "Off. Click to switch it back on.",
                colourOverride: rule.Enabled ? Theme.Accent : Col.TextMuted))
        {
            Rules.Toggle(rule, !rule.Enabled);
        }

        if (showOrder)
        {
            x -= Sp.Xs + mark;
            ImGui.SetCursorScreenPos(new Vector2(x, row.CentreY(mark)));

            if (EchoButton.BareIcon(
                    $"##ruledown{id}", icons, FontAwesomeIcon.ChevronDown, mark, "Lower priority."))
            {
                Rules.Move(rule, 1);
                ImGui.SetCursorScreenPos(after);
                return true;
            }

            x -= mark;
            ImGui.SetCursorScreenPos(new Vector2(x, row.CentreY(mark)));

            if (EchoButton.BareIcon(
                    $"##ruleup{id}", icons, FontAwesomeIcon.ChevronUp, mark, "Higher priority."))
            {
                Rules.Move(rule, -1);
                ImGui.SetCursorScreenPos(after);
                return true;
            }
        }

        if (rule.Trigger == OutfitTrigger.Zone)
        {
            var changeWidth = EchoButton.ContentSize("Change").X;

            x -= Sp.Md + changeWidth;
            ImGui.SetCursorScreenPos(new Vector2(x, row.CentreY(Metrics.Row)));

            if (EchoButton.Draw(
                    $"##rulezone{id}", "Change", new Vector2(changeWidth, Metrics.Row),
                    tooltip: "Pick a different map or duty."))
            {
                pickingPlaceFor = rule;
                zoneSearch = string.Empty;
                ImGui.SetCursorScreenPos(after);
                return true;
            }
        }

        var plate = Metrics.TouchMin + Sp.Xs;
        var plateMin = new Vector2(row.TextLeft, row.CentreY(plate));
        var tint = rule.Enabled ? Theme.Accent : Col.TextMuted;

        drawList.AddRectFilled(
            plateMin, plateMin + new Vector2(plate, plate), Col.U32(tint with { W = 0.14f }), Rad.Ctl);

        using (Text.PushIcon())
        {
            UiHelpers.DrawScaledIcon(
                drawList,
                rule.Trigger switch
                {
                    OutfitTrigger.Zone => rule.HouseId != 0 ? FontAwesomeIcon.Home : FontAwesomeIcon.MapMarkerAlt,
                    OutfitTrigger.Duty => FontAwesomeIcon.Dungeon,
                    _ => FontAwesomeIcon.FistRaised,
                },
                plateMin + new Vector2(plate / 2f, plate / 2f),
                Col.U32(tint));
        }

        var textLeft = plateMin.X + plate + Sp.Lg;
        var (headline, detail) = Describe(rule);

        if (showOutfit)
            detail = $"{detail}  ·  {rule.Outfit}";

        if (!rule.Enabled)
            detail = $"{detail}  ·  off";

        Row.TwoLines(row, textLeft, MathF.Max(Sp.Xxl, x - Sp.Md - textLeft), headline, detail);

        ImGui.SetCursorScreenPos(after);
        return false;
    }

    /// A rule as two lines: where, and what kind of where.
    private (string Headline, string Detail) Describe(OutfitRule rule)
    {
        switch (rule.Trigger)
        {
            case OutfitTrigger.Zone when rule.TerritoryId == 0:
                return ("Nowhere picked yet", "Choose a map or duty");

            case OutfitTrigger.Zone:
                var place = plugin.Zones.NameFor(rule.TerritoryId);

                if (rule.HouseId != 0)
                    return (place, $"This house  ·  {rule.HouseAddress ?? "one address"}");

                return (place, plugin.Zones.IsDuty(rule.TerritoryId) ? "A duty" : "Anywhere on this map");

            case OutfitTrigger.Duty:
                return ("Any duty", "Dungeons, trials and raids");

            case OutfitTrigger.Combat:
                return ("In combat", "Anywhere, while you are fighting");

            default:
                return ("Unknown", string.Empty);
        }
    }


    /// Picking the one place a zone rule fires in.
    private void DrawZonePicker()
    {
        var rule = pickingPlaceFor!;
        var width = ImGui.GetContentRegionAvail().X - EchoButton.GlowReach;
        var origin = ImGui.GetCursorScreenPos();

        if (SubHeader("Pick A Place", width))
        {
            if (rule.TerritoryId == 0)
                Rules.Remove(rule);

            pickingPlaceFor = null;
            return;
        }

        var drawList = ImGui.GetWindowDrawList();
        var top = ImGui.GetCursorScreenPos().Y;

        ReadOnlySpan<string> scopes = ["Everywhere", "Duties", "Open World"];

        var row = MathF.Max(Metrics.Row, Metrics.Field);
        var scopeWidth = EchoSegment.Width(scopes);
        var searchWidth = MathF.Max(Sp.Xxxl, width - scopeWidth - Sp.Xxl);

        ImGui.SetCursorScreenPos(new Vector2(origin.X, top + ((row - Metrics.Field) / 2f)));

        UiHelpers.SearchField(
            "##zonesearch", "Search Maps And Duties", ref zoneSearch, searchWidth,
            Metrics.Field, plugin.Fonts.Icon);

        ImGui.SetCursorScreenPos(new Vector2(origin.X + searchWidth + Sp.Xxl, top));

        var scope = EchoSegment.Draw(
            "##zonescope", scopes,
            zoneDutyFilter switch { null => 0, true => 1, false => 2 }, row,
            ["Every map in the game.", "Instanced content only.", "Maps you can walk into."]);

        zoneDutyFilter = scope switch { 1 => true, 2 => false, _ => null };

        var y = top + row + Sp.Lg;

        var here = Plugin.ClientState.TerritoryType;

        if (here != 0)
        {
            y += Section(drawList, new Vector2(origin.X, y), "Where You Are", width);

            if (plugin.Housing.Current is { } house)
            {
                ImGui.SetCursorScreenPos(new Vector2(origin.X, y));

                if (PickRow(
                        "##zonehouse", width, Row.Height, "This House", house.Address,
                        selected: rule.HouseId != 0 && rule.HouseId == house.Id,
                        tooltip: "Only this address. Other houses of the same kind will not match.",
                        icon: FontAwesomeIcon.Home,
                        highlight: true))
                {
                    rule.TerritoryId = here;
                    rule.HouseId = house.Id;
                    rule.HouseAddress = house.Address;
                    Rules.Save();
                    pickingPlaceFor = null;
                    return;
                }

                y += Row.Height + Sp.Xs;
            }

            ImGui.SetCursorScreenPos(new Vector2(origin.X, y));

            if (PickRow(
                    "##zonehere", width, Row.Height,
                    plugin.Zones.NameFor(here),
                    plugin.Housing.Current is null
                        ? "The map you are standing in"
                        : "Every house of this kind, anyone's",
                    selected: rule.TerritoryId == here && rule.HouseId == 0,
                    tooltip: plugin.Housing.Current is null
                        ? "The map you are standing in."
                        : "Every house of this kind, anyone's - not just this address.",
                    icon: FontAwesomeIcon.MapPin,
                    highlight: plugin.Housing.Current is null))
            {
                rule.TerritoryId = here;
                rule.HouseId = 0;
                rule.HouseAddress = null;
                Rules.Save();
                pickingPlaceFor = null;
                return;
            }

            y += Row.Height + Sp.Xxl;
        }


        y += Section(drawList, new Vector2(origin.X, y), "Anywhere Else", width);

        var matches = ZoneMatches();
        Zone? picked = null;

        ImGui.SetCursorScreenPos(new Vector2(origin.X, y));

        Well(width, () => picked = Clipped(
            matches,
            (zone, rowWidth) => PickRow(
                $"##zone{zone.TerritoryId}", rowWidth, Metrics.RowTall, zone.Label,
                zone.IsDuty ? "A duty" : "Open world",
                selected: rule.TerritoryId == zone.TerritoryId && rule.HouseId == 0,
                tooltip: null)));

        if (picked is not { } chosen)
            return;

        rule.TerritoryId = chosen.TerritoryId;

        rule.HouseId = 0;
        rule.HouseAddress = null;

        Rules.Save();
        pickingPlaceFor = null;
    }

    /// Choosing the animation, from every emote and player action in the game.
    private void DrawAnimationPicker(string name)
    {
        var width = ImGui.GetContentRegionAvail().X - EchoButton.GlowReach;
        var origin = ImGui.GetCursorScreenPos();

        if (SubHeader($"Animation For \"{name}\"", width))
        {
            pickingAnimationFor = null;
            return;
        }

        var outfit = Store.All.FirstOrDefault(o =>
            string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));

        if (outfit is null)
        {
            pickingAnimationFor = null;
            return;
        }

        var drawList = ImGui.GetWindowDrawList();
        var top = ImGui.GetCursorScreenPos().Y;

        ReadOnlySpan<string> sources = ["Both", "Emotes", "Actions"];

        var row = MathF.Max(Metrics.Row, Metrics.Field);
        var sourceWidth = EchoSegment.Width(sources);
        var searchWidth = MathF.Max(Sp.Xxxl, width - sourceWidth - Sp.Xxl);

        ImGui.SetCursorScreenPos(new Vector2(origin.X, top + ((row - Metrics.Field) / 2f)));

        UiHelpers.SearchField(
            "##animsearch", "Search Emotes And Actions", ref animationSearch, searchWidth,
            Metrics.Field, plugin.Fonts.Icon);

        ImGui.SetCursorScreenPos(new Vector2(origin.X + searchWidth + Sp.Xxl, top));

        var source = EchoSegment.Draw(
            "##animsource", sources,
            animationFilter switch { null => 0, AnimationSource.Emote => 1, _ => 2 }, row,
            [
                "Emotes and combat animations together.",
                "The things people know by name - a bow, a pose, a dance.",
                "Every weaponskill, spell and ability.",
            ]);

        animationFilter = source switch
        {
            1 => AnimationSource.Emote,
            2 => AnimationSource.Action,
            _ => null,
        };

        var y = top + row + Sp.Lg;

        ImGui.SetCursorScreenPos(new Vector2(origin.X, y));

        if (PickRow(
                "##animnone", width, Metrics.RowTall, "Nothing", "The outfit goes on plainly",
                selected: !outfit.HasAnimation, tooltip: null))
        {
            Store.SetAnimation(name, 0, null);
            pickingAnimationFor = null;
            return;
        }

        y += Metrics.RowTall + Sp.Lg;

        var matches = AnimationMatches();
        AnimationEntry? picked = null;

        ImGui.SetCursorScreenPos(new Vector2(origin.X, y));

        Well(width, () => picked = Clipped(
            matches,
            (entry, rowWidth) => PickRow(
                $"##anim{entry.TimelineId}", rowWidth, Metrics.RowTall, entry.Name,
                entry.Source == AnimationSource.Emote ? "Emote" : "Action",
                selected: outfit.AnimationId == entry.TimelineId,
                tooltip: "Click to use it, and it plays once so you can see it.")));

        if (picked is not { } chosen)
            return;

        Store.SetAnimation(name, chosen.TimelineId, chosen.Name);

        plugin.Animations.Request(chosen.TimelineId);
    }

    /// A long list, with only the rows on screen actually drawn.
    private static T? Clipped<T>(IReadOnlyList<T> items, Func<T, float, bool> row)
        where T : struct
    {
        var width = ImGui.GetContentRegionAvail().X;

        if (items.Count == 0)
        {
            using (Text.PushCaption())
                ImGui.TextColored(Col.TextMuted, "Nothing matches that.");

            return null;
        }

        var step = Metrics.RowTall + Sp.Xs;

        T? picked = null;

        var clipper = new ImGuiListClipper();
        clipper.Begin(items.Count, step);

        while (clipper.Step())
        {
            for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                var at = ImGui.GetCursorScreenPos();

                if (row(items[i], width))
                    picked = items[i];

                ImGui.SetCursorScreenPos(new Vector2(at.X, at.Y + step));
            }
        }

        clipper.End();

        return picked;
    }


    /// A way out and the name of where you are.
    private bool SubHeader(string title, float width)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var row = Metrics.RowTall;

        var back = BackLink.Draw(drawList, plugin.Fonts.Icon, "##outfitsback", "Back", origin, row);

        var left = origin.X + BackLink.Width("Back") + Sp.Xxl;

        using (Text.PushTitle())
        {
            drawList.AddText(
                new Vector2(left, origin.Y + ((row - (Text.TitleSize * 1.2f)) / 2f)),
                Col.U32(Col.Text),
                Text.Truncate(title, MathF.Max(Sp.Xxxl, origin.X + width - left)));
        }

        var ruleY = origin.Y + row + Sp.Md;

        drawList.AddRectFilled(
            new Vector2(origin.X, ruleY), new Vector2(origin.X + width, ruleY + Stroke.Hair),
            Col.U32(Col.EdgeRest with { W = 0.55f }));

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, row + Sp.Md + Stroke.Hair + Sp.Lg));

        return back;
    }

    /// A section's name, as tracked capitals and nothing else.
    private static float Section(ImDrawListPtr drawList, Vector2 at, string label, float width)
    {
        using (Text.PushCaption())
            Text.Tracked(drawList, at, label.ToUpperInvariant(), Col.U32(Col.TextMuted), 1.8f, width);

        return (Text.CaptionSize * 1.15f) + Sp.Md;
    }

    /// A recess filling what is left of the page, with a scrolling list inside it.
    private static void Well(float width, Action contents)
    {
        var min = ImGui.GetCursorScreenPos();

        var height = MathF.Max(
            Row.Height, ImGui.GetContentRegionAvail().Y - EchoButton.GlowReach);

        var size = new Vector2(width, height);

        Surface.Paint(min, size, SurfaceKind.Well);

        var pad = new Vector2(Sp.Lg, Sp.Lg);

        ImGui.SetCursorScreenPos(min + pad);

        if (ImGui.BeginChild(
                "##well", size - (pad * 2f), false, ImGuiWindowFlags.AlwaysVerticalScrollbar))
        {
            contents();
        }

        ImGui.EndChild();

        ImGui.SetCursorScreenPos(min);
        ImGui.Dummy(size);
    }

    /// One row of a list you pick from.
    private bool PickRow(
        string id, float width, float height, string primary, string? secondary,
        bool selected, string? tooltip, FontAwesomeIcon? icon = null, bool highlight = false)
    {
        var drawList = ImGui.GetWindowDrawList();

        var row = Row.Draw(
            id, width, height, selected: selected,
            accent: highlight && !selected ? Theme.Accent : null);

        var after = ImGui.GetCursorScreenPos();

        if (row.Hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            if (!string.IsNullOrEmpty(tooltip))
                UiHelpers.WrappedTooltip(tooltip);
        }

        var left = row.TextLeft;

        if (icon is { } glyph)
        {
            using (Text.PushIcon())
            {
                UiHelpers.DrawScaledIcon(
                    drawList, glyph,
                    new Vector2(left + (Metrics.TouchMin / 2f), row.CentreY(0f)),
                    Col.U32(selected || highlight ? Theme.Accent : Col.TextDim));
            }

            left += Metrics.TouchMin + Sp.Md;
        }

        var room = MathF.Max(Sp.Xl, row.ContentRight - Metrics.TouchMin - Sp.Md - left);

        if (secondary is null)
        {
            drawList.AddText(
                new Vector2(left, row.CentreY(ImGui.GetTextLineHeight())),
                Col.U32(selected ? Theme.Accent : row.Hovered ? Col.Text : Col.TextDim),
                Text.Truncate(primary, room));
        }
        else
        {
            Row.TwoLines(row, left, room, primary, secondary);
        }

        if (selected)
        {
            using (Text.PushIcon())
            {
                UiHelpers.DrawScaledIcon(
                    drawList, FontAwesomeIcon.Check,
                    new Vector2(row.ContentRight - (Metrics.TouchMin / 2f), row.CentreY(0f)),
                    Col.U32(Theme.Accent));
            }
        }

        ImGui.SetCursorScreenPos(after);
        return row.Clicked;
    }


    private void Wear(Outfit outfit)
    {
        if (Build.Diagnostics)
        {
            Plugin.Log.Information(
                $"[EchoGlam] Wear \"{outfit.Name}\": animation={outfit.AnimationId} "
                + $"first={outfit.ChangeAfterAnimation} hold={outfit.HoldSeconds:0.0}s");
        }

        if (outfit.HasAnimation && outfit.ChangeAfterAnimation)
        {
            plugin.Animations.PlayThen(outfit.AnimationId, outfit.HoldSeconds, () => PutOn(outfit));
            return;
        }

        PutOn(outfit);
        plugin.Animations.Request(outfit.AnimationId);
    }

    private void PutOn(Outfit outfit)
    {
        Wardrobe.Wear(outfit.ToLook());

        if (outfit.ToAppearance() is { } set)
        {
            Wardrobe.SetAppearance(set);
            appearance.Invalidate();
        }
    }
}
