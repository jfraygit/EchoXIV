using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using EchoGlam.Game;
using EchoGlam.UI.Controls;
using EchoGlam.UI.Foundation;

namespace EchoGlam.UI;

/// The appearance half of the Dressing Room: your own face, and the six parts of it you can change.
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


    private enum Part
    {
        Face,
        Eyes,
        Hair,
        Skin,
        Build,
        Race,
    }

    /// A part, its label in the index, and every byte it owns.
    private readonly record struct PartSpec(Part Which, string Label, CustomizeIndex[] Bytes);

    /// IN THE ORDER SOMEBODY ACTUALLY WORKS.
    private static readonly PartSpec[] Parts =
    [
        new(Part.Face, "Face",
        [
            CustomizeIndex.Face, CustomizeIndex.Nose, CustomizeIndex.Jaw,
            CustomizeIndex.Mouth, CustomizeIndex.LipColour,
        ]),
        new(Part.Eyes, "Eyes",
        [
            CustomizeIndex.EyeShape, CustomizeIndex.Eyebrows,
            CustomizeIndex.EyeColourRight, CustomizeIndex.EyeColourLeft,
        ]),
        new(Part.Hair, "Hair",
        [
            CustomizeIndex.Hair, CustomizeIndex.HairColour,
            CustomizeIndex.HasHighlights, CustomizeIndex.HighlightsColour,
        ]),
        new(Part.Skin, "Skin",
        [
            CustomizeIndex.SkinColour, CustomizeIndex.FacialFeatures,
            CustomizeIndex.FacialFeatureColour, CustomizeIndex.Facepaint,
            CustomizeIndex.FacepaintColour,
        ]),
        new(Part.Build, "Build",
        [
            CustomizeIndex.Height, CustomizeIndex.BodyType, CustomizeIndex.RaceFeatureSize,
            CustomizeIndex.RaceFeatureType, CustomizeIndex.BustSize,
        ]),
        new(Part.Race, "Race",
        [
            CustomizeIndex.Race, CustomizeIndex.Tribe, CustomizeIndex.Sex,
        ]),
    ];

    /// Which part is open.
    private Part part = Part.Face;

    /// Set when Revert is pressed, acted on at the END of the frame.
    private bool revertRequested;


    /// Radius of the status lamp's core, and how far its halo carries as a multiple of it.
    private const float LampDesign = 5f;
    private const float LampHalo = 2.4f;

    /// How fast the lamp breathes, in radians a second.
    private const float PulseSpeed = 1.6f;

    /// Side of a picture option, in design units.
    private const float OptionDesign = 50f;

    /// Side of a numbered chip - a nose, a jaw, one facial feature.
    private const float ChipDesign = 32f;

    /// A colour plate: the chosen colour, shown as much of it as a row can carry.
    private const float PlateDesign = 132f;

    /// Side of one swatch in the palette popup, matching the dye grid in the Dressing Room because they are
    /// the same gesture.
    private const float SwatchDesign = 26f;

    /// Narrowest a named race tile may be before the grid drops a column.
    private const float RaceTileDesign = 124f;

    /// Width of a bare switch, for right-aligning one against a label.
    private const float ToggleDesign = 38f;

    /// How far a picture is inset inside its cell, and the radius of an index change dot.
    private const float IconInsetDesign = 3f;

    private const float DotDesign = 2.6f;

    /// Smallest thing somebody is expected to hit, which is what the revert marks are drawn at.
    private static float MarkRoom => Metrics.TouchMin;

    /// A field heading's row, as tall as the mark that sits on it.
    private static float HeadHeight => MathF.Max(Text.CaptionSize * 1.4f, MarkRoom);


    public void Draw()
    {
        if (!Appearance.LayoutTrusted)
        {
            Notice.Fail(
                ImGui.GetContentRegionAvail().X,
                "Appearance Editing Is Switched Off",
                "The twenty-six bytes that describe a character no longer sit where EchoGlam expects, "
                + "which usually means a game update moved them. Editing anyway would mean writing to "
                + "bytes whose meaning is unknown, so the editor stays off until it is corrected. "
                + "Your gear is unaffected.");

            return;
        }

        var player = Plugin.ObjectTable.LocalPlayer;

        if (player == null)
        {
            Notice.Empty(
                ImGui.GetContentRegionAvail().X, FontAwesomeIcon.User,
                "No Character Loaded",
                "There is nobody to read an appearance from yet. This fills in as soon as you are in "
                + "the world.");

            return;
        }

        draft ??= new CustomizeSet([.. player.Customize]);

        CommitIfSettled();

        var halo = EchoButton.GlowReach;

        var available = ImGui.GetContentRegionAvail() - new Vector2(halo, halo);
        var origin = ImGui.GetCursorScreenPos();

        var status = DrawStatus(available.X);

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + status));
        var index = DrawIndex(available.X);

        var consumed = status + index;

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + consumed));

        DrawPart(available.X, MathF.Max(Metrics.RowTall * 3f, available.Y - consumed));

        if (!revertRequested)
            return;

        revertRequested = false;

        Wardrobe.ClearAppearance();
        draft = null;
        dirty = false;
    }


    /// The banner: a lamp, whether this is your own face or an edited one, how far from yours it is, and the
    /// way back.
    private float DrawStatus(float width)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var scale = UiHelpers.Scale;

        var height = DressingRoomTab.BannerHeight;
        var pad = Sp.Xl;

        var overridden = Wardrobe.HasAppearanceOverride;
        var changes = Changes();

        Surface.Paint(
            origin, new Vector2(width, height), SurfaceKind.Raised,
            overridden ? Theme.Accent : null, inside: true);

        var icons = plugin.Fonts.Icon;
        var revertWidth = EchoButton.ContentSize("Revert", icons, FontAwesomeIcon.UndoAlt).X;
        var cursor = ImGui.GetCursorScreenPos();

        ImGui.SetCursorScreenPos(
            new Vector2(origin.X + width - pad - revertWidth, origin.Y + ((height - Metrics.Row) / 2f)));

        if (EchoButton.Draw(
                "##appearancerevert", "Revert", new Vector2(revertWidth, Metrics.Row),
                iconFont: icons, icon: FontAwesomeIcon.UndoAlt,
                enabled: overridden,
                tooltip: overridden
                    ? "Puts your own face back and leaves your gear alone. Revert All under the "
                      + "character takes both."
                    : "Nothing is changed, so there is nothing to put back."))
        {
            revertRequested = true;
        }

        ImGui.SetCursorScreenPos(cursor);


        string headline;
        string meta;
        Vector4 tone;

        if (!overridden)
        {
            headline = "Your Own Face";
            meta = "Nothing here is changed. Edit anything below and it goes on straight away.";
            tone = Col.TextMuted;
        }
        else if (changes == 0)
        {
            headline = "Nothing Different";
            meta = "An appearance is set, and every value in it matches your own.";
            tone = Col.TextMuted;
        }
        else
        {
            headline = changes is { } n
                ? $"{n} {(n == 1 ? "Change" : "Changes")}"
                : "Changed";

            meta = "Your own face is remembered. Revert puts all of it back; the mark beside a field "
                   + "puts one.";

            tone = Theme.Accent;
        }

        if (dirty)
        {
            meta = "Rebuilding your character...";
            tone = Theme.Accent;
        }

        var lamp = LampDesign * scale;
        var reach = lamp * LampHalo;

        var captionLine = Text.CaptionSize * 1.15f;
        var titleLine = Text.TitleSize * 1.2f;
        var block = captionLine + Sp.Xs + titleLine + Sp.Xs + captionLine;
        var top = origin.Y + ((height - block) / 2f);

        var centre = new Vector2(origin.X + pad + reach, 0f);
        var headlineTop = top + captionLine + Sp.Xs;

        var textLeft = centre.X + lamp + Sp.Md;
        var room = MathF.Max(Sp.Xxxl, origin.X + width - pad - revertWidth - Sp.Xl - textLeft);

        using (Text.PushCaption())
        {
            Text.Tracked(
                drawList, new Vector2(textLeft, top), "APPEARANCE",
                Col.U32(overridden ? Theme.Accent : Col.TextMuted), 1.8f, room);
        }

        using (Text.PushTitle())
        {
            drawList.AddText(
                new Vector2(textLeft, headlineTop),
                Col.U32(overridden ? Col.Text : Col.TextDim),
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
                Col.U32(Col.TextDim), Text.Truncate(meta, room));
        }

        var pulse = dirty
            ? 0.5f + (0.5f * MathF.Sin((float)ImGui.GetTime() * PulseSpeed))
            : 0f;

        for (var ring = 3; ring >= 1; ring--)
        {
            drawList.AddCircleFilled(
                centre,
                lamp * (1f + (ring * 0.4f * (dirty ? 0.8f + (0.35f * pulse) : 1f))),
                Col.U32(tone with { W = dirty ? 0.05f + (0.05f * pulse) : 0.03f }), 24);
        }

        drawList.AddCircleFilled(centre, lamp, Col.U32(tone), 24);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));

        return height + Sp.Lg;
    }


    /// The index: six parts in tracked capitals, a lit rule under the open one, and a dot over any part
    /// holding a change.
    private float DrawIndex(float width)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var row = Metrics.RowTall;

        var dot = Sp.Px(DotDesign);
        var dotRoom = Sp.Md;

        Span<float> runs = stackalloc float[Parts.Length];
        Span<float> slots = stackalloc float[Parts.Length];
        Span<float> starts = stackalloc float[Parts.Length];

        var total = 0f;

        using (Text.PushCaption())
        {
            for (var i = 0; i < Parts.Length; i++)
            {
                runs[i] = Text.TrackedWidth(Parts[i].Label.ToUpperInvariant(), 1.8f);

                slots[i] = runs[i] + dotRoom;
                total += slots[i];
            }
        }

        var gap = Parts.Length > 1
            ? MathF.Max(Sp.Xl, (width - total) / (Parts.Length - 1))
            : 0f;

        var x = 0f;

        for (var i = 0; i < Parts.Length; i++)
        {
            starts[i] = x;
            x += slots[i] + gap;
        }

        var open = 0;

        for (var i = 0; i < Parts.Length; i++)
        {
            if (Parts[i].Which != part)
                continue;

            open = i;
            break;
        }

        var slide = Motion.Ease("appearancepart", open, Anim.SelectSpeed);

        var lower = Math.Clamp((int)MathF.Floor(slide), 0, Parts.Length - 1);
        var upper = Math.Clamp(lower + 1, 0, Parts.Length - 1);
        var t = slide - lower;

        var markX = origin.X + starts[lower] + ((starts[upper] - starts[lower]) * t);
        var markWidth = runs[lower] + ((runs[upper] - runs[lower]) * t);
        var markY = origin.Y + row - Stroke.Mark;

        var markMin = new Vector2(markX, markY);
        var markMax = new Vector2(markX + markWidth, markY + Stroke.Mark);

        Elev.Glow(drawList, markMin, markMax, Theme.Accent, 0.44f, Stroke.Mark, 4f);
        drawList.AddRectFilled(markMin, markMax, Col.U32(Theme.Accent), Stroke.Mark / 2f);


        var captionLine = Text.CaptionSize * 1.15f;
        var labelY = origin.Y + ((row - Stroke.Mark - captionLine) / 2f);

        using (Text.PushCaption())
        {
            for (var i = 0; i < Parts.Length; i++)
            {
                var min = new Vector2(origin.X + starts[i], origin.Y);

                ImGui.SetCursorScreenPos(min);

                var hit = ImGui.InvisibleButton(
                    $"##part{Parts[i].Which}", new Vector2(slots[i], row),
                    ImGuiButtonFlags.MouseButtonLeft);

                var hovered = ImGui.IsItemHovered();

                if (hovered)
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

                if (hit)
                    part = Parts[i].Which;

                var covered = Math.Clamp(1f - MathF.Abs(slide - i), 0f, 1f);
                var resting = hovered ? Col.Text : Col.TextDim;

                Text.Tracked(
                    drawList, new Vector2(min.X, labelY), Parts[i].Label.ToUpperInvariant(),
                    Col.U32(Vector4.Lerp(resting, Theme.Accent, covered)), 1.8f);

                var count = ChangesIn(Parts[i]);

                if (count == 0)
                    continue;

                if (hovered)
                {
                    UiHelpers.WrappedTooltip(
                        $"{count} {(count == 1 ? "change" : "changes")} in {Parts[i].Label.ToLowerInvariant()}.");
                }

                drawList.AddCircleFilled(
                    new Vector2(min.X + runs[i] + (dotRoom / 2f), labelY + (captionLine * 0.3f)),
                    dot, Col.U32(Theme.Accent), 16);
            }
        }

        var ruleY = origin.Y + row + Sp.Md;

        drawList.AddRectFilled(
            new Vector2(origin.X, ruleY), new Vector2(origin.X + width, ruleY + Stroke.Hair),
            Col.U32(Col.EdgeRest with { W = 0.55f }));

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, row));

        return row + Sp.Md + Stroke.Hair + Sp.Lg;
    }


    /// Screen x every field in the open part lines up on, and how much room a field's control has.
    private float fieldLeft;

    private float content;

    /// Screen x of the revert marks' column.
    private float MarkX => fieldLeft + content + Sp.Lg;

    private void DrawPart(float width, float height)
    {
        if (ImGui.BeginChild(
                "##appearancepart", new Vector2(width, height), false,
                ImGuiWindowFlags.AlwaysVerticalScrollbar))
        {
            var origin = ImGui.GetCursorScreenPos();
            var avail = ImGui.GetContentRegionAvail().X;

            fieldLeft = origin.X;
            content = MathF.Max(Sp.Xxxl, avail - MarkRoom - Sp.Lg);

            var menus = Menus();

            switch (part)
            {
                case Part.Eyes:
                    DrawEyes(menus);
                    break;

                case Part.Hair:
                    DrawHair(menus);
                    break;

                case Part.Skin:
                    DrawSkin(menus);
                    break;

                case Part.Build:
                    DrawBuild(menus);
                    break;

                case Part.Race:
                    DrawRace();
                    break;

                default:
                    DrawFace(menus);
                    break;
            }

            ImGui.Dummy(new Vector2(content, Sp.Xs));
        }

        ImGui.EndChild();
    }

    private void DrawFace(IReadOnlyList<CharaMakeMenu> menus)
    {
        DrawMenu(menus, CustomizeIndex.Face, "Shape");
        DrawMenu(menus, CustomizeIndex.Nose, "Nose");
        DrawMenu(menus, CustomizeIndex.Jaw, "Jaw");
        DrawMenu(menus, CustomizeIndex.Mouth, "Mouth");
        DrawColour("Lip Colour", CustomizeIndex.LipColour);
    }

    private void DrawEyes(IReadOnlyList<CharaMakeMenu> menus)
    {
        DrawMenu(menus, CustomizeIndex.EyeShape, "Shape");

        DrawFlagRow("Small Iris", CustomizeIndex.EyeShape, marked: false);

        DrawMenu(menus, CustomizeIndex.Eyebrows, "Eyebrows");
        DrawColour("Right Eye", CustomizeIndex.EyeColourRight);
        DrawColour("Left Eye", CustomizeIndex.EyeColourLeft);
    }

    private void DrawHair(IReadOnlyList<CharaMakeMenu> menus)
    {
        DrawMenu(menus, CustomizeIndex.Hair, "Style");
        DrawColour("Colour", CustomizeIndex.HairColour);
        DrawFlagRow("Highlights", CustomizeIndex.HasHighlights, marked: true);

        if ((draft![CustomizeIndex.HasHighlights] & Appearance.FlagBit) != 0)
            DrawColour("Highlight Colour", CustomizeIndex.HighlightsColour);
    }

    private void DrawSkin(IReadOnlyList<CharaMakeMenu> menus)
    {
        DrawColour("Colour", CustomizeIndex.SkinColour);

        DrawFeatures(menus);
        DrawColour("Feature Colour", CustomizeIndex.FacialFeatureColour);

        DrawMenu(menus, CustomizeIndex.Facepaint, "Face Paint");
        DrawColour("Paint Colour", CustomizeIndex.FacepaintColour);
    }

    private void DrawBuild(IReadOnlyList<CharaMakeMenu> menus)
    {
        DrawMenuSlider(menus, CustomizeIndex.Height, "Height");
        DrawMenuSlider(menus, CustomizeIndex.BodyType, "Muscle");
        DrawMenuSlider(menus, CustomizeIndex.RaceFeatureSize, "Ear / Tail Size");
        DrawMenu(menus, CustomizeIndex.RaceFeatureType, "Ear / Tail Shape");
        DrawMenuSlider(menus, CustomizeIndex.BustSize, "Bust");
    }

    /// Race, clan and gender.
    private void DrawRace()
    {
        var set = draft!;

        DrawRaceGrid();

        var tribes = TribesFor(set[CustomizeIndex.Race], set[CustomizeIndex.Sex]);

        if (tribes.Count == 2)
        {
            ReadOnlySpan<string> labels = [tribes[0].Name, tribes[1].Name];
            var current = set[CustomizeIndex.Tribe] == tribes[1].Value ? 1 : 0;
            var picked = DrawSegment("##clan", "Clan", labels, current, CustomizeIndex.Tribe);

            if (picked != current)
            {
                set[CustomizeIndex.Tribe] = tribes[picked].Value;
                Reconcile();
                Touch();
            }
        }

        {
            ReadOnlySpan<string> labels = ["Masculine", "Feminine"];
            var current = set[CustomizeIndex.Sex] == 1 ? 1 : 0;
            var picked = DrawSegment("##gender", "Gender", labels, current, CustomizeIndex.Sex);

            if (picked != current)
            {
                set[CustomizeIndex.Sex] = (byte)picked;
                Reconcile();
                Touch();
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(fieldLeft, ImGui.GetCursorScreenPos().Y));

        using (Text.PushCaption())
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Col.TextMuted);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + content);

            ImGui.TextUnformatted(
                "Changing any of these three puts anything the new race, clan or gender does not "
                + "offer back to its default - the game's own, not a guess.");

            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();
        }
    }


    /// A stacked field's heading: tracked capitals, how many choices there are, whatever reading belongs at
    /// the far end, and the mark that reverts it.
    private float Head(Vector2 at, string label, int? count, CustomizeIndex index, string? reading = null)
    {
        var drawList = ImGui.GetWindowDrawList();
        var head = HeadHeight;
        var textY = at.Y + ((head - Text.CaptionSize) / 2f);

        var readingWidth = reading is null ? 0f : Text.Width(reading);

        using (Text.PushCaption())
        {
            var used = Text.Tracked(
                drawList, new Vector2(fieldLeft, textY), label.ToUpperInvariant(),
                Col.U32(Col.TextMuted), 1.8f,
                MathF.Max(Sp.Xl, content - readingWidth - Sp.Xl));

            if (count is { } n)
            {
                drawList.AddText(
                    new Vector2(fieldLeft + used + Sp.Md, textY),
                    Col.U32(Col.TextMuted with { W = 0.5f }), n.ToString());
            }
        }

        if (reading is not null)
        {
            drawList.AddText(
                new Vector2(fieldLeft + content - readingWidth, at.Y + ((head - ImGui.GetTextLineHeight()) / 2f)),
                Col.U32(Col.Text), reading);
        }

        DrawMark(index, at.Y + ((head - MarkRoom) / 2f));

        return head + Sp.Sm;
    }

    /// The mark that puts one field back, drawn only where there is something to put back.
    private void DrawMark(CustomizeIndex index, float y)
    {
        if (!ChangedAt(index))
            return;

        var cursor = ImGui.GetCursorScreenPos();

        ImGui.SetCursorScreenPos(new Vector2(MarkX, y));

        if (EchoButton.BareIcon(
                $"##revert{index}", plugin.Fonts.Icon, FontAwesomeIcon.UndoAlt, MarkRoom,
                "Changed from your own. Click to put just this one back.",
                colourOverride: Theme.Accent))
        {
            RevertField(index);
        }

        ImGui.SetCursorScreenPos(cursor);
    }

    /// A row: the field's name on the left, one control pinned to the right of the content column, and the
    /// mark beyond it.
    private void Setting(string label, CustomizeIndex? index, float controlWidth, float controlHeight, Action draw)
    {
        var drawList = ImGui.GetWindowDrawList();
        var at = ImGui.GetCursorScreenPos();
        var line = ImGui.GetTextLineHeight();

        var height = MathF.Max(MathF.Max(line, controlHeight), MarkRoom);

        drawList.AddText(
            new Vector2(fieldLeft, at.Y + ((height - line) / 2f)),
            Col.U32(Col.TextDim),
            Text.Truncate(label, MathF.Max(Sp.Xl, content - controlWidth - Sp.Xl)));

        ImGui.SetCursorScreenPos(
            new Vector2(fieldLeft + content - controlWidth, at.Y + ((height - controlHeight) / 2f)));

        draw();

        if (index is { } owned)
            DrawMark(owned, at.Y + ((height - MarkRoom) / 2f));

        ImGui.SetCursorScreenPos(new Vector2(fieldLeft, at.Y + height + Sp.Lg));
    }

    /// Draws whichever control this field's menu asks for, falling back to a slider when the game has no menu
    /// for it.
    private void DrawMenu(IReadOnlyList<CharaMakeMenu> menus, CustomizeIndex index, string label)
    {
        var menu = CharaMake.Menu(menus, index);

        if (menu is null || menu.Options.Count == 0)
        {
            DrawSlider(label, index, 255);
            return;
        }

        var pictures = false;

        foreach (var option in menu.Options)
        {
            if (option.IconId == 0)
                continue;

            pictures = true;
            break;
        }

        if (pictures)
            DrawOptions(menu, label);
        else
            DrawChips(menu, label);
    }

    /// A grid of pictures, on a floor of its own - hairstyles, faces, face paint.
    private void DrawOptions(CharaMakeMenu menu, string label)
    {
        var at = ImGui.GetCursorScreenPos();
        var head = Head(at, label, menu.Options.Count, menu.Index);

        var size = Sp.Px(OptionDesign);
        var pad = Sp.Lg;
        var step = Sp.Xs;

        var inner = MathF.Max(size, content - (pad * 2f));
        var (perRow, gap) = Spread(inner, size, step, menu.Options.Count);
        var rows = (menu.Options.Count + perRow - 1) / perRow;

        var wellMin = new Vector2(fieldLeft, at.Y + head);
        var wellHeight = (rows * size) + ((rows - 1) * step) + (pad * 2f);

        Surface.Paint(wellMin, new Vector2(content, wellHeight), SurfaceKind.Well);

        var drawList = ImGui.GetWindowDrawList();
        var set = draft!;
        var current = ValueOf(set, menu.Index);
        var inset = Sp.Px(IconInsetDesign);

        for (var i = 0; i < menu.Options.Count; i++)
        {
            var option = menu.Options[i];

            var min = new Vector2(
                wellMin.X + pad + ((i % perRow) * (size + gap)),
                wellMin.Y + pad + ((i / perRow) * (size + step)));

            var max = min + new Vector2(size, size);

            ImGui.SetCursorScreenPos(min);

            var clicked = ImGui.InvisibleButton(
                $"##{menu.Index}_{i}", new Vector2(size, size), ImGuiButtonFlags.MouseButtonLeft);

            var hovered = ImGui.IsItemHovered();

            if (clicked)
            {
                SetValue(set, menu.Index, option.Value);
                Touch();
            }

            if (hovered)
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            Cell.Paint(drawList, min, max, option.Value == current, hovered);

            var texture = Plugin.TextureProvider
                .GetFromGameIcon(new GameIconLookup(option.IconId)).GetWrapOrDefault();

            if (texture != null)
            {
                drawList.AddImage(
                    texture.Handle, min + new Vector2(inset, inset), max - new Vector2(inset, inset));
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(fieldLeft, wellMin.Y + wellHeight + Sp.Lg));
    }

    /// A row of numbered chips, for the short lists the game gives no pictures for.
    private void DrawChips(CharaMakeMenu menu, string label)
    {
        var at = ImGui.GetCursorScreenPos();
        var head = Head(at, label, menu.Options.Count, menu.Index);

        var size = Sp.Px(ChipDesign);
        var step = Sp.Xs;
        var (perRow, gap) = Spread(content, size, step, menu.Options.Count);
        var rows = (menu.Options.Count + perRow - 1) / perRow;

        var drawList = ImGui.GetWindowDrawList();
        var set = draft!;
        var current = ValueOf(set, menu.Index);
        var top = at.Y + head;

        for (var i = 0; i < menu.Options.Count; i++)
        {
            var option = menu.Options[i];

            var min = new Vector2(
                fieldLeft + ((i % perRow) * (size + gap)),
                top + ((i / perRow) * (size + step)));

            var selected = option.Value == current;

            ImGui.SetCursorScreenPos(min);

            var clicked = ImGui.InvisibleButton(
                $"##{menu.Index}_chip{i}", new Vector2(size, size), ImGuiButtonFlags.MouseButtonLeft);

            var hovered = ImGui.IsItemHovered();

            if (clicked)
            {
                SetValue(set, menu.Index, option.Value);
                Touch();
            }

            if (hovered)
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            PaintNumber(drawList, min, size, (i + 1).ToString(), selected, hovered);
        }

        ImGui.SetCursorScreenPos(
            new Vector2(fieldLeft, top + (rows * size) + ((rows - 1) * step) + Sp.Lg));
    }

    /// The facial features: independent marks packed into one byte, as a row of chips that light up.
    private void DrawFeatures(IReadOnlyList<CharaMakeMenu> menus)
    {
        var set = draft!;
        var total = CharaMake.FacialFeatureCount(menus);
        var marks = Math.Max(0, total - 1);
        var index = CustomizeIndex.FacialFeatures;

        if (marks > 0)
        {
            var at = ImGui.GetCursorScreenPos();
            var head = Head(at, "Features", marks, index);

            var size = Sp.Px(ChipDesign);
            var step = Sp.Xs;
            var (perRow, gap) = Spread(content, size, step, marks);
            var rows = (marks + perRow - 1) / perRow;

            var drawList = ImGui.GetWindowDrawList();
            var top = at.Y + head;

            for (var bit = 0; bit < marks; bit++)
            {
                var min = new Vector2(
                    fieldLeft + ((bit % perRow) * (size + gap)),
                    top + ((bit / perRow) * (size + step)));

                var on = (set[index] & (1 << bit)) != 0;

                ImGui.SetCursorScreenPos(min);

                var clicked = ImGui.InvisibleButton(
                    $"##feature{bit}", new Vector2(size, size), ImGuiButtonFlags.MouseButtonLeft);

                var hovered = ImGui.IsItemHovered();

                if (clicked)
                {
                    set[index] = on
                        ? (byte)(set[index] & ~(1 << bit))
                        : (byte)(set[index] | (1 << bit));

                    Touch();
                }

                if (hovered)
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

                PaintNumber(drawList, min, size, (bit + 1).ToString(), on, hovered, glow: false);
            }

            ImGui.SetCursorScreenPos(
                new Vector2(fieldLeft, top + (rows * size) + ((rows - 1) * step) + Sp.Lg));
        }

        DrawBitRow("Legacy Tattoo", index, Math.Max(0, total - 1), marked: marks == 0);
    }

    /// A chip with a number in it, in the same material every other cell on the page is made of.
    private static void PaintNumber(
        ImDrawListPtr drawList, Vector2 min, float size, string text, bool selected, bool hovered,
        bool glow = true)
    {
        Cell.Paint(drawList, min, min + new Vector2(size, size), selected, hovered, glow);

        var textSize = ImGui.CalcTextSize(text);

        drawList.AddText(
            min + ((new Vector2(size, size) - textSize) / 2f),
            Col.U32(Cell.Ink(selected, hovered)), text);
    }

    /// A switch over one byte's top bit, for the flags that share a byte with a value.
    private void DrawFlagRow(string label, CustomizeIndex index, bool marked)
    {
        var set = draft!;
        var on = (set[index] & Appearance.FlagBit) != 0;

        Setting(label, marked ? index : null, Sp.Px(ToggleDesign), EchoToggle.Height, () =>
        {
            var wanted = on;

            if (!EchoToggle.Draw($"##flag{index}", string.Empty, ref wanted))
                return;

            set[index] = wanted
                ? (byte)(set[index] | Appearance.FlagBit)
                : (byte)(set[index] & ~Appearance.FlagBit);

            Touch();
        });
    }

    /// The same row over a plain bit rather than a byte's flag.
    private void DrawBitRow(string label, CustomizeIndex index, int bit, bool marked)
    {
        var set = draft!;

        Setting(label, marked ? index : null, Sp.Px(ToggleDesign), EchoToggle.Height, () =>
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

    /// One plate showing the current colour, which opens the palette when clicked.
    private void DrawColour(string label, CustomizeIndex index)
    {
        var set = draft!;
        var swatches = ColourPalette.For(index, set[CustomizeIndex.Tribe], set[CustomizeIndex.Sex]);

        if (swatches.Count == 0)
        {
            DrawSlider(label, index, 255);
            return;
        }

        var width = Sp.Px(PlateDesign);
        var height = Metrics.RowTall;
        var current = set[index];
        var colour = ColourFor(swatches, current);

        Setting(label, index, width, height, () =>
        {
            var drawList = ImGui.GetWindowDrawList();
            var origin = ImGui.GetCursorScreenPos();
            var max = origin + new Vector2(width, height);

            var clicked = ImGui.InvisibleButton(
                $"##swatch{index}", new Vector2(width, height), ImGuiButtonFlags.MouseButtonLeft);

            var hovered = ImGui.IsItemHovered();

            if (clicked)
                ImGui.OpenPopup($"##palette{index}");

            if (hovered)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                UiHelpers.WrappedTooltip($"{label}  {Hex(colour)}\n\nClick to change.");
            }

            drawList.AddRectFilled(origin, max, Col.U32(Col.Sunken), Rad.Ctl);

            var inset = Sp.Xxs;

            drawList.AddRectFilled(
                origin + new Vector2(inset, inset), max - new Vector2(inset, inset),
                Col.U32(colour), Rad.Ctl * 0.6f);

            drawList.AddRect(
                origin, max,
                Col.U32(hovered ? Theme.Accent : Col.EdgeRest with { W = 0.6f }),
                Rad.Ctl, ImDrawFlags.None, hovered ? Stroke.Edge : Stroke.Hair);
        });

        DrawPalette(label, index, swatches);
    }

    /// A slider that says what it is set to.
    private void DrawSlider(string label, CustomizeIndex index, int max)
    {
        var set = draft!;
        var flagged = Appearance.HasFlagBit(index);
        var ceiling = flagged ? Math.Min(max, 0x7F) : max;

        var value = (int)ValueOf(set, index);
        var at = ImGui.GetCursorScreenPos();

        var head = Head(at, label, null, index, value.ToString());

        ImGui.SetCursorScreenPos(new Vector2(fieldLeft, at.Y + head));

        if (EchoSlider.DrawInt($"##slider{index}", ref value, 0, ceiling, width: content, format: "{0:0}"))
        {
            SetValue(set, index, (byte)Math.Clamp(value, 0, ceiling));
            Touch();
        }

        var bottom = ImGui.GetItemRectMax().Y;

        ImGui.SetCursorScreenPos(new Vector2(fieldLeft, bottom + Sp.Lg));
    }

    /// The same slider, with its range taken from the game's own menu where there is one.
    private void DrawMenuSlider(IReadOnlyList<CharaMakeMenu> menus, CustomizeIndex index, string label)
    {
        var menu = CharaMake.Menu(menus, index);

        DrawSlider(label, index, menu is { Count: > 1 } ? menu.Count - 1 : 255);
    }

    /// Every race as a named tile, in the material the option grids are made of.
    private void DrawRaceGrid()
    {
        var races = RaceNames.All;

        if (races.Length == 0)
        {
            DrawSlider("Race", CustomizeIndex.Race, 255);
            return;
        }

        var set = draft!;
        var at = ImGui.GetCursorScreenPos();
        var head = Head(at, "Race", races.Length, CustomizeIndex.Race);

        var height = Metrics.RowTall;
        var step = Sp.Xs;
        var minimum = Sp.Px(RaceTileDesign);

        var perRow = Math.Max(1, (int)((content + step) / (minimum + step)));
        var tile = (content - ((perRow - 1) * step)) / perRow;
        var rows = (races.Length + perRow - 1) / perRow;

        var drawList = ImGui.GetWindowDrawList();
        var top = at.Y + head;

        for (var i = 0; i < races.Length; i++)
        {
            var (id, name) = races[i];

            var min = new Vector2(
                fieldLeft + ((i % perRow) * (tile + step)),
                top + ((i / perRow) * (height + step)));

            var selected = id == set[CustomizeIndex.Race];

            ImGui.SetCursorScreenPos(min);

            var clicked = ImGui.InvisibleButton(
                $"##race{id}", new Vector2(tile, height), ImGuiButtonFlags.MouseButtonLeft);

            var hovered = ImGui.IsItemHovered();

            if (hovered)
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            if (clicked && !selected)
            {
                set[CustomizeIndex.Race] = id;

                set[CustomizeIndex.Tribe] = (byte)(((id - 1) * 2) + 1);

                Reconcile();
                Touch();
            }

            Cell.Paint(drawList, min, min + new Vector2(tile, height), selected, hovered);

            var room = MathF.Max(Sp.Xs, tile - (Sp.Md * 2f));
            var text = Text.Truncate(name, room);

            drawList.AddText(
                new Vector2(
                    min.X + ((tile - Text.Width(text)) / 2f),
                    min.Y + ((height - ImGui.GetTextLineHeight()) / 2f)),
                Col.U32(Cell.Ink(selected, hovered)), text);
        }

        ImGui.SetCursorScreenPos(
            new Vector2(fieldLeft, top + (rows * height) + ((rows - 1) * step) + Sp.Lg));
    }

    /// A row whose control is a segmented choice.
    private int DrawSegment(
        string id, string label, ReadOnlySpan<string> labels, int selected, CustomizeIndex index)
    {
        var drawList = ImGui.GetWindowDrawList();
        var at = ImGui.GetCursorScreenPos();
        var line = ImGui.GetTextLineHeight();

        var segment = EchoSegment.Width(labels);
        var height = MathF.Max(Metrics.RowTall, MarkRoom);

        drawList.AddText(
            new Vector2(fieldLeft, at.Y + ((height - line) / 2f)),
            Col.U32(Col.TextDim),
            Text.Truncate(label, MathF.Max(Sp.Xl, content - segment - Sp.Xl)));

        ImGui.SetCursorScreenPos(new Vector2(fieldLeft + content - segment, at.Y));

        var picked = EchoSegment.Draw(id, labels, selected, height);

        DrawMark(index, at.Y + ((height - MarkRoom) / 2f));

        ImGui.SetCursorScreenPos(new Vector2(fieldLeft, at.Y + height + Sp.Lg));

        return picked;
    }

    /// Lays a run of equal cells across the width, flush to both edges.
    private static (int PerRow, float Gap) Spread(float width, float cell, float gap, int count)
    {
        var perRow = Math.Max(1, (int)((width + gap) / (cell + gap)));

        if (count < perRow || perRow < 2)
            return (perRow, gap);

        var used = (perRow * cell) + ((perRow - 1) * gap);
        var slack = MathF.Max(0f, width - used);

        return (perRow, gap + (slack / (perRow - 1)));
    }

    /// The palette itself, in a popup so it floats free of the row holding it.
    private void DrawPalette(
        string label, CustomizeIndex index, IReadOnlyList<(byte Value, Vector4 Colour)> swatches)
    {
        if (!ImGui.BeginPopup($"##palette{index}"))
            return;

        var size = Sp.Px(SwatchDesign);
        var gap = Sp.Xs;

        const int perRow = 12;

        var width = (size * perRow) + (gap * (perRow - 1));
        var drawList = ImGui.GetWindowDrawList();
        var head = ImGui.GetCursorScreenPos();

        using (Text.PushCaption())
            Text.Tracked(drawList, head, label.ToUpperInvariant(), Col.U32(Col.TextMuted), 1.8f, width);

        var top = head.Y + (Text.CaptionSize * 1.15f) + Sp.Md;

        var set = draft!;
        var current = set[index];

        for (var i = 0; i < swatches.Count; i++)
        {
            var (value, colour) = swatches[i];
            var selected = value == current;

            var min = new Vector2(
                head.X + ((i % perRow) * (size + gap)),
                top + ((i / perRow) * (size + gap)));

            ImGui.SetCursorScreenPos(min);

            var clicked = ImGui.InvisibleButton(
                $"##{index}_c{i}", new Vector2(size, size), ImGuiButtonFlags.MouseButtonLeft);

            var hovered = ImGui.IsItemHovered();

            if (clicked)
            {
                set[index] = value;
                Touch();
                ImGui.CloseCurrentPopup();
            }

            if (hovered)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                UiHelpers.WrappedTooltip(Hex(colour));
            }

            Vector4? ring = null;

            if (selected)
                ring = Theme.Accent;
            else if (hovered)
                ring = new Vector4(1f, 1f, 1f, 0.85f);

            UiHelpers.DrawSwatch(drawList, min, size, colour, ring);
        }

        var rows = (swatches.Count + perRow - 1) / perRow;

        ImGui.SetCursorScreenPos(head);
        ImGui.Dummy(new Vector2(width, (top - head.Y) + (rows * (size + gap))));

        ImGui.EndPopup();
    }


    /// Your own appearance, sampled once a frame.
    private CustomizeSet? baseline;

    private int baselineFrame = -1;

    private CustomizeSet? Baseline()
    {
        var frame = ImGui.GetFrameCount();

        if (baselineFrame == frame)
            return baseline;

        baselineFrame = frame;
        baseline = Wardrobe.BaseAppearance;

        return baseline;
    }

    /// Whether one field differs from the character you actually have.
    private bool ChangedAt(CustomizeIndex index) =>
        Baseline() is { } mine && draft is { } set && set[index] != mine[index];

    /// How many bytes in one part differ from your own.
    private int ChangesIn(PartSpec spec)
    {
        if (Baseline() is not { } mine || draft is not { } set)
            return 0;

        var count = 0;

        foreach (var index in spec.Bytes)
        {
            if (set[index] != mine[index])
                count++;
        }

        return count;
    }

    /// How many differ across the whole appearance, or null when there is nothing to compare against.
    private int? Changes()
    {
        if (Baseline() is null || draft is null)
            return null;

        var count = 0;

        foreach (var spec in Parts)
            count += ChangesIn(spec);

        return count;
    }

    /// Puts one byte back to the character's own, and leaves the other twenty-five alone.
    private void RevertField(CustomizeIndex index)
    {
        if (Baseline() is not { } mine || draft is not { } set)
            return;

        set[index] = mine[index];
        Touch();
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

    /// Puts any value the new race, clan or gender does not offer back to the game's own default.
    private void Reconcile()
    {
        var set = draft!;

        foreach (var menu in Menus())
        {
            if (menu.Index is CustomizeIndex.Race or CustomizeIndex.Tribe or CustomizeIndex.Sex)
                continue;

            if (menu.Kind == CharaMakeKind.Colour)
            {
                var swatches = ColourPalette.For(
                    menu.Index, set[CustomizeIndex.Tribe], set[CustomizeIndex.Sex]);

                if (swatches.Count == 0)
                    continue;

                var held = set[menu.Index];
                var known = false;

                foreach (var (value, _) in swatches)
                {
                    if (value != held)
                        continue;

                    known = true;
                    break;
                }

                if (!known)
                    set[menu.Index] = menu.Initial;

                continue;
            }

            if (menu.Options.Count == 0)
                continue;

            var current = ValueOf(set, menu.Index);
            var offered = false;

            foreach (var option in menu.Options)
            {
                if (option.Value != current)
                    continue;

                offered = true;
                break;
            }

            if (!offered)
                SetValue(set, menu.Index, menu.Initial);
        }
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

    /// The two clans belonging to a race, named for the gender being edited.
    private static List<(byte Value, string Name)> TribesFor(byte race, byte sex)
    {
        List<(byte, string)> names = [];

        if (race == 0)
            return names;

        var first = (byte)(((race - 1) * 2) + 1);

        for (var id = first; id <= first + 1; id++)
        {
            var name = RaceNames.Tribe(id, sex);

            if (!string.IsNullOrWhiteSpace(name))
                names.Add((id, name));
        }

        return names;
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
}
