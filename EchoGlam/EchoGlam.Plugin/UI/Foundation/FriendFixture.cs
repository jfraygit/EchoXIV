using System.Numerics;
using Dalamud.Bindings.ImGui;
using EchoGlam.Game;
using EchoGlam.UI.Controls;

namespace EchoGlam.UI.Foundation;

/// The control that seeds the Friends page with test data, in the diagnostics window.
public static class FriendFixture
{
    /// How many friends the next seed puts up.
    private static int wanted = FakeFriends.Most;

    public static void Draw(Plugin plugin)
    {
        var width = ImGui.GetContentRegionAvail().X;

        Theme.SectionHeader("A populated Friends page");
        ImGui.Dummy(new Vector2(0f, Sp.Xs));

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);

        ImGui.TextColored(
            Theme.TextDim,
            "Fills the Friends page with invented people so the states that only appear in a long "
            + "list can be looked at together: live, sharing but out of zone, not seen in a week, "
            + "never seen, blocked, a name too long for its card, and requests in both directions.");

        ImGui.Dummy(new Vector2(0f, Sp.Sm));

        ImGui.TextColored(
            Theme.TextDim,
            "Nothing is sent and nothing is written down. The relay poll is suspended while it is "
            + "up, and Accept, Decline and Remove do the local half only - so the page answers a "
            + "press without the relay being handed an id that was invented in a loop. Asking "
            + "somebody new is refused outright and says why.");

        ImGui.PopTextWrapPos();
        ImGui.Dummy(new Vector2(0f, Sp.Lg));

        var seeded = plugin.Friendships.Faked;

        ImGui.TextColored(
            seeded ? Theme.Accent : Theme.TextDisabled,
            seeded ? "Seeded. The Friends page is showing test data." : "Not seeded.");

        ImGui.Dummy(new Vector2(0f, Sp.Lg));

        Field.Label("Friends", wanted, FakeFriends.Most, width);

        var count = wanted;

        if (EchoSlider.DrawInt("##fakefriendcount", ref count, 1, FakeFriends.Most, width, "{0:0}"))
            wanted = count;

        ImGui.Dummy(new Vector2(0f, Sp.Lg));

        ImGui.TextColored(Theme.TextDisabled, "Pending requests are always included.");

        ImGui.Dummy(new Vector2(0f, Sp.Lg));

        if (EchoButton.Draw(
                "##seedfriends", seeded ? "Re-seed" : "Seed", new Vector2(0f, Metrics.RowTall),
                primary: !seeded))
        {
            FakeFriends.Apply(plugin, wanted);
        }

        ImGui.SameLine(0f, Sp.Md);

        if (EchoButton.Draw(
                "##clearfriends", "Clear", new Vector2(0f, Metrics.RowTall), enabled: seeded,
                tooltip: "Takes the test data down and asks the relay for the real list again."))
        {
            FakeFriends.Clear(plugin);
        }
    }
}
