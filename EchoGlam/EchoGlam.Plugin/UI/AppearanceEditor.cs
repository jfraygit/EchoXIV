using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using EchoGlam.Game;
using EchoGlam.UI.Controls;
using LuminaRace = Lumina.Excel.Sheets.Race;
using LuminaTribe = Lumina.Excel.Sheets.Tribe;

namespace EchoGlam.UI;

/// The appearance half of the Dressing Room.
public sealed class AppearanceEditor
{
    private readonly Plugin plugin;

    private CustomizeSet? draft;
    private bool dirty;
    private long lastEditTick;

    /// How long the editor waits after the last change before rebuilding the model.
    private const long ApplyDelayMs = 300;

    public AppearanceEditor(Plugin plugin) => this.plugin = plugin;

    private Wardrobe Wardrobe => plugin.Wardrobe;

    /// The appearance currently being edited, or null if there is no character to read.
    public CustomizeSet? Draft => draft;

    /// Drops the draft so the next frame re-reads the character.
    public void Invalidate() => draft = null;

    public void Draw()
    {
        var scale = UiHelpers.Scale;

        if (!Appearance.LayoutTrusted)
        {
            DrawLayoutFailure();
            return;
        }

        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
        {
            ImGui.TextColored(Theme.TextDim, "No character loaded.");
            return;
        }

        draft ??= new CustomizeSet([.. player.Customize]);

        CommitIfSettled();
        DrawHeader();

        if (draft == null)
            return;

        ImGui.Dummy(new Vector2(0f, 8f * scale));
        DrawBlocks();
    }

    /// Below this the two columns are narrower than a sensible run of swatches, so the layout drops to one.
    private const float TwoColumnMinDesign = 780f;

    /// Lays the feature blocks out in one column or two.
    private void DrawBlocks()
    {
        var scale = UiHelpers.Scale;
        var available = ImGui.GetContentRegionAvail().X;
        var gap = 10f * scale;

        if (available < TwoColumnMinDesign * scale)
        {
            columnWidth = available;
            DrawTallBlocks();
            DrawCompactBlocks();
            return;
        }

        columnWidth = (available - gap) / 2f;

        var startX = ImGui.GetCursorPosX();
        var startY = ImGui.GetCursorPosY();

        ImGui.BeginGroup();
        DrawTallBlocks();
        ImGui.EndGroup();

        var leftBottom = ImGui.GetCursorPosY();

        ImGui.SetCursorPos(new Vector2(startX + columnWidth + gap, startY));

        ImGui.BeginGroup();
        DrawCompactBlocks();
        ImGui.EndGroup();

        ImGui.SetCursorPos(new Vector2(startX, MathF.Max(leftBottom, ImGui.GetCursorPosY())));
    }

    /// How wide a column is this frame.
    private float columnWidth;

    /// Usable width inside the block being drawn, once its padding is taken off.
    private float contentWidth;

    private void DrawTallBlocks()
    {
        var menus = Menus();

        DrawIdentityBlock();
        DrawSkinBlock();
        DrawHairBlock(menus);
        DrawEyesBlock(menus);
    }

    private void DrawCompactBlocks()
    {
        var menus = Menus();

        DrawFaceBlock(menus);
        DrawMouthBlock(menus);
        DrawFeaturesBlock(menus);
        DrawFacePaintBlock(menus);
        DrawBodyBlock(menus);
    }

    private const float BlockPadX = 14f;
    private const float BlockPadY = 13f;
    private const float BlockGap = 11f;

    /// Between one field and the next inside a block.
    private const float RowGap = 13f;

    /// Between a field's label and the control under it.
    private const float LabelGap = 6f;

    private Vector2 blockOrigin;
    private float blockWidth;

    /// Opens a titled block with a panel behind it.
    private void BeginBlock(string title)
    {
        var scale = UiHelpers.Scale;
        var drawList = ImGui.GetWindowDrawList();

        blockOrigin = ImGui.GetCursorScreenPos();
        blockWidth = columnWidth;
        contentWidth = MathF.Max(40f * scale, blockWidth - (BlockPadX * 2f * scale));

        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);

        ImGui.BeginGroup();
        ImGui.Dummy(new Vector2(blockWidth, BlockPadY * scale));
        ImGui.Indent(BlockPadX * scale);

        DrawBlockTitle(title);
    }

    /// A block's heading: an accent tick and tracked small capitals.
    private void DrawBlockTitle(string title)
    {
        var scale = UiHelpers.Scale;
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var lineHeight = ImGui.GetTextLineHeight();

        var tickWidth = 3f * scale;
        var tickTop = origin.Y + (lineHeight * 0.16f);

        drawList.AddRectFilled(
            new Vector2(origin.X, tickTop),
            new Vector2(origin.X + tickWidth, tickTop + (lineHeight * 0.68f)),
            ImGui.GetColorU32(Theme.Accent), 1.5f * scale);

        var x = origin.X + tickWidth + (8f * scale);
        var colour = ImGui.GetColorU32(Theme.Text);
        var tracking = 1.4f * scale;

        foreach (var character in title.ToUpperInvariant())
        {
            var glyph = character.ToString();
            drawList.AddText(new Vector2(x, origin.Y), colour, glyph);
            x += ImGui.CalcTextSize(glyph).X + tracking;
        }

        ImGui.Dummy(new Vector2(0f, lineHeight + (RowGap * scale)));
    }

    private void EndBlock()
    {
        var scale = UiHelpers.Scale;

        ImGui.Unindent(BlockPadX * scale);
        ImGui.Dummy(new Vector2(0f, BlockPadY * scale));
        ImGui.EndGroup();

        var drawList = ImGui.GetWindowDrawList();
        var height = ImGui.GetItemRectMax().Y - blockOrigin.Y;
        var max = blockOrigin + new Vector2(blockWidth, height);
        var rounding = 8f * scale;

        drawList.ChannelsSetCurrent(0);

        for (var i = 3; i >= 1; i--)
        {
            var offset = new Vector2(0f, i * 1.5f * scale);
            drawList.AddRectFilled(
                blockOrigin + offset, max + offset,
                ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.045f * i)), rounding);
        }

        drawList.AddRectFilled(blockOrigin, max, ImGui.GetColorU32(Theme.Panel), rounding);

        drawList.AddRect(
            blockOrigin, max,
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.055f)), rounding, ImDrawFlags.None, 1f * scale);

        drawList.ChannelsMerge();

        ImGui.Dummy(new Vector2(0f, BlockGap * scale));
    }

    private void DrawIdentityBlock()
    {
        BeginBlock("Who you are");

        var set = draft!;

        if (DrawCombo("Race", RaceNames(), set[CustomizeIndex.Race], out var race))
        {
            set[CustomizeIndex.Race] = race;

            set[CustomizeIndex.Tribe] = (byte)(((race - 1) * 2) + 1);
            Touch();
        }

        if (DrawCombo("Clan", TribeNamesFor(set[CustomizeIndex.Race]), set[CustomizeIndex.Tribe], out var tribe))
        {
            set[CustomizeIndex.Tribe] = tribe;
            Touch();
        }

        if (DrawCombo("Gender", GenderNames, set[CustomizeIndex.Sex], out var sex))
        {
            set[CustomizeIndex.Sex] = sex;
            Touch();
        }

        EndBlock();
    }

    private void DrawSkinBlock()
    {
        BeginBlock("Skin");
        DrawColour("Skin Colour", CustomizeIndex.SkinColour);
        EndBlock();
    }

    private void DrawHairBlock(IReadOnlyList<CharaMakeMenu> menus)
    {
        BeginBlock("Hair");

        DrawMenu(menus, CustomizeIndex.Hair, "Style");
        DrawColour("Colour", CustomizeIndex.HairColour);

        var set = draft!;
        DrawToggleRow("Highlights", CustomizeIndex.HasHighlights);

        if ((set[CustomizeIndex.HasHighlights] & Appearance.FlagBit) != 0)
            DrawColour("Highlight Colour", CustomizeIndex.HighlightsColour);

        EndBlock();
    }

    private void DrawEyesBlock(IReadOnlyList<CharaMakeMenu> menus)
    {
        BeginBlock("Eyes");

        DrawMenu(menus, CustomizeIndex.EyeShape, "Shape");
        DrawToggleRow("Small Iris", CustomizeIndex.EyeShape);
        DrawMenu(menus, CustomizeIndex.Eyebrows, "Eyebrows");
        DrawColour("Right Eye", CustomizeIndex.EyeColourRight);
        DrawColour("Left Eye", CustomizeIndex.EyeColourLeft);

        EndBlock();
    }

    private void DrawFaceBlock(IReadOnlyList<CharaMakeMenu> menus)
    {
        BeginBlock("Face");

        DrawMenu(menus, CustomizeIndex.Face, "Shape");
        DrawMenu(menus, CustomizeIndex.Nose, "Nose");
        DrawMenu(menus, CustomizeIndex.Jaw, "Jaw");

        EndBlock();
    }

    private void DrawMouthBlock(IReadOnlyList<CharaMakeMenu> menus)
    {
        BeginBlock("Mouth");

        DrawMenu(menus, CustomizeIndex.Mouth, "Shape");
        DrawColour("Lip Colour", CustomizeIndex.LipColour);

        EndBlock();
    }

    private void DrawFacePaintBlock(IReadOnlyList<CharaMakeMenu> menus)
    {
        BeginBlock("Face Paint");

        DrawMenu(menus, CustomizeIndex.Facepaint, "Design");
        DrawColour("Colour", CustomizeIndex.FacepaintColour);

        EndBlock();
    }

    private void DrawFeaturesBlock(IReadOnlyList<CharaMakeMenu> menus)
    {
        BeginBlock("Facial Features");

        var featureCount = CharaMake.FacialFeatureCount(menus);

        for (var bit = 0; bit < featureCount; bit++)
        {
            DrawBitRow(
                bit == featureCount - 1 ? "Legacy Tattoo" : $"Feature {bit + 1}",
                CustomizeIndex.FacialFeatures, bit);
        }

        DrawColour("Colour", CustomizeIndex.FacialFeatureColour);

        EndBlock();
    }

    private void DrawBodyBlock(IReadOnlyList<CharaMakeMenu> menus)
    {
        BeginBlock("Build");

        DrawSlider(menus, CustomizeIndex.Height, "Height");
        DrawSlider(menus, CustomizeIndex.BodyType, "Muscle");
        DrawSlider(menus, CustomizeIndex.RaceFeatureSize, "Ear / Tail Size");
        DrawMenu(menus, CustomizeIndex.RaceFeatureType, "Ear / Tail Shape");
        DrawSlider(menus, CustomizeIndex.BustSize, "Bust");

        EndBlock();
    }

    /// A slider that says what it is set to.
    private void DrawSlider(IReadOnlyList<CharaMakeMenu> menus, CustomizeIndex index, string label)
    {
        var scale = UiHelpers.Scale;
        var set = draft!;
        var menu = CharaMake.Menu(menus, index);
        var max = menu is { Count: > 1 } ? menu.Count - 1 : 255;

        var value = (int)set[index];
        var left = ImGui.GetCursorPosX();

        var reading = $"{value}";
        var readingWidth = ImGui.CalcTextSize(reading).X;

        ImGui.TextColored(Theme.TextDim, label);
        ImGui.SameLine(0f, 0f);
        ImGui.SetCursorPosX(left + MathF.Max(0f, contentWidth - readingWidth));
        ImGui.TextColored(Theme.Text, reading);

        ImGui.SetCursorPosX(left);
        ImGui.Dummy(new Vector2(0f, LabelGap * scale));
        ImGui.SetCursorPosX(left);

        if (EchoSlider.DrawInt($"##slider{index}", ref value, 0, max, width: contentWidth, format: "{0:0}"))
        {
            set[index] = (byte)Math.Clamp(value, 0, 255);
            Touch();
        }

        ImGui.SetCursorPosX(left);
        ImGui.Dummy(new Vector2(0f, RowGap * scale));
    }

    private void DrawLayoutFailure()
    {
        var scale = UiHelpers.Scale;

        ImGui.Dummy(new Vector2(0f, 20f * scale));
        Theme.SectionHeader("Appearance editing is switched off");
        ImGui.Dummy(new Vector2(0f, 6f * scale));

        ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
        ImGui.TextColored(Theme.Bad,
            "The twenty-six bytes that describe a character no longer sit where EchoGlam expects.");
        ImGui.Dummy(new Vector2(0f, 6f * scale));
        ImGui.TextUnformatted(
            "This is checked against the game on startup, and the check failed - most likely because " +
            "a game update moved them. Editing anyway would mean writing to bytes whose meaning is " +
            "unknown, which is a good way to break a character rather than a reduced feature, so the " +
            "editor stays off until it is corrected. Your gear is unaffected.");
        ImGui.PopTextWrapPos();
    }

    /// The appearance's own undo, and a line saying whose face is on the character.
    private void DrawHeader()
    {
        var scale = UiHelpers.Scale;
        var height = 26f * scale;

        if (EchoButton.Draw(
                "##appearancereset", "Revert Appearance", new Vector2(0f, height),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.UndoAlt,
                enabled: Wardrobe.HasAppearanceOverride,
                tooltip: "Puts your own face back and leaves your gear alone. Revert All in the toolbar takes both."))
        {
            Wardrobe.ClearAppearance();
            draft = null;
            dirty = false;
        }

        ImGui.SameLine(0f, 10f * scale);
        ImGui.AlignTextToFramePadding();

        if (dirty)
            ImGui.TextColored(Theme.TextDim, "Applying...");
        else if (Wardrobe.HasAppearanceOverride)
            ImGui.TextColored(Theme.Accent, "Appearance overridden.");
        else
            ImGui.TextColored(Theme.TextDim, "Your own appearance.");
    }

    /// Pushes the draft once the user has stopped fiddling with it.
    private void CommitIfSettled()
    {
        if (!dirty || draft == null)
            return;

        if (Environment.TickCount64 - lastEditTick < ApplyDelayMs)
            return;

        dirty = false;
        Wardrobe.SetAppearance(draft);
    }

    private void Touch()
    {
        dirty = true;
        lastEditTick = Environment.TickCount64;
    }

    /// The menus for whoever is being edited, refreshed when the race, clan or sex changes.
    private IReadOnlyList<CharaMakeMenu> Menus()
    {
        var set = draft!;
        return CharaMake.For(set[CustomizeIndex.Race], set[CustomizeIndex.Tribe], set[CustomizeIndex.Sex]);
    }

    /// Draws whichever control this field's menu asks for, falling back to a slider when the game has no menu
    /// for it.
    private void DrawMenu(
        IReadOnlyList<CharaMakeMenu> menus, CustomizeIndex index, string label, Action? trailing = null)
    {
        var menu = CharaMake.Menu(menus, index);

        if (menu == null || menu.Options.Count == 0)
        {
            DrawByte(label, index, trailing: trailing);
            return;
        }

        if (menu.Options.Any(o => o.IconId != 0))
            DrawIconGrid(menu, label, trailing);
        else
            DrawChips(menu, label, trailing);
    }

    /// A field's heading: dim label, an optional count, and whatever belongs beside it.
    private static void DrawFieldLabel(string label, int? count, Action? trailing)
    {
        var text = count is { } n ? $"{label}  ({n})" : label;

        if (trailing == null)
        {
            ImGui.TextColored(Theme.TextDim, text);
            return;
        }

        var lineHeight = ImGui.GetTextLineHeight();
        var toggleHeight = EchoToggle.Height;
        var rowHeight = MathF.Max(lineHeight, toggleHeight);
        var top = ImGui.GetCursorPosY();

        ImGui.SetCursorPosY(top + ((rowHeight - lineHeight) / 2f));
        ImGui.TextColored(Theme.TextDim, text);

        ImGui.SameLine(0f, 14f * UiHelpers.Scale);
        ImGui.SetCursorPosY(top + ((rowHeight - toggleHeight) / 2f));
        trailing();

        ImGui.SetCursorPosY(top + rowHeight);
    }

    /// Width of a bare switch, for right-aligning one against a label.
    private const float ToggleWidth = 38f;

    /// A switch over one byte's top bit, for the flags that share a byte with a value.
    private void DrawFlagToggle(string id, string label, CustomizeIndex index)
    {
        var set = draft!;
        var on = (set[index] & Appearance.FlagBit) != 0;

        if (!EchoToggle.Draw(id, label, ref on))
            return;

        set[index] = on
            ? (byte)(set[index] | Appearance.FlagBit)
            : (byte)(set[index] & ~Appearance.FlagBit);

        Touch();
    }

    /// A full-width row: name on the left, switch pinned right.
    private void DrawToggleRow(string label, CustomizeIndex index)
    {
        var scale = UiHelpers.Scale;

        DrawRightAligned(label, ToggleWidth * scale, EchoToggle.Height,
            () => DrawFlagToggle($"##flag{index}", string.Empty, index));
    }

    /// The same row, over a plain bit rather than a byte's flag - the facial features.
    private void DrawBitRow(string label, CustomizeIndex index, int bit)
    {
        var scale = UiHelpers.Scale;
        var set = draft!;

        DrawRightAligned(label, ToggleWidth * scale, EchoToggle.Height, () =>
        {
            var value = set[index];
            var on = (value & (1 << bit)) != 0;

            if (!EchoToggle.Draw($"##bit{index}_{bit}", string.Empty, ref on))
                return;

            set[index] = on
                ? (byte)(value | (1 << bit))
                : (byte)(value & ~(1 << bit));

            Touch();
        });
    }

    /// The value in a byte, with any flag bit taken off.
    private static byte ValueOf(CustomizeSet set, CustomizeIndex index) =>
        Appearance.HasFlagBit(index) ? (byte)(set[index] & 0x7F) : set[index];

    /// Writes a chosen option, preserving a flag bit if the byte carries one.
    private static void SetValue(CustomizeSet set, CustomizeIndex index, byte value)
    {
        set[index] = Appearance.HasFlagBit(index)
            ? (byte)((set[index] & Appearance.FlagBit) | (value & 0x7F))
            : value;
    }

    /// Side of a picture option, in design units.
    private const float OptionIconDesign = 50f;

    private void DrawIconGrid(CharaMakeMenu menu, string label, Action? trailing = null)
    {
        var scale = UiHelpers.Scale;
        var size = OptionIconDesign * scale;
        var set = draft!;

        DrawStacked(label, menu.Options.Count, trailing, () =>
        {
            var (perRow, gap) = GridMetrics(size, 5f * scale, menu.Options.Count);
            var drawList = ImGui.GetWindowDrawList();
            var current = ValueOf(set, menu.Index);
            var rounding = 5f * scale;

            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(gap, 5f * scale));

            for (var i = 0; i < menu.Options.Count; i++)
            {
                if (i % perRow != 0)
                    ImGui.SameLine(0f, gap);

                var option = menu.Options[i];
                var origin = ImGui.GetCursorScreenPos();
                var selected = option.Value == current;

                var clicked = ImGui.InvisibleButton($"##{menu.Index}_{i}", new Vector2(size, size), ImGuiButtonFlags.MouseButtonLeft);
                var hovered = ImGui.IsItemHovered();

                if (clicked)
                {
                    SetValue(set, menu.Index, option.Value);
                    Touch();
                }

                if (hovered)
                    UiHelpers.WrappedTooltip($"{label} {option.Value}");

                var max = origin + new Vector2(size, size);

                drawList.AddRectFilled(
                    origin, max,
                    ImGui.GetColorU32(selected ? Theme.Tinted(0.26f) : hovered ? Theme.Tinted(0.14f) : Theme.Background),
                    rounding);

                var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(option.IconId)).GetWrapOrDefault();
                if (texture != null)
                {
                    var inset = 3f * scale;
                    drawList.AddImage(texture.Handle, origin + new Vector2(inset, inset), max - new Vector2(inset, inset));
                }

                drawList.AddRect(
                    origin, max,
                    ImGui.GetColorU32(selected
                        ? Theme.Accent
                        : new Vector4(1f, 1f, 1f, hovered ? 0.22f : 0.07f)),
                    rounding, ImDrawFlags.None, (selected ? 2f : 1f) * scale);
            }

            ImGui.PopStyleVar();
        });
    }

    /// A row of numbered chips, for the short lists the game gives no pictures for.
    private void DrawChips(CharaMakeMenu menu, string label, Action? trailing = null)
    {
        var scale = UiHelpers.Scale;
        var size = 30f * scale;
        var set = draft!;

        DrawStacked(label, menu.Options.Count, trailing, () =>
        {
            var (perRow, gap) = GridMetrics(size, 5f * scale, menu.Options.Count);
            var drawList = ImGui.GetWindowDrawList();
            var current = ValueOf(set, menu.Index);
            var rounding = 5f * scale;

            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(gap, 5f * scale));

            for (var i = 0; i < menu.Options.Count; i++)
            {
                if (i % perRow != 0)
                    ImGui.SameLine(0f, gap);

                var option = menu.Options[i];
                var origin = ImGui.GetCursorScreenPos();
                var selected = option.Value == current;

                var clicked = ImGui.InvisibleButton($"##{menu.Index}_chip{i}", new Vector2(size, size), ImGuiButtonFlags.MouseButtonLeft);
                var hovered = ImGui.IsItemHovered();

                if (clicked)
                {
                    SetValue(set, menu.Index, option.Value);
                    Touch();
                }

                var max = origin + new Vector2(size, size);

                drawList.AddRectFilled(
                    origin, max,
                    ImGui.GetColorU32(selected ? Theme.Tinted(0.26f) : hovered ? Theme.Tinted(0.14f) : Theme.Background),
                    rounding);

                drawList.AddRect(
                    origin, max,
                    ImGui.GetColorU32(selected
                        ? Theme.Accent
                        : new Vector4(1f, 1f, 1f, hovered ? 0.22f : 0.07f)),
                    rounding, ImDrawFlags.None, (selected ? 2f : 1f) * scale);

                var text = (i + 1).ToString();
                var textSize = ImGui.CalcTextSize(text);
                drawList.AddText(
                    origin + ((new Vector2(size, size) - textSize) / 2f),
                    ImGui.GetColorU32(selected ? Theme.Accent : Theme.TextDim), text);
            }

            ImGui.PopStyleVar();
        });
    }

    /// Side of a colour swatch, in design units.
    private const float SwatchDesign = 26f;

    /// One swatch showing the current colour, which opens the palette when clicked.
    private void DrawColour(string label, CustomizeIndex index)
    {
        var set = draft!;
        var swatches = ColourPalette.For(index, set[CustomizeIndex.Tribe], set[CustomizeIndex.Sex]);

        if (swatches.Count == 0)
        {
            DrawByte(label, index);
            return;
        }

        var scale = UiHelpers.Scale;
        var height = SwatchDesign * scale;
        var width = 46f * scale;
        var current = set[index];
        var colour = ColourFor(swatches, current);

        DrawRightAligned(label, width, height, () =>
        {
            var origin = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            var max = origin + new Vector2(width, height);
            var rounding = 4f * scale;

            var clicked = ImGui.InvisibleButton($"##swatch{index}", new Vector2(width, height), ImGuiButtonFlags.MouseButtonLeft);
            var hovered = ImGui.IsItemHovered();

            if (clicked)
                ImGui.OpenPopup($"##palette{index}");

            drawList.AddRectFilled(origin, max, ImGui.GetColorU32(Theme.Background), rounding);

            var inset = 2f * scale;
            drawList.AddRectFilled(
                origin + new Vector2(inset, inset), max - new Vector2(inset, inset),
                ImGui.GetColorU32(colour), rounding * 0.6f);

            drawList.AddRect(
                origin, max,
                ImGui.GetColorU32(hovered ? Theme.Accent : new Vector4(1f, 1f, 1f, 0.12f)),
                rounding, ImDrawFlags.None, (hovered ? 2f : 1f) * scale);

            if (hovered)
                UiHelpers.WrappedTooltip($"{label} {current}\n{Hex(colour)}\n\nClick to change.");
        });

        DrawPalettePopup(label, index, swatches);
    }

    /// A dim label on the left of the block and something pinned to its right edge.
    private void DrawRightAligned(string label, float width, float height, Action draw)
    {
        var scale = UiHelpers.Scale;
        var gap = 8f * scale;
        var left = ImGui.GetCursorPosX();
        var top = ImGui.GetCursorPosY();
        var labelWidth = ImGui.CalcTextSize(label).X;

        var lineHeight = ImGui.GetTextLineHeight();
        var rowHeight = MathF.Max(lineHeight, height);

        ImGui.SetCursorPosY(top + ((rowHeight - lineHeight) / 2f));
        ImGui.TextColored(Theme.TextDim, label);
        ImGui.SameLine(0f, 0f);

        ImGui.SetCursorPosX(left + MathF.Max(labelWidth + gap, contentWidth - width));
        ImGui.SetCursorPosY(top + ((rowHeight - height) / 2f));
        draw();

        ImGui.SetCursorPos(new Vector2(left, top + rowHeight + (RowGap * scale)));
    }

    /// A label with its control on the line below - for grids and sliders, which want the whole width.
    private void DrawStacked(string label, int? count, Action? trailing, Action draw)
    {
        var scale = UiHelpers.Scale;
        var left = ImGui.GetCursorPosX();

        DrawFieldLabel(label, count, trailing);
        ImGui.Dummy(new Vector2(0f, LabelGap * scale));
        ImGui.SetCursorPosX(left);

        draw();

        ImGui.SetCursorPosX(left);
        ImGui.Dummy(new Vector2(0f, RowGap * scale));
    }

    /// Lays a run of equal cells across the full content width, flush to both edges.
    private (int PerRow, float Gap) GridMetrics(float cell, float gap, int count)
    {
        var perRow = Math.Max(1, (int)((contentWidth + gap) / (cell + gap)));

        if (count < perRow || perRow < 2)
            return (perRow, gap);

        var used = (perRow * cell) + ((perRow - 1) * gap);
        var slack = MathF.Max(0f, contentWidth - used);

        return (perRow, gap + (slack / (perRow - 1)));
    }

    /// The palette itself, in a popup so it floats free of the block holding it.
    private void DrawPalettePopup(string label, CustomizeIndex index, IReadOnlyList<(byte Value, Vector4 Colour)> swatches)
    {
        if (!ImGui.BeginPopup($"##palette{index}"))
            return;

        var scale = UiHelpers.Scale;
        var size = SwatchDesign * scale;
        var gap = 4f * scale;

        const int perRow = 12;

        Theme.SectionHeader(label, ruleWidth: (size * perRow) + (gap * (perRow - 1)));
        ImGui.Dummy(new Vector2(0f, 4f * scale));

        var drawList = ImGui.GetWindowDrawList();
        var set = draft!;
        var current = set[index];

        for (var i = 0; i < swatches.Count; i++)
        {
            if (i % perRow != 0)
                ImGui.SameLine(0f, gap);

            var (value, colour) = swatches[i];
            var origin = ImGui.GetCursorScreenPos();
            var selected = value == current;

            var clicked = ImGui.InvisibleButton($"##{index}_c{i}", new Vector2(size, size), ImGuiButtonFlags.MouseButtonLeft);
            var hovered = ImGui.IsItemHovered();

            if (clicked)
            {
                set[index] = value;
                Touch();
                ImGui.CloseCurrentPopup();
            }

            if (hovered)
                UiHelpers.WrappedTooltip($"{value}\n{Hex(colour)}");

            drawList.AddRectFilled(origin, origin + new Vector2(size, size), ImGui.GetColorU32(colour), 4f * scale);

            var ring = selected
                ? Theme.Accent
                : new Vector4(1f, 1f, 1f, hovered ? 0.55f : 0.15f);

            drawList.AddRect(
                origin, origin + new Vector2(size, size), ImGui.GetColorU32(ring),
                4f * scale, ImDrawFlags.None, (selected ? 2.5f : 1f) * scale);
        }

        ImGui.Dummy(new Vector2(0f, 4f * scale));
        ImGui.EndPopup();
    }

    /// The colour for a stored value, or a dark grey when the value is not in the palette - which happens on
    /// a character whose colour was set outside the ranges the creator offers.
    private static Vector4 ColourFor(IReadOnlyList<(byte Value, Vector4 Colour)> swatches, byte value)
    {
        foreach (var (candidate, colour) in swatches)
        {
            if (candidate == value)
                return colour;
        }

        return new Vector4(0.18f, 0.18f, 0.22f, 1f);
    }

    private static string Hex(Vector4 colour) =>
        $"#{(int)(colour.X * 255):X2}{(int)(colour.Y * 255):X2}{(int)(colour.Z * 255):X2}";

    /// A labelled byte slider that marks the draft dirty when moved.
    private void DrawByte(string label, CustomizeIndex index, int max = 255, Action? trailing = null)
    {
        var scale = UiHelpers.Scale;
        var set = draft!;
        var flagged = Appearance.HasFlagBit(index);

        DrawFieldLabel(label, null, trailing);

        var stored = set[index];
        var value = flagged ? stored & 0x7F : stored;

        if (EchoSlider.DrawInt($"##{index}", ref value, 0, flagged ? Math.Min(max, 0x7F) : max, width: contentWidth, format: "{0:0}"))
        {
            var clamped = (byte)Math.Clamp(value, 0, flagged ? 0x7F : 255);

            set[index] = flagged
                ? (byte)((stored & Appearance.FlagBit) | clamped)
                : clamped;

            Touch();
        }

        ImGui.Dummy(new Vector2(0f, 2f * scale));
    }

    /// A labelled dropdown over (value, name) pairs.
    private bool DrawCombo(
        string label, IReadOnlyList<(byte Value, string Name)> options, byte current, out byte chosen)
    {
        chosen = current;

        var left = ImGui.GetCursorPosX();
        ImGui.TextColored(Theme.TextDim, label);
        ImGui.Dummy(new Vector2(0f, LabelGap * UiHelpers.Scale));
        ImGui.SetCursorPosX(left);

        ImGui.SetNextItemWidth(contentWidth);

        var currentName = "Unknown";
        foreach (var (value, name) in options)
        {
            if (value == current)
            {
                currentName = name;
                break;
            }
        }

        var picked = false;

        if (ImGui.BeginCombo($"##{label}", currentName))
        {
            foreach (var (value, name) in options)
            {
                if (ImGui.Selectable(name, value == current))
                {
                    chosen = value;
                    picked = value != current;
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SetCursorPosX(left);
        ImGui.Dummy(new Vector2(0f, RowGap * UiHelpers.Scale));
        return picked;
    }

    private static readonly (byte Value, string Name)[] GenderNames =
    [
        (0, "Masculine"),
        (1, "Feminine"),
    ];

    /// Race names off the game's own sheet, so they are whatever the client is running in rather than an
    /// English list baked in here.
    private static List<(byte Value, string Name)> RaceNames()
    {
        var names = new List<(byte, string)>();

        try
        {
            foreach (var row in Plugin.DataManager.GetExcelSheet<LuminaRace>())
            {
                if (row.RowId is 0 or > byte.MaxValue)
                    continue;

                var name = row.Masculine.ExtractText();
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(((byte)row.RowId, name));
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[EchoGlam] Could not read the race sheet");
        }

        return names;
    }

    /// The two clans belonging to a race.
    private static List<(byte Value, string Name)> TribeNamesFor(byte race)
    {
        var names = new List<(byte, string)>();

        if (race == 0)
            return names;

        var first = (byte)(((race - 1) * 2) + 1);

        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<LuminaTribe>();

            for (byte id = first; id <= first + 1; id++)
            {
                if (!sheet.TryGetRow(id, out var row))
                    continue;

                var name = row.Masculine.ExtractText();
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add((id, name));
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[EchoGlam] Could not read the tribe sheet");
        }

        return names;
    }
}
