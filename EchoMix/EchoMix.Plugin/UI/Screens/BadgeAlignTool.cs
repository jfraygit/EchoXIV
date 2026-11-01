#if DEBUG
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using EchoMix.Plugin.UI.Controls.V2;
using EchoMix.Plugin.UI.Cosmetics;
using EchoMix.Plugin.UI.Design;
using EchoMix.Shared;

namespace EchoMix.Plugin.UI.Screens;

/// Reports where the badge PLATES actually landed, and rules a guide across them.
internal static class BadgeAlignTool
{
    /// Off unless switched on in Settings > Appearance.
    public static bool Enabled;

    private readonly record struct Plate(Vector2 Position, Vector2 Size);

    /// Recorded during the shelf's draw, read by Draw immediately afterwards in the same frame.
    private static readonly Dictionary<string, Plate> Plates = new();
    private static readonly List<string> Order = new();

    /// Per badge id: X nudge, Y nudge (design pixels), and a multiplier on the catalogue's ArtScale.
    private static readonly Dictionary<string, Vector3> Nudges = new();

    public static Vector3 For(string id) =>
        Nudges.TryGetValue(id, out var value) ? value : new Vector3(0f, 0f, 1f);

    /// Called by the shelf for each badge as it draws, with the rect that badge was given.
    public static void Record(string id, Vector2 position, Vector2 size)
    {
        if (!Enabled)
            return;

        if (!Plates.ContainsKey(id))
            Order.Add(id);

        Plates[id] = new Plate(position, size);
    }

    public static void Draw(IReadOnlyList<DjBadgeDto> badges, float width)
    {
        if (!Enabled || Order.Count == 0)
            return;

        DrawGuides();

        Surfaces.Gap(Metrics.Md);
        Surfaces.SectionHeader("Badge Alignment (debug)", width: width);
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel(width);

        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary,
                "Plate rects as drawn. A magenta line is ruled across the first plate's top and bottom - "
                + "any plate off those lines is a layout fault, and the numbers below say by how much.");

        Surfaces.Gap(Metrics.Md);

        var firstTop = Plates[Order[0]].Position.Y;

        foreach (var id in Order)
        {
            var plate = Plates[id];
            var title = BadgeCatalog.Find(id)?.Title ?? id;
            var deltaY = plate.Position.Y - firstTop;

            var line = string.Format(
                CultureInfo.InvariantCulture,
                "{0}  x={1:0.##}  y={2:0.##}  w={3:0.##}  h={4:0.##}   dY={5:+0.##;-0.##;0}",
                title, plate.Position.X, plate.Position.Y, plate.Size.X, plate.Size.Y, deltaY);

            Surfaces.RowText(line, deltaY == 0f ? Semantic.TextSecondary : Semantic.Danger);
        }

        Surfaces.Gap(Metrics.Md);

        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary,
                "If every dY is 0 and the row still looks stepped, it is the art inside the plates, "
                + "not the plates. Use the nudges below and paste the result into BadgeCatalog.");

        Surfaces.Gap(Metrics.Md);

        foreach (var id in Order)
        {
            if (BadgeCatalog.Find(id) is not { } entry)
                continue;

            using (TypeScale.Body())
                ImGui.TextColored(Semantic.TextPrimary, entry.Title);

            var x = For(id).X;
            if (Fields.Slider($"##badgeX{id}", "Art X", ref x, -20f, 20f, "{0:0.#}", resetTo: 0f))
                Nudges[id] = For(id) with { X = x };

            var y = For(id).Y;
            if (Fields.Slider($"##badgeY{id}", "Art Y", ref y, -20f, 20f, "{0:0.#}", resetTo: 0f))
                Nudges[id] = For(id) with { Y = y };

            var size = For(id).Z;
            if (Fields.Slider($"##badgeS{id}", "Art Size", ref size, 0.6f, 1.6f, "{0:0.###}x", resetTo: 1f))
                Nudges[id] = For(id) with { Z = size };

            var nudge = For(id);
            Surfaces.RowText(
                string.Format(CultureInfo.InvariantCulture,
                    "ArtScale: {0:0.###}f, OffsetX: {1:0.#}f, OffsetY: {2:0.#}f",
                    entry.ArtScale * nudge.Z, nudge.X, nudge.Y),
                Semantic.TextSecondary);

            Fields.Divider();
        }

        if (Fields.Button("Reset All", Fields.ButtonStyle.Secondary,
                height: MathF.Round(Metrics.ControlSm), pill: true))
        {
            Nudges.Clear();
        }

        Surfaces.EndPanel();

        Plates.Clear();
        Order.Clear();
    }

    /// Magenta rules across the first plate's top and bottom edges, and down each plate's left edge.
    private static void DrawGuides()
    {
        var drawList = ImGui.GetWindowDrawList();
        var first = Plates[Order[0]];
        var colour = ImGui.GetColorU32(new Vector4(1f, 0f, 0.85f, 0.9f));

        var left = first.Position.X - (Metrics.Xxl * 2f);
        var right = first.Position.X + (Metrics.Xxl * 2f);

        foreach (var id in Order)
        {
            var plate = Plates[id];
            right = MathF.Max(right, plate.Position.X + plate.Size.X + Metrics.Xxl);

            drawList.AddLine(
                new Vector2(plate.Position.X, plate.Position.Y - Metrics.Md),
                new Vector2(plate.Position.X, plate.Position.Y + plate.Size.Y + Metrics.Md),
                colour, 1f);
        }

        drawList.AddLine(new Vector2(left, first.Position.Y), new Vector2(right, first.Position.Y), colour, 1f);
        drawList.AddLine(
            new Vector2(left, first.Position.Y + first.Size.Y),
            new Vector2(right, first.Position.Y + first.Size.Y), colour, 1f);
    }
}
#endif
