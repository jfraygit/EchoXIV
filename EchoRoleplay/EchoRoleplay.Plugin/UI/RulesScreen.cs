using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoRoleplay.UI.Controls;

namespace EchoRoleplay.UI;

/// The rules, shown once per version, before anything else.
public sealed class RulesScreen
{
    private readonly Plugin plugin;

    public RulesScreen(Plugin plugin)
    {
        this.plugin = plugin;
    }

    /// Whether the rules still need accepting for this build.
    public bool Pending =>
        !string.Equals(plugin.Configuration.AcceptedRulesVersion, plugin.Version, System.StringComparison.Ordinal);

    /// The rules themselves.
    private static readonly (FontAwesomeIcon Icon, string Heading, string Body)[] Rules =
    [
        (FontAwesomeIcon.Globe, "Anyone Can Read Your Profile",
            "Once you publish, any player who meets your character can open your profile and read all "
            + "of it. Assume strangers will."),

        (FontAwesomeIcon.Lock, "Adult Content Stays Private",
            "Adult content is allowed. Keep it to private venues - if someone sees it in public and "
            + "reports it, we take it down."),

        (FontAwesomeIcon.Ban, "Nothing Hateful",
            "No slurs and no harassment, anywhere in your profile. Names and statuses are checked "
            + "automatically, since those appear over your character uninvited."),

        (FontAwesomeIcon.IdBadge, "Only Publish Your Own Characters",
            "Only the real owner can verify a character, and verified always beats unverified - so if "
            + "someone takes yours, verify it and you get it back. Playing as someone else without "
            + "their blessing will get you removed."),

        (FontAwesomeIcon.Gavel, "Three Strikes",
            "If we take something of yours down, that is one strike. After three, this copy of the "
            + "plugin can no longer publish."),

        (FontAwesomeIcon.ShieldAlt, "You Can Always Block",
            "You never have to wait for us. Blocking someone instantly hides their profile, statuses "
            + "and roleplay name, and they are not told."),
    ];

    /// Draws the rules.
    public bool Draw(float scale)
    {
        var width = ImGui.GetContentRegionAvail().X;

        using (plugin.Fonts.Header.PushSafe())
            ImGui.TextColored(Theme.Accent, "Before You Start");

        ImGui.Dummy(new Vector2(0f, 4f * scale));

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
        ImGui.TextColored(Theme.TextDim,
            "EchoRoleplay puts what you write in front of other players. A few things to know.");
        ImGui.PopTextWrapPos();

        ImGui.Dummy(new Vector2(0f, 10f * scale));

        Theme.BeginPanel("##rules");

        var inner = Theme.ContentWidth;
        var gutter = 30f * scale;

        for (var i = 0; i < Rules.Length; i++)
        {
            var (icon, heading, body) = Rules[i];
            var top = ImGui.GetCursorScreenPos();

            var headingCentre = ImGui.GetTextLineHeight() / 2f;

            var column = 22f * scale;

            using (plugin.Fonts.Icon.PushSafe())
            {
                var glyph = icon.ToIconString();
                var glyphSize = ImGui.CalcTextSize(glyph);

                ImGui.GetWindowDrawList().AddText(
                    new Vector2(
                        top.X + ((column - glyphSize.X) / 2f),
                        top.Y + headingCentre - (glyphSize.Y / 2f)),
                    ImGui.GetColorU32(Theme.Accent),
                    glyph);
            }

            ImGui.SetCursorScreenPos(new Vector2(top.X + gutter, top.Y));

            ImGui.BeginGroup();
            ImGui.TextColored(Theme.Text, heading);
            ImGui.Dummy(new Vector2(0f, 3f * scale));

            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + inner - gutter);
            ImGui.TextColored(Theme.TextDim, body);
            ImGui.PopTextWrapPos();
            ImGui.EndGroup();

            if (i == Rules.Length - 1)
                continue;

            ImGui.Dummy(new Vector2(0f, 8f * scale));
            Theme.PanelDivider(inner);
            ImGui.Dummy(new Vector2(0f, 8f * scale));
        }

        Theme.EndPanel();

        ImGui.Dummy(new Vector2(0f, 12f * scale));

        var buttonWidth = 180f * scale;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((width - buttonWidth) / 2f));

        var accepted = EchoButton.Draw("##acceptrules", "Okay", new Vector2(buttonWidth, 32f * scale));

        ImGui.Dummy(new Vector2(0f, 12f * scale));

        if (!accepted)
            return false;

        plugin.Configuration.AcceptedRulesVersion = plugin.Version;
        plugin.Configuration.Save();

        return true;
    }
}
