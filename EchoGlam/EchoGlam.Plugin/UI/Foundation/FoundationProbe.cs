using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace EchoGlam.UI.Foundation;

/// The six scenes that decide whether EchoGlam 2.0 can have nested surfaces.
internal static class FoundationProbe
{
    /// THESE SCENES NOW DRIVE THE REAL Surface, not a stand-in.
    private static SurfaceScope Begin(float width, Vector4 accent) =>
        Surface.Begin(width, SurfaceKind.Card, accent, padding: 10f);

    /// Four accents, far enough apart that a background painted into the wrong layer is visible rather than
    /// inferred.
    private static readonly Vector4[] Tints =
    [
        new(0.55f, 0.35f, 0.85f, 1f),
        new(0.90f, 0.44f, 0.76f, 1f),
        new(0.40f, 0.75f, 0.95f, 1f),
        new(0.95f, 0.70f, 0.35f, 1f),
    ];

    private const string Paragraph =
        "This paragraph exists to be wrapped, because a surface that takes its height from its " +
        "contents can only be tested by something whose height is not known until it has wrapped. " +
        "If this text runs outside the innermost card, the wrap position is being handed a screen " +
        "coordinate where a window-local one was wanted. If the card behind it is the wrong colour, " +
        "a background has been painted into another surface's layer.";

    private static bool fade;
    private static bool modalQueued;

    public static void Draw()
    {
        var scale = UiHelpers.Scale;

        ImGui.TextColored(
            new Vector4(0.62f, 0.62f, 0.70f, 1f),
            $"scale {scale:0.00}   font {ImGui.GetFontSize():0.0}px   depth cap {LayerStack.MaxDepth}");

        ImGui.Checkbox("Push style Alpha 0.5 (scene 6)", ref fade);
        ImGui.Separator();

        var pushedAlpha = false;
        if (fade)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.5f);
            pushedAlpha = true;
        }

        using (LayerStack.Begin())
        {
            var width = ImGui.GetContentRegionAvail().X;

            DrawNestingScene(width, "1 - three deep, innermost wraps");
            ImGui.Dummy(new Vector2(0f, 12f * scale));

            DrawScrolledScene(width, scale);
            ImGui.Dummy(new Vector2(0f, 12f * scale));

            DrawTableScene(width, scale);
            ImGui.Dummy(new Vector2(0f, 12f * scale));

            DrawSiblingScene(width, scale);
            ImGui.Dummy(new Vector2(0f, 12f * scale));

            DrawPopupScene(width, scale);
        }

        if (pushedAlpha)
            ImGui.PopStyleVar();
    }

    private static void DrawNestingScene(float width, string label)
    {
        ImGui.TextUnformatted(label);

        using var outer = Begin(width, Tints[0]);
        ImGui.TextUnformatted($"depth {LayerStack.Depth} - outer, inner width {Layout.Inner:0}");

        using var middle = Begin(Layout.Inner, Tints[1]);
        ImGui.TextUnformatted($"depth {LayerStack.Depth} - middle, inner width {Layout.Inner:0}");

        using var inner = Begin(Layout.Inner, Tints[2]);
        ImGui.TextUnformatted($"depth {LayerStack.Depth} - inner, inner width {Layout.Inner:0}");
        ImGui.TextWrapped(Paragraph);
    }

    /// Scene 2.
    private static void DrawScrolledScene(float width, float scale)
    {
        ImGui.TextUnformatted("2 - same, in a scroll child (scroll it part-way)");

        ImGui.BeginChild("##probescroll", new Vector2(width, 150f * scale), true);

        using (LayerStack.Begin())
        {
            var inner = ImGui.GetContentRegionAvail().X;
            DrawNestingScene(inner, "nested inside the child");
            ImGui.Dummy(new Vector2(0f, 10f * scale));
            DrawNestingScene(inner, "and again, so there is something to scroll to");
        }

        ImGui.EndChild();
    }

    /// Scene 3.
    private static void DrawTableScene(float width, float scale)
    {
        ImGui.TextUnformatted("3 - bordered table at depth 2");

        using var outer = Begin(width, Tints[0]);
        using var middle = Begin(Layout.Inner, Tints[1]);

        if (ImGui.BeginTable("##probetable", 3,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Slot");
            ImGui.TableSetupColumn("Item");
            ImGui.TableSetupColumn("Dye");
            ImGui.TableHeadersRow();

            for (var row = 0; row < 3; row++)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"row {row}");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted("something wearable");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted("snow white");
            }

            ImGui.EndTable();
        }
    }

    /// Scene 4.
    private static void DrawSiblingScene(float width, float scale)
    {
        ImGui.TextUnformatted("4 - unequal siblings at one depth");

        var gap = 10f * scale;
        var half = (width - gap) / 2f;
        var startY = ImGui.GetCursorPosY();
        var startX = ImGui.GetCursorPosX();

        using (Begin(half, Tints[1]))
            ImGui.TextUnformatted("short");

        var leftBottom = ImGui.GetCursorPosY();

        ImGui.SetCursorPos(new Vector2(startX + half + gap, startY));

        using (Begin(half, Tints[2]))
        {
            ImGui.TextUnformatted("tall");
            ImGui.TextWrapped(Paragraph);
        }

        ImGui.SetCursorPos(new Vector2(startX, MathF.Max(leftBottom, ImGui.GetCursorPosY())));
    }

    /// Scene 5.
    private static void DrawPopupScene(float width, float scale)
    {
        ImGui.TextUnformatted("5 - tooltip and modal from depth 2");

        DrawPopupTriggers(width);

        if (modalQueued)
        {
            ImGui.OpenPopup("##probemodal");
            modalQueued = false;
        }

        DrawProbeModal(scale);
    }

    /// Separated so the two surfaces are closed by their scopes before the popup is opened.
    private static void DrawPopupTriggers(float width)
    {
        using var outer = Begin(width, Tints[0]);
        using var middle = Begin(Layout.Inner, Tints[1]);

        ImGui.Button("hover me");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 18f);
            ImGui.TextUnformatted(Paragraph);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }

        ImGui.SameLine();

        if (ImGui.Button("open modal"))
            modalQueued = true;
    }

    private static void DrawProbeModal(float scale)
    {
        if (ImGui.BeginPopupModal("##probemodal", ImGuiWindowFlags.AlwaysAutoResize))
        {
            var content = 320f * scale;
            ImGui.Dummy(new Vector2(content, 0f));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + content);
            ImGui.TextUnformatted("A modal opened from inside two nested surfaces.");
            ImGui.TextWrapped(Paragraph);
            ImGui.PopTextWrapPos();

            using (Begin(content, Tints[2]))
                ImGui.TextUnformatted("and a surface inside the modal, on the modal's own draw list");

            if (ImGui.Button("close"))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }
    }
}
