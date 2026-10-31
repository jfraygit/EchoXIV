using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using EchoGlam.Game;
using EchoGlam.UI.Controls;
using EchoGlam.UI.Foundation;

namespace EchoGlam.UI;

/// The wardrobe: a slot down the left, everything that fits it on the right.
public sealed class DressingRoomTab
{
    private readonly Plugin plugin;

    private GlamSlot selectedSlot = GlamSlot.Body;
    private string search = string.Empty;

    /// ClassJob row id the picker is filtered to, or null for everything.
    private uint? selectedJob;

    /// Whether the picker is narrowed to gear this character can get at.
    private bool ownedOnly;

    /// Which kinds of hard-to-get gear the picker is hiding.
    private bool hideStore;

    private bool hideSeasonal;
    private bool hideExclusive;

    /// This frame's item list, and how many the source filters took out of it.
    private List<GlamItem> matches = [];

    /// Everything the filter pass depends on, as one comparable value.
    private readonly record struct FilterKey(
        GlamSlot Slot, string Search, uint Job, bool Owned,
        bool HideStore, bool HideSeasonal, bool HideExclusive,
        int OwnedVersion, bool CatalogueReady);

    private FilterKey lastFilter;
    private bool filterValid;

    private FilterKey CurrentFilter => new(
        selectedSlot, search, selectedJob ?? 0u, ownedOnly,
        hideStore, hideSeasonal, hideExclusive,
        plugin.Owned.Version, Items.Ready);

    private void RefreshMatches()
    {
        var key = CurrentFilter;

        if (filterValid && key == lastFilter)
            return;

        lastFilter = key;
        filterValid = true;
        matches = Matches(out hiddenBySource);
    }

    private int hiddenBySource;

    /// The last dye picked in each channel, carried onto whatever goes on next.
    private byte stickyStain0;

    private byte stickyStain1;

    /// What dyes to put a piece on in, given what it can actually take.
    private (byte Stain0, byte Stain1) StainsFor(GlamItem item) =>
        (item.DyeChannels >= 1 ? stickyStain0 : (byte)0,
         item.DyeChannels >= 2 ? stickyStain1 : (byte)0);

    /// Which slot's dye row is open.
    private GlamSlot? dyeOpenFor;

    /// What the right-hand pane is showing.
    private enum RightPane
    {
        Items,
        Outfits,
        Appearance,
    }

    private RightPane pane = RightPane.Items;

    private readonly AppearanceEditor appearance;

    /// The outfits pane, which is its own screen and keeps its own state.
    private readonly OutfitsPane outfits;

    public DressingRoomTab(Plugin plugin)
    {
        this.plugin = plugin;
        appearance = new AppearanceEditor(plugin);
        outfits = new OutfitsPane(plugin, appearance);
    }

    private ItemCatalogue Items => plugin.Items;
    private DyeCatalogue Dyes => plugin.Dyes;
    private Wardrobe Wardrobe => plugin.Wardrobe;

    /// Width of the figure column, in design units.
    internal const float FigureDesign = 380f;

    /// Width of the figure once it is collapsed: one column of icons, wide enough to hit.
    internal const float RailDesign = 56f;

    /// How much narrower the window gets when the figure folds away.
    internal const float CollapseSaving = FigureDesign - RailDesign;

    /// Height of a slot tile, in design units.
    private const float SlotTileDesign = 52f;

    /// The icon in the block at the top of the bench, and how tall that block comes out.
    internal const float BannerIconDesign = 64f;

    internal static float BannerHeight => (BannerIconDesign * UiHelpers.Scale) + (Sp.Xl * 2f);

    /// An item cell: the icon box, and the row height including the name under it.
    private const float CellIconDesign = 46f;
    private const float CellDesign = 54f;
    private const float CellGapDesign = 6f;

    /// The figure's left-hand column: what a look is built around, top to bottom.
    private static readonly GlamSlot[] ArmourColumn =
    [
        GlamSlot.MainHand, GlamSlot.OffHand, GlamSlot.Head,
        GlamSlot.Body, GlamSlot.Hands, GlamSlot.Legs, GlamSlot.Feet,
    ];

    /// The figure's right-hand column.
    private static readonly GlamSlot[] AccessoryColumn =
    [
        GlamSlot.Ears, GlamSlot.Neck, GlamSlot.Wrists, GlamSlot.RingR, GlamSlot.RingL,
    ];

    /// Whether the figure is folded away to its rail.
    private bool Compact
    {
        get => plugin.Configuration.DressingRoomCompact;
        set
        {
            if (plugin.Configuration.DressingRoomCompact == value)
                return;

            plugin.Configuration.DressingRoomCompact = value;
            plugin.Configuration.Save();
        }
    }

    public void Draw()
    {
        plugin.EnsureCatalogues();

        if (!Items.Ready || !Dyes.Ready)
        {
            DrawBuilding();
            return;
        }

        var available = ImGui.GetContentRegionAvail();
        var scale = UiHelpers.Scale;

        var figureWidth = Compact
            ? RailDesign * scale
            : MathF.Min(FigureDesign * scale, available.X * 0.45f);

        var gap = Compact ? Sp.Lg : Sp.Xxl;
        var benchWidth = MathF.Max(260f * scale, available.X - figureWidth - gap);

        var origin = ImGui.GetCursorScreenPos();

        var rule = origin.X + figureWidth + (gap / 2f);

        ImGui.GetWindowDrawList().AddRectFilled(
            new Vector2(rule, origin.Y),
            new Vector2(rule + Stroke.Hair, origin.Y + available.Y),
            Col.U32(Col.EdgeRest with { W = 0.5f }));

        if (ImGui.BeginChild("##figure", new Vector2(figureWidth, available.Y), false))
        {
            if (Compact)
                DrawRail();
            else
                DrawFigure();
        }

        ImGui.EndChild();
        ImGui.SameLine(0f, gap);

        if (ImGui.BeginChild("##bench", new Vector2(benchWidth, available.Y), false))
        {
            switch (pane)
            {
                case RightPane.Outfits:
                    outfits.Draw();
                    break;

                case RightPane.Appearance:
                    appearance.Draw();
                    break;

                default:
                    DrawPicker();
                    break;
            }
        }

        ImGui.EndChild();
    }

    /// The character sheet: twelve slots as tiles, in the arrangement the game uses, plus the two
    /// destinations that are the same kind of choice.
    private void DrawFigure()
    {
        var drawList = ImGui.GetWindowDrawList();
        var avail = ImGui.GetContentRegionAvail();
        var origin = ImGui.GetCursorScreenPos();

        var column = (avail.X - Sp.Lg) / 2f;
        var right = origin.X + column + Sp.Lg;
        var gap = Sp.Sm;
        var tile = SlotTileDesign * UiHelpers.Scale;

        var headRow = Metrics.TouchMin;
        var captionH = headRow + Sp.Md;
        var captionY = origin.Y + ((headRow - (Text.CaptionSize * 1.15f)) / 2f);

        using (Text.PushCaption())
        {
            Text.Tracked(
                drawList, new Vector2(origin.X, captionY), "ARMOUR",
                Col.U32(Col.TextMuted), 1.8f, column);

            Text.Tracked(
                drawList, new Vector2(right, captionY), "ACCESSORIES",
                Col.U32(Col.TextMuted), 1.8f, column - headRow - Sp.Sm);
        }

        DrawFoldButton(new Vector2(origin.X + avail.X - headRow, origin.Y));

        var top = origin.Y + captionH;

        for (var i = 0; i < ArmourColumn.Length; i++)
        {
            ImGui.SetCursorScreenPos(new Vector2(origin.X, top + (i * (tile + gap))));
            DrawSlotTile(ArmourColumn[i], column, tile);
        }

        for (var i = 0; i < AccessoryColumn.Length; i++)
        {
            ImGui.SetCursorScreenPos(new Vector2(right, top + (i * (tile + gap))));
            DrawSlotTile(AccessoryColumn[i], column, tile);
        }

        var ruleY = top + (AccessoryColumn.Length * (tile + gap)) + Sp.Xl - gap;

        drawList.AddRectFilled(
            new Vector2(right, ruleY),
            new Vector2(right + column, ruleY + Stroke.Hair),
            Col.U32(Col.EdgeRest with { W = 0.55f }));

        var wardrobeTop = ruleY + Stroke.Hair + Sp.Xl;

        ImGui.SetCursorScreenPos(new Vector2(right, wardrobeTop));

        DrawPaneTile(
            RightPane.Outfits, FontAwesomeIcon.Bookmark, "Outfits",
            plugin.Outfits.Count == 0 ? "None saved" : $"{plugin.Outfits.Count} saved",
            highlight: false, width: column, height: tile);

        ImGui.SetCursorScreenPos(new Vector2(right, wardrobeTop + tile + gap));

        DrawPaneTile(
            RightPane.Appearance, FontAwesomeIcon.User, "Appearance",
            Wardrobe.HasAppearanceOverride ? "Changed" : "Untouched",
            highlight: Wardrobe.HasAppearanceOverride, width: column, height: tile);

        var footTop = MathF.Max(
            top + (ArmourColumn.Length * (tile + gap)),
            wardrobeTop + (tile * 2f) + gap) + Sp.Xl;

        drawList.AddRectFilled(
            new Vector2(origin.X, footTop),
            new Vector2(origin.X + avail.X, footTop + Stroke.Hair),
            Col.U32(Col.EdgeRest with { W = 0.55f }));

        ImGui.SetCursorScreenPos(new Vector2(origin.X, footTop + Stroke.Hair + Sp.Xl));
        DrawGlobalActions((avail.X - Sp.Md) / 2f, Metrics.RowTall);
    }

    /// Revert All and Load Current, side by side at the foot of the figure.
    private void DrawGlobalActions(float width, float height)
    {
        var icons = plugin.Fonts.Icon;

        if (EchoButton.Draw(
                "##revertall", "Revert All", new Vector2(width, height),
                iconFont: icons, icon: FontAwesomeIcon.UndoAlt,
                enabled: Wardrobe.Active,
                tooltip: "Puts your real gear and appearance back."))
        {
            Wardrobe.RevertAll();
            appearance.Invalidate();
        }

        ImGui.SameLine(0f, Sp.Md);

        if (EchoButton.Draw(
                "##copyworn", "Load Current", new Vector2(width, height),
                iconFont: icons, icon: FontAwesomeIcon.ArrowDown,
                tooltip: "Fills the slots with what you are wearing right now, glamour plates and " +
                         "dyes included, so you can edit from there."))
        {
            var current = Wardrobe.CaptureWorn();

            if (current.Count > 0)
                Wardrobe.Wear(current);
        }
    }

    /// The material every tile in the figure is made of: fill, the thread of light on its top edge, and a
    /// ring that only emits for the one you are working in.
    private static void PaintTileBody(
        ImDrawListPtr drawList, Vector2 min, Vector2 max, string id,
        bool selected, bool hovered, bool occupied)
    {
        var lit = Motion.Toward(id, selected, Anim.SelectSpeed);

        if (lit > 0.01f)
            Elev.Glow(drawList, min, max, Theme.Accent, 0.21f * lit, Rad.Ctl, 9f);

        if (selected || hovered || occupied)
        {
            var body = selected
                ? Vector4.Lerp(Col.Raised, Theme.Accent, 0.12f)
                : hovered
                    ? Col.RaisedHigh
                    : Col.Raised;

            drawList.AddRectFilled(min, max, Col.U32(body), Rad.Ctl);

            drawList.AddLine(
                new Vector2(min.X + Rad.Ctl, min.Y + (Stroke.Hair / 2f)),
                new Vector2(max.X - Rad.Ctl, min.Y + (Stroke.Hair / 2f)),
                Col.U32(new Vector4(1f, 1f, 1f, 0.09f)), Stroke.Hair);
        }

        drawList.AddRect(
            min, max,
            Col.U32(lit > 0.01f
                ? Theme.Accent with { W = 0.85f * lit }
                : Col.EdgeRest with { W = occupied ? 0.6f : 0.35f }),
            Rad.Ctl, ImDrawFlags.None, lit > 0.01f ? Stroke.Edge : Stroke.Hair);
    }

    /// The control that folds the figure away and brings it back.
    private void DrawFoldButton(Vector2 at)
    {
        var drawList = ImGui.GetWindowDrawList();
        var size = Metrics.TouchMin;

        ImGui.SetCursorScreenPos(at);
        ImGui.InvisibleButton("##fold", new Vector2(size, size), ImGuiButtonFlags.MouseButtonLeft);

        var hovered = ImGui.IsItemHovered();

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            Compact = !Compact;

        var warm = Motion.Toward("foldfigure", hovered, 20f);

        var lean = (Compact ? 1f : -1f) * Sp.Xxs * warm;

        using (plugin.Fonts.Icon.PushSafe())
        {
            UiHelpers.DrawScaledIcon(
                drawList,
                Compact ? FontAwesomeIcon.AngleDoubleRight : FontAwesomeIcon.AngleDoubleLeft,
                at + new Vector2((size / 2f) + lean, size / 2f),
                Col.U32(Vector4.Lerp(Col.TextDim, Theme.Accent, warm)));
        }
    }

    /// The figure, folded: twelve slots and two destinations as icons, and nothing else.
    private void DrawRail()
    {
        var drawList = ImGui.GetWindowDrawList();
        var avail = ImGui.GetContentRegionAvail();
        var origin = ImGui.GetCursorScreenPos();

        var size = MathF.Min(avail.X, 36f * UiHelpers.Scale);
        var left = origin.X + ((avail.X - size) / 2f);
        var gap = Sp.Xxs;

        DrawFoldButton(new Vector2(origin.X + ((avail.X - Metrics.TouchMin) / 2f), origin.Y));

        var y = origin.Y + Metrics.TouchMin + Sp.Lg;

        foreach (var slot in GlamSlots.All)
        {
            ImGui.SetCursorScreenPos(new Vector2(left, y));
            DrawRailTile(slot, size);
            y += size + gap;
        }

        y += Sp.Md;

        drawList.AddRectFilled(
            new Vector2(left, y),
            new Vector2(left + size, y + Stroke.Hair),
            Col.U32(Col.EdgeRest with { W = 0.55f }));

        y += Stroke.Hair + Sp.Md;

        ImGui.SetCursorScreenPos(new Vector2(left, y));
        DrawRailPane(RightPane.Outfits, FontAwesomeIcon.Bookmark, "Outfits", size, highlight: false);

        y += size + gap;

        ImGui.SetCursorScreenPos(new Vector2(left, y));

        DrawRailPane(
            RightPane.Appearance, FontAwesomeIcon.User, "Appearance", size,
            highlight: Wardrobe.HasAppearanceOverride);

        y += size + Sp.Lg;

        ImGui.SetCursorScreenPos(new Vector2(left, y));

        if (DrawRailAction(
                "railrevert", FontAwesomeIcon.UndoAlt, size,
                "Revert All\n\nPuts your real gear and appearance back.",
                enabled: Wardrobe.Active))
        {
            Wardrobe.RevertAll();
            appearance.Invalidate();
        }

        ImGui.SetCursorScreenPos(new Vector2(left, y + size + gap));

        if (DrawRailAction(
                "railload", FontAwesomeIcon.ArrowDown, size,
                "Load Current\n\nFills the slots with what you are wearing right now, glamour "
                + "plates and dyes included.",
                enabled: true))
        {
            var current = Wardrobe.CaptureWorn();

            if (current.Count > 0)
                Wardrobe.Wear(current);
        }
    }

    /// One of the two global actions in the folded rail, square and matching the tiles.
    private bool DrawRailAction(string id, FontAwesomeIcon glyph, float size, string tooltip, bool enabled)
    {
        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(size, size);

        var pressed = ImGui.InvisibleButton($"##{id}", new Vector2(size, size), ImGuiButtonFlags.MouseButtonLeft)
                      && enabled;

        var hovered = enabled && ImGui.IsItemHovered();

        if (ImGui.IsItemHovered())
            UiHelpers.WrappedTooltip(tooltip);

        PaintTileBody(drawList, min, max, id, selected: false, hovered: hovered, occupied: enabled);

        using (plugin.Fonts.Icon.PushSafe())
        {
            UiHelpers.DrawScaledIcon(
                drawList, glyph, min + new Vector2(size / 2f, size / 2f),
                Col.U32(!enabled
                    ? Col.Disabled(Col.TextMuted)
                    : hovered
                        ? Theme.Accent
                        : Col.TextDim));
        }

        return pressed;
    }

    /// One slot in the folded rail: its icon, and the tooltip that names it.
    private void DrawRailTile(GlamSlot slot, float size)
    {
        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(size, size);

        var entry = Wardrobe.EntryFor(slot);
        var item = entry is { } e ? Items.Resolve(e.ItemId) : GlamItem.None;
        var occupied = entry is not null;
        var selected = slot == selectedSlot && pane == RightPane.Items;

        if (ImGui.InvisibleButton($"##rail{slot}", new Vector2(size, size), ImGuiButtonFlags.MouseButtonLeft))
        {
            selectedSlot = slot;
            search = string.Empty;
            pane = RightPane.Items;
            outfits.Leave();
        }

        var hovered = ImGui.IsItemHovered();

        if (hovered && occupied && !item.IsNone && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            Wardrobe.Set(slot, 0);

        if (hovered)
        {
            UiHelpers.WrappedTooltip(occupied
                ? item.IsNone
                    ? $"{slot.Label()}\nHidden"
                    : $"{slot.Label()}\n{item.Name}\n\nRight-click to take it off."
                : $"{slot.Label()}\nYour own gear");
        }

        PaintTileBody(drawList, min, max, $"railtile{slot}", selected, hovered, occupied);

        var inset = Sp.Xs;
        DrawItemIcon(drawList, item, min + new Vector2(inset, inset), size - (inset * 2f), occupied ? 1f : 0.4f);
    }

    private void DrawRailPane(RightPane target, FontAwesomeIcon glyph, string label, float size, bool highlight)
    {
        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(size, size);
        var selected = pane == target;

        if (ImGui.InvisibleButton($"##railpane{target}", new Vector2(size, size), ImGuiButtonFlags.MouseButtonLeft))
        {
            pane = target;
            outfits.Leave();
        }

        var hovered = ImGui.IsItemHovered();

        if (hovered)
            UiHelpers.WrappedTooltip(label);

        PaintTileBody(drawList, min, max, $"railpane{target}", selected, hovered, occupied: true);

        using (plugin.Fonts.Icon.PushSafe())
        {
            UiHelpers.DrawScaledIcon(
                drawList, glyph, min + new Vector2(size / 2f, size / 2f),
                Col.U32(selected || highlight ? Theme.Accent : Col.TextDim));
        }
    }

    /// One slot on the character sheet: what is in it, what colour, and the way to go and change it.
    private void DrawSlotTile(GlamSlot slot, float width, float height)
    {
        var drawList = ImGui.GetWindowDrawList();
        var scale = UiHelpers.Scale;
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(width, height);

        var entry = Wardrobe.EntryFor(slot);
        var item = entry is { } e ? Items.Resolve(e.ItemId) : GlamItem.None;
        var occupied = entry is not null;
        var selected = slot == selectedSlot && pane == RightPane.Items;

        var clicked = ImGui.InvisibleButton(
            $"##slot{slot}", new Vector2(width, height), ImGuiButtonFlags.MouseButtonLeft);

        var hovered = ImGui.IsItemHovered();

        if (clicked)
        {
            selectedSlot = slot;
            search = string.Empty;
            pane = RightPane.Items;
            outfits.Leave();
        }

        if (hovered && occupied && !item.IsNone && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            Wardrobe.Set(slot, 0);

        if (hovered && occupied)
        {
            UiHelpers.WrappedTooltip(item.IsNone
                ? "Hidden\n\nSelect the slot and press Restore to show your real gear again."
                : $"{item.Name}\n\nRight-click to take it off.");
        }

        PaintTileBody(drawList, min, max, $"slottile{slot}", selected, hovered, occupied);

        var icon = 34f * scale;
        var iconMin = new Vector2(min.X + Sp.Md, min.Y + ((height - icon) / 2f));

        DrawItemIcon(drawList, item, iconMin, icon, occupied ? 1f : 0.4f);

        var textLeft = iconMin.X + icon + Sp.Md;
        var textRight = max.X - Sp.Md;

        var chip = 10f * scale;
        var chips = 0;

        if (entry is { } dyed)
        {
            if (item.DyeChannels >= 1 && dyed.Stain0 != 0) chips++;
            if (item.DyeChannels >= 2 && dyed.Stain1 != 0) chips++;
        }

        var chipRoom = chips > 0 ? (chips * (chip + Sp.Xxs)) + Sp.Sm : 0f;

        var captionLine = Text.CaptionSize * 1.15f;
        var block = captionLine + Sp.Xxs + ImGui.GetTextLineHeight();
        var textTop = min.Y + ((height - block) / 2f);
        var nameTop = textTop + captionLine + Sp.Xxs;

        using (Text.PushCaption())
        {
            Text.Tracked(
                drawList, new Vector2(textLeft, textTop), slot.Label().ToUpperInvariant(),
                Col.U32(selected ? Theme.Accent : Col.TextMuted), 1.5f,
                MathF.Max(0f, textRight - textLeft));
        }

        var name = occupied ? (item.IsNone ? "Hidden" : item.Name) : "Your own";

        drawList.AddText(
            new Vector2(textLeft, nameTop),
            Col.U32(occupied ? Col.Text : Col.TextMuted),
            Text.Truncate(name, MathF.Max(0f, textRight - textLeft - chipRoom)));

        if (chips == 0 || entry is not { } stains)
            return;

        var chipY = nameTop + ((ImGui.GetTextLineHeight() - chip) / 2f);
        var chipX = textRight - chip;

        if (item.DyeChannels >= 2 && stains.Stain1 != 0)
        {
            UiHelpers.DrawSwatch(drawList, new Vector2(chipX, chipY), chip, Dyes.Resolve(stains.Stain1).Colour);
            chipX -= chip + Sp.Xxs;
        }

        if (item.DyeChannels >= 1 && stains.Stain0 != 0)
            UiHelpers.DrawSwatch(drawList, new Vector2(chipX, chipY), chip, Dyes.Resolve(stains.Stain0).Colour);
    }

    /// Outfits and Appearance, drawn to match a slot tile because they are the same kind of choice.
    private void DrawPaneTile(
        RightPane target, FontAwesomeIcon glyph, string label, string detail,
        bool highlight, float width, float height)
    {
        var drawList = ImGui.GetWindowDrawList();
        var scale = UiHelpers.Scale;
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(width, height);

        var selected = pane == target;

        if (ImGui.InvisibleButton(
                $"##pane{target}", new Vector2(width, height), ImGuiButtonFlags.MouseButtonLeft))
        {
            pane = target;

            outfits.Leave();
        }

        var hovered = ImGui.IsItemHovered();

        PaintTileBody(drawList, min, max, $"panetile{target}", selected, hovered, occupied: true);

        var icon = 34f * scale;
        var centre = new Vector2(min.X + Sp.Md + (icon / 2f), min.Y + (height / 2f));

        using (plugin.Fonts.Icon.PushSafe())
        {
            UiHelpers.DrawScaledIcon(
                drawList, glyph,
                centre,
                Col.U32(selected || highlight ? Theme.Accent : Col.TextDim));
        }

        var textLeft = min.X + Sp.Md + icon + Sp.Md;
        var room = MathF.Max(0f, max.X - Sp.Md - textLeft);

        var captionLine = Text.CaptionSize * 1.15f;
        var block = captionLine + Sp.Xxs + ImGui.GetTextLineHeight();
        var textTop = min.Y + ((height - block) / 2f);

        using (Text.PushCaption())
        {
            Text.Tracked(
                drawList, new Vector2(textLeft, textTop), label.ToUpperInvariant(),
                Col.U32(selected ? Theme.Accent : Col.TextMuted), 1.5f, room);
        }

        drawList.AddText(
            new Vector2(textLeft, textTop + captionLine + Sp.Xxs),
            Col.U32(highlight ? Col.Text : Col.TextDim),
            Text.Truncate(detail, room));
    }

    private void DrawBuilding()
    {
        var scale = UiHelpers.Scale;
        ImGui.Dummy(new Vector2(0f, 40f * scale));
        Theme.SectionHeader("Reading the game's item list");
        ImGui.Dummy(new Vector2(0f, 6f * scale));
        ImGui.TextColored(Theme.TextDim,
            "Every wearable item in the game, straight from its own data. This happens once.");
    }

    /// The workbench: what you are working on, the controls that narrow the list, and the list.
    private void DrawPicker()
    {
        var dyed = Wardrobe.EntryFor(selectedSlot);
        if (dyeOpenFor == selectedSlot && (dyed is null || !Items.Resolve(dyed.Value.ItemId).IsDyeable))
            CloseDye();

        var showingDye = dyeOpenFor == selectedSlot;

        RefreshMatches();

        var width = ImGui.GetContentRegionAvail().X;

        DrawSubject(width, showingDye);
        ImGui.Dummy(new Vector2(0f, Sp.Lg));

        if (showingDye)
        {
            UiHelpers.SearchField(
                "##dyesearch", "Search dyes...", ref dyeSearch, width,
                Metrics.Row, plugin.Fonts.Icon);
        }
        else
        {
            DrawFilterRow(width);
        }

        ImGui.Dummy(new Vector2(0f, Sp.Lg));

        var drawList = ImGui.GetWindowDrawList();
        var wellMin = ImGui.GetCursorScreenPos();
        var wellSize = new Vector2(width, MathF.Max(100f * UiHelpers.Scale, ImGui.GetContentRegionAvail().Y));
        var wellMax = wellMin + wellSize;

        drawList.AddRectFilled(wellMin, wellMax, Col.U32(Col.Sunken), Rad.Panel);

        drawList.AddLine(
            new Vector2(wellMin.X + Rad.Panel, wellMin.Y + (Stroke.Hair / 2f)),
            new Vector2(wellMax.X - Rad.Panel, wellMin.Y + (Stroke.Hair / 2f)),
            Col.U32(new Vector4(0f, 0f, 0f, 0.45f)), Stroke.Hair);

        var pad = new Vector2(Sp.Lg, Sp.Lg);
        ImGui.SetCursorScreenPos(wellMin + pad);

        if (ImGui.BeginChild("##pickerscroll", wellSize - (pad * 2f), false))
        {
            if (showingDye)
            {
                DrawDyeGrid();
            }
            else
            {
                if (restoreItemScroll is { } scroll)
                {
                    ImGui.SetScrollY(scroll);
                    restoreItemScroll = null;
                }

                DrawItemGrid();

                itemScroll = ImGui.GetScrollY();
            }
        }

        ImGui.EndChild();
    }

    /// The item the pointer was over in the grid, and the frame it was seen on.
    private GlamItem? hoveredCell;

    private int hoveredCellFrame = -10;

    /// The one slot you are working on, said properly.
    private void DrawSubject(float width, bool showingDye)
    {
        var drawList = ImGui.GetWindowDrawList();
        var scale = UiHelpers.Scale;
        var min = ImGui.GetCursorScreenPos();

        var icon = BannerIconDesign * scale;
        var height = BannerHeight;
        var max = min + new Vector2(width, height);

        var entry = Wardrobe.EntryFor(selectedSlot);
        var worn = entry is { } e ? Items.Resolve(e.ItemId) : GlamItem.None;

        var previewing = hoveredCell is not null && ImGui.GetFrameCount() - hoveredCellFrame <= 1;
        var subject = previewing && hoveredCell is { } peek ? peek : worn;

        Surface.Paint(min, new Vector2(width, height), SurfaceKind.Raised);

        var iconMin = new Vector2(min.X + Sp.Xl, min.Y + Sp.Xl);
        DrawItemIcon(drawList, subject, iconMin, icon, subject.IsNone ? 0.4f : 1f);

        var actionsLeft = DrawSubjectActions(min, max, height, showingDye);

        var textLeft = iconMin.X + icon + Sp.Xl;
        var room = MathF.Max(0f, actionsLeft - Sp.Xl - textLeft);

        var captionLine = Text.CaptionSize * 1.15f;
        var titleLine = Text.TitleSize * 1.2f;
        var block = captionLine + Sp.Xs + titleLine + Sp.Xs + captionLine;
        var top = min.Y + ((height - block) / 2f);

        using (Text.PushCaption())
        {
            var label = selectedSlot.Label().ToUpperInvariant();

            var used = Text.Tracked(
                drawList, new Vector2(textLeft, top), label,
                Col.U32(entry is not null ? Theme.Accent : Col.TextMuted), 1.8f, room);

            if (previewing)
            {
                Pill.Paint(
                    drawList,
                    new Vector2(textLeft + used + Sp.Md, top - Sp.Xs),
                    "PREVIEW", Foundation.Pill.Tone.Info);
            }
        }

        var name = !subject.IsNone
            ? subject.Name
            : entry is null
                ? "Your own gear"
                : "Hidden";

        using (Text.PushTitle())
        {
            drawList.AddText(
                new Vector2(textLeft, top + captionLine + Sp.Xs),
                Col.U32(subject.IsNone ? Col.TextDim : Col.Text),
                Text.Truncate(name, room));
        }

        DrawSubjectMeta(
            subject, entry, drawList,
            new Vector2(textLeft, top + captionLine + Sp.Xs + titleLine + Sp.Xs), room);

        ImGui.SetCursorScreenPos(min);
        ImGui.Dummy(new Vector2(width, height));
    }

    /// Hide, Restore and Dye, laid from the banner's right edge.
    private float DrawSubjectActions(Vector2 min, Vector2 max, float height, bool showingDye)
    {
        var entry = Wardrobe.EntryFor(selectedSlot);
        var item = entry is { } e ? Items.Resolve(e.ItemId) : GlamItem.None;
        var rowHeight = Metrics.Row;

        var dyeable = entry is not null && item.IsDyeable;
        var dyeLabel = showingDye ? "Back" : "Dye";

        var widths = EchoButton.ContentSize("Hide").X + EchoButton.ContentSize("Restore").X + Sp.Sm;

        if (dyeable)
            widths += EchoButton.ContentSize(dyeLabel).X + Sp.Sm;

        var left = max.X - Sp.Xl - widths;

        ImGui.SetCursorScreenPos(new Vector2(left, min.Y + ((height - rowHeight) / 2f)));

        var hidden = entry is { ItemId: 0 };
        var restores = Wardrobe.HiddenBefore(selectedSlot);
        var restoresName = restores is { } r ? Items.Resolve(r.ItemId).Name : null;

        if (EchoButton.Draw(
                "##hide", "Hide", new Vector2(0f, rowHeight),
                selected: hidden,
                tooltip: !hidden
                    ? "Show nothing in this slot."
                    : restoresName is not null
                        ? $"Put {restoresName} back."
                        : "Show your real gear again."))
        {
            if (!hidden)
            {
                Wardrobe.Set(selectedSlot, 0);
            }
            else if (!Wardrobe.Unhide(selectedSlot))
            {
                Wardrobe.Clear(selectedSlot);
            }
        }

        ImGui.SameLine(0f, Sp.Sm);

        if (EchoButton.Draw(
                "##restore", "Restore", new Vector2(0f, rowHeight),
                enabled: entry is not null,
                tooltip: "Hand this slot back to the game and show your real gear."))
            Wardrobe.Clear(selectedSlot);

        if (dyeable)
        {
            ImGui.SameLine(0f, Sp.Sm);

            if (EchoButton.Draw(
                    "##dye", dyeLabel, new Vector2(0f, rowHeight), selected: showingDye))
            {
                if (showingDye)
                    CloseDye();
                else
                    OpenDye(selectedSlot);
            }
        }

        return left;
    }

    /// The line under the name: owned, or what the board wants for it, and any availability pill the piece
    /// has earned.
    private void DrawSubjectMeta(
        GlamItem subject, GlamEntry? entry, ImDrawListPtr drawList, Vector2 at, float room)
    {
        if (subject.IsNone)
        {
            using (Text.PushCaption())
            {
                drawList.AddText(
                    at, Col.U32(Col.TextMuted),
                    Text.Truncate(
                        entry is null
                            ? "Nothing is glamoured here. Pick something below."
                            : "Nothing is drawn in this slot.",
                        room));
            }

            return;
        }

        var x = at.X;

        var chip = 12f * UiHelpers.Scale;
        var chips = 0;

        if (entry is { } worn && subject.DyeChannels > 0)
        {
            if (worn.Stain0 != 0) chips++;
            if (subject.DyeChannels >= 2 && worn.Stain1 != 0) chips++;
        }

        var rightEdge = at.X + room;
        var limit = rightEdge - (chips > 0 ? (chips * (chip + Sp.Xs)) + Sp.Lg : 0f);
        room = MathF.Max(0f, limit - at.X);

        string note;
        Vector4 tone;

        if (plugin.Owned.Contains(subject.ItemId))
        {
            note = "You own this";
            tone = Col.Success;
        }
        else if (!subject.IsMarketable)
        {
            note = "Not sold on the board";
            tone = Col.TextMuted;
        }
        else
        {
            var quote = plugin.Prices.Ask(subject.ItemId, GalleryState.Character.World);

            note = quote.State switch
            {
                MarketState.Listed => $"{MarketPrices.Gil(quote.Cheapest)} on the board",
                MarketState.Loading => "Checking the board...",
                MarketState.Empty => "None listed on the board",
                _ => string.Empty,
            };

            tone = quote.State == MarketState.Listed ? Col.TextDim : Col.TextMuted;
        }

        if (note.Length > 0)
        {
            using (Text.PushCaption())
            {
                drawList.AddText(at, Col.U32(tone), Text.Truncate(note, room));
                x += Text.Width(Text.Truncate(note, room)) + Sp.Lg;
            }
        }

        foreach (var pill in AvailabilityPills.For(subject))
        {
            var pillWidth = Pill.Measure(pill.Label);

            if (x + pillWidth > limit)
                break;

            Pill.Paint(
                drawList, new Vector2(x, at.Y - (Sp.Xxs * 2f)), pill.Label,
                pill.Colour == Theme.Warning ? Foundation.Pill.Tone.Caution : Foundation.Pill.Tone.Info);

            x += pillWidth + Sp.Xs;
        }

        if (chips == 0 || entry is not { } stains)
            return;

        var chipX = rightEdge - chip;
        var chipY = at.Y + (((Text.CaptionSize * 1.15f) - chip) / 2f);

        if (subject.DyeChannels >= 2 && stains.Stain1 != 0)
        {
            UiHelpers.DrawSwatch(drawList, new Vector2(chipX, chipY), chip, Dyes.Resolve(stains.Stain1).Colour);
            chipX -= chip + Sp.Xs;
        }

        if (stains.Stain0 != 0)
            UiHelpers.DrawSwatch(drawList, new Vector2(chipX, chipY), chip, Dyes.Resolve(stains.Stain0).Colour);
    }

    /// The search box and the three filters, on one line, with the search taking whatever the filters do not.
    private void DrawFilterRow(float width)
    {
        var icons = plugin.Fonts.Icon;

        var rowHeight = Metrics.Row;
        var weapon = selectedSlot.IsWeapon();

        var reserved = 0f;

        if (!weapon)
            reserved += EchoButton.ContentSize(JobFilterLabel, icons, FontAwesomeIcon.Filter).X + Sp.Md;

        reserved += EchoButton.ContentSize(SourceFilterLabel, icons, FontAwesomeIcon.Tags).X + Sp.Md;
        reserved += EchoButton.ContentSize("Owned", icons, FontAwesomeIcon.BoxOpen).X;

        UiHelpers.SearchField(
            "##search", "Search this slot...", ref search,
            MathF.Max(140f * UiHelpers.Scale, width - reserved - Sp.Xl),
            rowHeight, plugin.Fonts.Icon);

        if (!weapon)
        {
            ImGui.SameLine(0f, Sp.Xl);
            DrawJobFilter(rowHeight);
            ImGui.SameLine(0f, Sp.Md);
        }
        else
        {
            ImGui.SameLine(0f, Sp.Xl);
        }

        DrawAvailabilityFilter(rowHeight);
        ImGui.SameLine(0f, Sp.Md);
        DrawOwnedFilter(rowHeight);

        if (!weapon)
            return;

        ImGui.Dummy(new Vector2(0f, Sp.Xs));

        using (Text.PushCaption())
        {
            ImGui.TextColored(
                Theme.TextDim, "Your current job's weapons only. Changing job clears this slot.");
        }
    }

    /// Where the item list was left, and the position to put it back to on the frame after the dye grid
    /// closes.
    private float itemScroll;

    private float? restoreItemScroll;

    /// Opens the dye grid for a slot, keeping the item list's place.
    private void OpenDye(GlamSlot slot)
    {
        restoreItemScroll = null;
        dyeOpenFor = slot;
    }

    /// Closes the dye grid and asks for the item list's place back.
    private void CloseDye()
    {
        dyeOpenFor = null;
        restoreItemScroll = itemScroll;
    }

    private const string AvailabilityPopupId = "##echoglamavailability";

    /// THE COUNT IS OF ITEMS, NOT OF BOXES TICKED.
    private string SourceFilterLabel =>
        hideStore || hideSeasonal || hideExclusive ? $"Sources ({hiddenBySource})" : "Sources";

    private string JobFilterLabel =>
        selectedJob is { } id ? JobList.Find(id)?.Abbreviation ?? "All Jobs" : "All Jobs";

    /// Hides gear that cannot simply be gone and got.
    private void DrawAvailabilityFilter(float rowHeight)
    {
        var on = hideStore || hideSeasonal || hideExclusive;
        var label = SourceFilterLabel;

        if (EchoButton.Draw(
                "##availabilityfilter", label,
                new Vector2(0f, rowHeight),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Tags,
                selected: on,
                tooltip: !on
                    ? "Hide store, seasonal or exclusive items."
                    : $"Hiding {hiddenBySource} "
                      + (hiddenBySource == 1 ? "item" : "items")
                      + " in this slot: "
                      + string.Join(", ",
                        new[]
                        {
                            hideStore ? "store" : null,
                            hideSeasonal ? "seasonal" : null,
                            hideExclusive ? "exclusive" : null,
                        }.Where(part => part is not null))
                      + ". Click to change, right-click to clear."))
            ImGui.OpenPopup(AvailabilityPopupId);

        if (EchoButton.RightClicked())
            hideStore = hideSeasonal = hideExclusive = false;

        DrawAvailabilityPopup();
    }

    private void DrawAvailabilityPopup()
    {
        if (!ImGui.BeginPopup(AvailabilityPopupId))
            return;

        var scale = UiHelpers.Scale;
        var width = Modal.Width(ModalSize.Small);

        Theme.SectionHeader("Hide by source", ruleWidth: width);
        ImGui.Dummy(new Vector2(0f, 4f * scale));

        EchoToggle.Draw(
            "##hidestore", "Hide store items", ref hideStore,
            "Gear sold on the online store.");

        EchoToggle.Draw(
            "##hideseasonal", "Hide seasonal items", ref hideSeasonal,
            "Gear from seasonal events, which is only obtainable while the event is running.");

        EchoToggle.Draw(
            "##hideexclusive", "Hide exclusive items", ref hideExclusive,
            "Promotional, collaboration and pre-order gear.");

        ImGui.Dummy(new Vector2(0f, 6f * scale));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);

        ImGui.TextColored(
            Theme.TextDisabled,
            "Read from the game's own store and armoire listings, so the odd piece may not be marked.");

        ImGui.PopTextWrapPos();
        ImGui.EndPopup();
    }

    /// Narrows the picker to gear this character can actually get at.
    private void DrawOwnedFilter(float rowHeight)
    {
        var owned = plugin.Owned;

        if (EchoButton.Draw(
                "##ownedfilter", "Owned", new Vector2(0f, rowHeight),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.BoxOpen,
                selected: ownedOnly,
                tooltip: OwnedTooltip(owned)))
            ownedOnly = !ownedOnly;

        if (EchoButton.RightClicked())
            ownedOnly = false;
    }

    private static string OwnedTooltip(OwnedItems owned)
    {
        var sources = owned.Sources;

        if (sources.Count == 0)
        {
            return "Show only gear you can get at.\n\nNothing scanned yet - it fills in as you play, "
                 + "and reads your retainers, saddlebag, armoire and glamour dresser as you open them.";
        }

        var lines = new List<string>
        {
            $"Showing only gear you can get at - {owned.Count} items.",
            string.Empty,
            "Read from:",
        };

        foreach (var source in sources)
            lines.Add($"  {source.Label} - {Ago(source.SeenUtc)}");

        lines.Add(string.Empty);
        lines.Add("Retainers, the saddlebag, the armoire and the dresser are only readable while open, "
                + "so anything missing is a container EchoGlam has not seen yet. Open it once and it is "
                + "remembered.");
        lines.Add(string.Empty);
        lines.Add("Right-click to clear the filter.");

        return string.Join("\n", lines);
    }

    /// How long ago, in the roughest terms that are still useful.
    private static string Ago(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;

        if (span < TimeSpan.FromMinutes(2)) return "just now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} minutes ago";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours} hours ago";

        return span < TimeSpan.FromDays(2) ? "yesterday" : $"{(int)span.TotalDays} days ago";
    }

    private const string JobPopupId = "##echoglamjobs";

    /// The job filter, as one button that opens a picker.
    private void DrawJobFilter(float rowHeight)
    {
        var job = selectedJob is { } id ? JobList.Find(id) : null;

        if (EchoButton.Draw(
                "##jobfilter", JobFilterLabel, new Vector2(0f, rowHeight),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Filter,
                selected: selectedJob is not null,
                tooltip: job is { } sel
                    ? $"Showing only gear {sel.Name} can wear. Click to change, right-click to clear."
                    : "Show only gear a particular job can wear."))
            ImGui.OpenPopup(JobPopupId);

        if (EchoButton.RightClicked())
            selectedJob = null;

        DrawJobPopup();
    }

    private void DrawJobPopup()
    {
        if (!ImGui.BeginPopup(JobPopupId))
            return;

        var scale = UiHelpers.Scale;
        var icon = 30f * scale;
        var gap = 4f * scale;

        var width = (icon * JobList.LargestGroupSize) + (gap * (JobList.LargestGroupSize - 1));

        Theme.SectionHeader("Filter by job", ruleWidth: width);
        ImGui.Dummy(new Vector2(0f, 4f * scale));

        if (EchoButton.Draw(
                "##joball", "All Jobs", new Vector2(width, 24f * scale),
                selected: selectedJob is null))
        {
            selectedJob = null;
            ImGui.CloseCurrentPopup();
        }

        var drawList = ImGui.GetWindowDrawList();

        foreach (var group in JobList.Groups)
        {
            ImGui.Dummy(new Vector2(0f, 8f * scale));
            ImGui.TextColored(Theme.TextDim, JobList.GroupLabel(group));
            ImGui.Dummy(new Vector2(0f, 2f * scale));

            var first = true;

            foreach (var job in JobList.InGroup(group))
            {
                if (!first)
                    ImGui.SameLine(0f, gap);

                first = false;
                DrawJobButton(drawList, job, icon);
            }
        }

        ImGui.Dummy(new Vector2(0f, 4f * scale));
        ImGui.EndPopup();
    }

    private void DrawJobButton(ImDrawListPtr drawList, JobInfo job, float size)
    {
        var scale = UiHelpers.Scale;
        var origin = ImGui.GetCursorScreenPos();
        var selected = selectedJob == job.Id;

        var clicked = ImGui.InvisibleButton($"##job{job.Id}", new Vector2(size, size), ImGuiButtonFlags.MouseButtonLeft);
        var hovered = ImGui.IsItemHovered();

        if (clicked)
        {
            selectedJob = job.Id;
            ImGui.CloseCurrentPopup();
        }

        if (hovered)
            UiHelpers.WrappedTooltip($"{job.Name} ({job.Abbreviation})");

        if (selected || hovered)
        {
            drawList.AddRectFilled(
                origin, origin + new Vector2(size, size),
                ImGui.GetColorU32(Theme.Tinted(selected ? 0.32f : 0.16f)), 5f * scale);
        }

        var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(job.IconId)).GetWrapOrDefault();
        if (texture != null)
        {
            var inset = 2f * scale;
            drawList.AddImage(
                texture.Handle, origin + new Vector2(inset, inset),
                origin + new Vector2(size - inset, size - inset), Vector2.Zero, Vector2.One,
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, selected ? 1f : hovered ? 0.9f : 0.6f)));
        }

        if (selected)
        {
            drawList.AddRect(
                origin, origin + new Vector2(size, size), ImGui.GetColorU32(Theme.Accent),
                5f * scale, ImDrawFlags.None, 2f * scale);
        }
    }

    /// The item grid, clipped to what is on screen.
    private void DrawItemGrid()
    {
        var scale = UiHelpers.Scale;
        var cell = CellDesign * scale;
        var gap = CellGapDesign * scale;
        var iconSize = CellIconDesign * scale;

        hoveredCell = null;

        if (matches.Count == 0)
        {
            var reason = search.Length > 0
                ? "Nothing here matches that search."
                : ownedOnly && !plugin.Owned.Ready
                    ? "Nothing scanned yet. Your inventory and armoury are read automatically; open a "
                      + "retainer, your saddlebag, the armoire or the glamour dresser once and their "
                      + "contents are remembered."
                    : ownedOnly
                        ? "Nothing you can get at fits this slot. Anything in a retainer or container "
                          + "EchoGlam has not seen yet is missing until you open it once."
                        : selectedJob is not null
                            ? "Nothing in this slot is equippable by that job."
                            : hideStore || hideSeasonal || hideExclusive
                                ? "Everything in this slot is hidden by the source filter."
                                : "Nothing your character can wear fits this slot.";

            Notice.Empty(
                ImGui.GetContentRegionAvail().X, FontAwesomeIcon.Search,
                "Nothing to show", reason);

            return;
        }

        var available = ImGui.GetContentRegionAvail().X;
        var columns = Math.Max(1, (int)((available + gap) / (cell + gap)));
        var rows = (matches.Count + columns - 1) / columns;

        var entry = Wardrobe.EntryFor(selectedSlot);
        var currentId = entry?.ItemId ?? uint.MaxValue;

        var drawList = ImGui.GetWindowDrawList();

        var nameLine = Text.CaptionSize * 1.25f;
        var rowHeight = cell + nameLine + gap;

        var clipper = new ImGuiListClipper();
        clipper.Begin(rows, rowHeight);

        while (clipper.Step())
        {
            for (var row = clipper.DisplayStart; row < clipper.DisplayEnd; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    var index = (row * columns) + column;
                    if (index >= matches.Count)
                        break;

                    if (column > 0)
                        ImGui.SameLine(0f, gap);

                    DrawItemCell(drawList, matches[index], cell, iconSize, currentId);
                }
            }
        }

        clipper.End();
    }

    private void DrawItemCell(ImDrawListPtr drawList, GlamItem item, float cell, float iconSize, uint currentId)
    {
        var scale = UiHelpers.Scale;
        var origin = ImGui.GetCursorScreenPos();

        var selected = item.ItemId == currentId;

        ImGui.BeginGroup();

        var clicked = ImGui.InvisibleButton($"##item{item.ItemId}", new Vector2(cell, cell), ImGuiButtonFlags.MouseButtonLeft);
        var hovered = ImGui.IsItemHovered();

        if (clicked)
        {
            var (stain0, stain1) = StainsFor(item);
            Wardrobe.Set(selectedSlot, item.ItemId, stain0, stain1);
        }

        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            var (stain0, stain1) = StainsFor(item);
            Wardrobe.Set(selectedSlot, item.ItemId, stain0, stain1);

            if (item.IsDyeable)
                OpenDye(selectedSlot);
        }

        if (hovered)
        {
            hoveredCell = item;
            hoveredCellFrame = ImGui.GetFrameCount();

            if (item.IsDyeable)
                UiHelpers.WrappedTooltip("Right-click to wear it and pick its dyes.");
        }

        var max = origin + new Vector2(cell, cell);

        Cell.Paint(drawList, origin, max, selected, hovered);

        var inset = (cell - iconSize) / 2f;
        DrawItemIcon(drawList, item, origin + new Vector2(inset, inset), iconSize);

        var nameLine = Text.CaptionSize * 1.25f;

        using (Text.PushCaption())
        {
            drawList.AddText(
                new Vector2(origin.X, origin.Y + cell + (2f * scale)),
                Col.U32(Cell.Ink(selected, hovered)),
                Text.Truncate(item.Name, cell));
        }

        ImGui.Dummy(new Vector2(cell, nameLine));
        ImGui.EndGroup();
    }

    private void DrawDyeGrid()
    {
        var scale = UiHelpers.Scale;
        var swatch = 28f * scale;
        var gap = 5f * scale;

        var entry = Wardrobe.EntryFor(selectedSlot);
        if (entry is not { } current)
            return;

        var item = Items.Resolve(current.ItemId);
        var drawList = ImGui.GetWindowDrawList();

        var filter = dyeSearch.Trim();
        var filtered = filter.Length > 0;

        for (var channel = 0; channel < Math.Max(1, (int)item.DyeChannels); channel++)
        {
            Theme.SectionHeader(item.DyeChannels > 1 ? $"Dye channel {channel + 1}" : "Dye");
            ImGui.Dummy(new Vector2(0f, 4f * scale));

            var active = channel == 0 ? current.Stain0 : current.Stain1;
            var available = ImGui.GetContentRegionAvail().X;
            var perRow = Math.Max(1, (int)((available + gap) / (swatch + gap)));

            bool Survives(Dye dye) =>
                !filtered
                || dye.Id == active
                || dye.Name.Contains(filter, StringComparison.OrdinalIgnoreCase);

            var starred = 0;
            foreach (var dye in Dyes.Palette)
            {
                if (plugin.Configuration.IsFavouriteDye(dye.Id) && Survives(dye))
                    starred++;
            }

            var shown = 0;

            if (starred > 0)
            {
                using (Text.PushCaption())
                    ImGui.TextColored(Theme.TextDim, "Favourites");

                ImGui.Dummy(new Vector2(0f, Sp.Xs));

                shown += DrawDyeRun(channel, active, perRow, swatch, gap, Survives, favourites: true);

                ImGui.Dummy(new Vector2(0f, Sp.Sm));

                var ruleY = ImGui.GetCursorScreenPos().Y;
                var ruleLeft = ImGui.GetCursorScreenPos().X;
                drawList.AddRectFilled(
                    new Vector2(ruleLeft, ruleY),
                    new Vector2(ruleLeft + available, ruleY + Stroke.Hair),
                    Col.U32(Theme.Border with { W = Theme.Border.W * 0.7f }));

                ImGui.Dummy(new Vector2(0f, Sp.Md));
            }

            shown += DrawDyeRun(channel, active, perRow, swatch, gap, Survives, favourites: false);

            if (shown == 0)
                ImGui.TextColored(Theme.TextDim, $"No dye matches \"{filter}\".");

            ImGui.Dummy(new Vector2(0f, 10f * scale));
        }
    }

    /// One run of swatches: either the starred dyes or everything else, never both.
    private int DrawDyeRun(
        int channel, byte active, int perRow, float swatch, float gap,
        Func<Dye, bool> survives, bool favourites)
    {
        var scale = UiHelpers.Scale;
        var drawList = ImGui.GetWindowDrawList();
        var entry = Wardrobe.EntryFor(selectedSlot);
        if (entry is not { } current)
            return 0;

        var column = 0;

        foreach (var dye in Dyes.Palette)
        {
            if (plugin.Configuration.IsFavouriteDye(dye.Id) != favourites)
                continue;

            if (!survives(dye))
                continue;

            if (column > 0 && column % perRow != 0)
                ImGui.SameLine(0f, gap);

            var origin = ImGui.GetCursorScreenPos();

            var clicked = ImGui.InvisibleButton(
                $"##dye{channel}_{dye.Id}", new Vector2(swatch, swatch), ImGuiButtonFlags.MouseButtonLeft);

            var hovered = ImGui.IsItemHovered();

            if (clicked)
            {
                if (channel == 0)
                    Wardrobe.SetDye(selectedSlot, dye.Id, current.Stain1);
                else
                    Wardrobe.SetDye(selectedSlot, current.Stain0, dye.Id);

                if (channel == 0)
                    stickyStain0 = dye.Id;
                else
                    stickyStain1 = dye.Id;
            }

            if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                plugin.Configuration.ToggleFavouriteDye(dye.Id);

            if (hovered)
            {
                UiHelpers.WrappedTooltip(favourites
                    ? $"{dye.Name}\n\nRight-click to remove it from your favourites."
                    : $"{dye.Name}\n\nRight-click to add it to your favourites.");
            }

            drawList.AddRectFilled(
                origin, origin + new Vector2(swatch, swatch), ImGui.GetColorU32(dye.Colour), 4f * scale);

            var ring = dye.Id == active
                ? Theme.Accent
                : new Vector4(1f, 1f, 1f, hovered ? 0.55f : 0.15f);

            drawList.AddRect(
                origin, origin + new Vector2(swatch, swatch), ImGui.GetColorU32(ring),
                4f * scale, ImDrawFlags.None, (dye.Id == active ? 2.5f : 1f) * scale);

            column++;
        }

        return column;
    }

    /// Kept across opens rather than cleared each time.
    private string dyeSearch = string.Empty;

    /// SHARED NOW, because the outfits pane draws gear too.
    private static void DrawItemIcon(
        ImDrawListPtr drawList, GlamItem item, Vector2 min, float size, float alpha = 1f) =>
        ItemIcon.Draw(drawList, item, min, size, alpha);

    /// Items in the selected slot that this character could actually wear, narrowed by the job filter and the
    /// search box.
    private List<GlamItem> Matches(out int hidden)
    {
        IEnumerable<GlamItem> items = Items.ForSlot(selectedSlot);

        items = items.Where(plugin.CanWear);

        if (selectedSlot.IsWeapon())
        {
            var current = Plugin.ObjectTable.LocalPlayer?.ClassJob.RowId ?? 0;

            if (current != 0)
                items = items.Where(i => i.UsableBy(current));
        }
        else if (selectedJob is { } job)
        {
            items = items.Where(i => i.UsableBy(job));
        }

        if (ownedOnly)
            items = items.Where(i => i.IsNone || plugin.Owned.Contains(i.ItemId));

        if (!string.IsNullOrWhiteSpace(search))
            items = items.Where(i => i.Name.Contains(search, StringComparison.OrdinalIgnoreCase));

        var everything = items.ToList();

        if (!hideStore && !hideSeasonal && !hideExclusive)
        {
            hidden = 0;
            return everything;
        }

        var kept = everything
            .Where(i => i.IsNone
                || ((!hideStore || !i.IsStore)
                    && (!hideSeasonal || !i.IsSeasonal)
                    && (!hideExclusive || !i.IsExclusive)))
            .ToList();

        hidden = everything.Count - kept.Count;

        return kept;
    }
}
