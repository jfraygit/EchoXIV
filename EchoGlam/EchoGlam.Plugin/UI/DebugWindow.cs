using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using EchoGlam.UI.Foundation;

namespace EchoGlam.UI;

/// The diagnostics window.
public sealed class DebugWindow : Window, IDisposable
{
    /// The styleframes decode a few of the player's screenshots to show real pictures.
    public void Dispose() => Styleframes.Unload();

    /// The font handles, so the Type tab can report what each one was actually baked at.
    private readonly Fonts fonts;

    /// For the Friends tab, which seeds a list into the live Friendships and LookSharing rather than drawing
    /// a mock-up of one - the page under test has to be the real page.
    private readonly Plugin plugin;

    public DebugWindow(Plugin plugin, Fonts fonts)
        : base("EchoGlam Diagnostics###EchoGlamDebug")
    {
        this.plugin = plugin;
        this.fonts = fonts;

        Size = new Vector2(620f, 720f);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(320f, 240f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    private Theme.ThemePush pushed;

    public override void PreDraw() => pushed = Theme.Push();

    public override void PostDraw() => Theme.Pop(pushed);

    public override void Draw()
    {
        if (ImGui.BeginTabBar("##echoglamdebugtabs"))
        {
            if (ImGui.BeginTabItem("Looks"))
            {
                ImGui.BeginChild("##lookscroll", ImGui.GetContentRegionAvail(), false);
                Styleframes.Draw();
                ImGui.EndChild();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Tokens"))
            {
                ImGui.BeginChild("##tokenscroll", ImGui.GetContentRegionAvail(), false);
                TokenSpecimen.Draw();
                ImGui.EndChild();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Type"))
            {
                ImGui.BeginChild("##typescroll", ImGui.GetContentRegionAvail(), false);
                TypeProbe.Draw(fonts, Plugin.PluginInterface);
                ImGui.EndChild();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Friends"))
            {
                ImGui.BeginChild("##friendfixturescroll", ImGui.GetContentRegionAvail(), false);
                FriendFixture.Draw(plugin);
                ImGui.EndChild();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Nesting"))
            {
                ImGui.BeginChild("##foundationscroll", ImGui.GetContentRegionAvail(), false);
                FoundationProbe.Draw();
                ImGui.EndChild();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }
}
