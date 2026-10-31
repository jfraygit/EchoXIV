using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using EchoGlam.UI;

namespace EchoGlam.UI.Foundation;

/// Every number the plugin's text and sizing is derived from, in one place.
public static class TypeProbe
{
    /// Dalamud's default at 100%, which is the number every hand-placed length in this plugin was chosen
    /// against.
    private const float ExpectedBase = 17f;

    public static void Draw(Fonts fonts, Dalamud.Plugin.IDalamudPluginInterface pluginInterface)
    {
        var io = ImGui.GetIO();

        Theme.SectionHeader("What everything is derived from");
        ImGui.Dummy(new Vector2(0f, Sp.Xs));

        Row("ImGui.GetFontSize()", ImGui.GetFontSize(), ExpectedBase);
        Row("ImGuiHelpers.GlobalScale", ImGuiHelpers.GlobalScale, 1f);
        Row("io.FontGlobalScale", io.FontGlobalScale, 1f);
        Row("UiHelpers.Scale", UiHelpers.Scale, 1f);

        var spec = pluginInterface.UiBuilder.DefaultFontSpec;

        Row("Dalamud default (px)", spec.SizePx, ExpectedBase);
        Row("UiHelpers.BaseFontSize", ExpectedBase, ExpectedBase);

        ImGui.Dummy(new Vector2(0f, Sp.Md));
        Theme.SectionHeader("The type scale");
        ImGui.Dummy(new Vector2(0f, Sp.Xs));

        Row("Text.BodySize", Text.BodySize, ExpectedBase);
        Row("Text.CaptionSize", Text.CaptionSize, ExpectedBase * Text.CaptionRatio);
        Row("Text.TitleSize", Text.TitleSize, ExpectedBase * Text.TitleRatio);
        Row("Text.DisplaySize", Text.DisplaySize, ExpectedBase * Text.DisplayRatio);

        ImGui.Dummy(new Vector2(0f, Sp.Md));
        Theme.SectionHeader("The window");
        ImGui.Dummy(new Vector2(0f, Sp.Xs));

        var work = ImGui.GetMainViewport().WorkSize;
        var scale = MathF.Max(ImGuiHelpers.GlobalScale, 0.0001f);

        ImGui.TextColored(Theme.TextDim, $"design       {MainWindow.DesignSize.X:0} x {MainWindow.DesignSize.Y:0}");
        ImGui.TextColored(Theme.TextDim, $"work area    {work.X / scale:0} x {work.Y / scale:0}  (design units)");

        var fits = work.X / scale >= MainWindow.DesignSize.X + 24f
                   && work.Y / scale >= MainWindow.DesignSize.Y + 24f;

        ImGui.TextColored(
            fits ? Theme.TextDim : Theme.Warning,
            fits ? "the window fits at its design size" : "THE WINDOW IS BEING CLAMPED SMALLER");

        ImGui.Dummy(new Vector2(0f, Sp.Md));
        Theme.SectionHeader("The atlas");
        ImGui.Dummy(new Vector2(0f, Sp.Xs));

        ImGui.TextColored(
            Theme.TextDim, $"texture   {io.Fonts.TexWidth} x {io.Fonts.TexHeight}");

        ImGui.Dummy(new Vector2(0f, Sp.Md));
        Theme.SectionHeader("Specimen");
        ImGui.Dummy(new Vector2(0f, Sp.Xs));

        ImGui.TextColored(Theme.TextDim, "Baked is what the handle was built at; drawn is what it");
        ImGui.TextColored(Theme.TextDim, "is rendering at now. They have to match or it is soft.");
        ImGui.Dummy(new Vector2(0f, Sp.Sm));

        Specimen("body", null);
        Specimen("caption", fonts.Caption);
        Specimen("title", fonts.Header);
        Specimen("display", fonts.Display);
    }

    private static void Row(string label, float actual, float expected)
    {
        var off = MathF.Abs(actual - expected) > expected * 0.02f;

        ImGui.TextColored(off ? Theme.Warning : Theme.TextDim, $"{label,-26} {actual,8:0.00}");

        if (!off)
            return;

        ImGui.SameLine(0f, Sp.Md);
        ImGui.TextColored(Theme.TextDisabled, $"expected {expected:0.00}");
    }

    private static void Specimen(string name, Dalamud.Interface.ManagedFontAtlas.IFontHandle? handle)
    {
        using (handle?.PushSafe())
        {
            var font = ImGui.GetFont();

            ImGui.TextColored(
                Theme.Text,
                $"{name}  baked {font.FontSize:0.0}  drawn {ImGui.GetFontSize():0.0}   Handful of Glyphs 0123");
        }
    }
}
