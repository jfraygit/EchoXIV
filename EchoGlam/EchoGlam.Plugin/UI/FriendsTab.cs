using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoGlam.Game;
using EchoGlam.Shared;
using EchoGlam.UI.Controls;

namespace EchoGlam.UI;

/// Who may see your glamours, and who has asked to.
public sealed class FriendsTab
{
    private readonly Plugin plugin;

    private string search = string.Empty;

    /// Which friend's row is showing its confirm-remove state, by id.
    private string confirming = string.Empty;

    public FriendsTab(Plugin plugin) => this.plugin = plugin;

    private Friendships Friendships => plugin.Friendships;

    public void Draw()
    {
        var width = ImGui.GetContentRegionAvail().X;
        var scale = UiHelpers.Scale;

        DrawSharingPanel(width);
        ImGui.Dummy(new Vector2(0f, 10f * scale));

        if (!Friendships.SharingEnabled && Friendships.Friends.Count == 0)
        {
            DrawOffState(width);
            return;
        }

        if (Friendships.Incoming.Count > 0)
        {
            DrawRequestsPanel(width);
            ImGui.Dummy(new Vector2(0f, 10f * scale));
        }

        DrawFriendsPanel(width);

        if (Friendships.Outgoing.Count > 0)
        {
            ImGui.Dummy(new Vector2(0f, 10f * scale));
            DrawOutgoingPanel(width);
        }
    }


    private void DrawSharingPanel(float width)
    {
        var scale = UiHelpers.Scale;

        Theme.BeginPanel(width);

        var inner = width - (28f * scale);
        var sharing = Friendships.SharingEnabled;

        Theme.SectionHeader("Glamour Sharing", ruleWidth: inner);
        ImGui.Dummy(new Vector2(0f, 6f * scale));

        var wasSharing = sharing;

        if (EchoToggle.Draw(
                "##echoglamsharing",
                sharing ? "Sharing On" : "Sharing Off",
                ref sharing,
                "Friends see the glamours you wear, and you see theirs. Nobody else does. "
                + "Turning this off pauses it - it doesn't remove your friends.")
            && sharing != wasSharing)
        {
            Friendships.SetSharing(sharing);
            confirming = string.Empty;
        }

        ImGui.Dummy(new Vector2(0f, 4f * scale));

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextDisabled);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + inner);

        ImGui.TextUnformatted(Friendships.SharingEnabled
            ? "Right-click somebody in game and choose Add EchoGlam Friend."
            : "Nobody can look you up while this is off. Your friends are kept.");

        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();

        if (Friendships.SharingEnabled && plugin.Looks.Drawn > 0)
        {
            ImGui.Dummy(new Vector2(0f, 6f * scale));

            var drawn = plugin.Looks.Drawn;

            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Good);
            ImGui.TextUnformatted(drawn == 1
                ? "Wearing 1 friend's glamour on screen."
                : $"Wearing {drawn} friends' glamours on screen.");
            ImGui.PopStyleColor();
        }

        if (Friendships.Message.Length > 0)
        {
            ImGui.Dummy(new Vector2(0f, 6f * scale));

            ImGui.PushStyleColor(ImGuiCol.Text, Friendships.Failed ? Theme.Bad : Theme.Good);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + inner);
            ImGui.TextUnformatted(Friendships.Message);
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();
        }

        Theme.EndPanel();
    }

    /// What the page says when sharing has never been turned on.
    private void DrawOffState(float width)
    {
        var scale = UiHelpers.Scale;

        Theme.BeginPanel(width);

        var inner = width - (28f * scale);

        ImGui.Dummy(new Vector2(0f, 8f * scale));

        var center = ImGui.GetCursorScreenPos() + new Vector2(inner / 2f, 16f * scale);
        UiHelpers.DrawScaledIcon(
            ImGui.GetWindowDrawList(), FontAwesomeIcon.UserFriends, center,
            ImGui.GetColorU32(Theme.TextDisabled));

        ImGui.Dummy(new Vector2(0f, 44f * scale));

        Centred("Sharing Off", Theme.Text, inner);
        ImGui.Dummy(new Vector2(0f, 4f * scale));
        Centred("Turn it on to add friends and see what they're wearing.", Theme.TextDisabled, inner);

        ImGui.Dummy(new Vector2(0f, 10f * scale));

        Theme.EndPanel();
    }


    private void DrawRequestsPanel(float width)
    {
        var scale = UiHelpers.Scale;

        Theme.BeginPanel(width, Theme.Accent);

        var inner = width - (28f * scale);

        Theme.SectionHeader($"Requests ({Friendships.Incoming.Count})", ruleWidth: inner);
        ImGui.Dummy(new Vector2(0f, 6f * scale));

        foreach (var entry in Friendships.Incoming.ToList())
        {
            DrawRequestRow(entry, inner);
            ImGui.Dummy(new Vector2(0f, 4f * scale));
        }

        Theme.EndPanel();
    }

    private void DrawRequestRow(FriendEntry entry, float width)
    {
        var scale = UiHelpers.Scale;
        var rowHeight = 46f * scale;
        var padX = 10f * scale;
        var gap = 6f * scale;

        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        ImGui.InvisibleButton($"##req{entry.Id}", new Vector2(width, rowHeight), ImGuiButtonFlags.MouseButtonLeft);
        ImGui.SetItemAllowOverlap();

        var min = origin;
        var max = origin + new Vector2(width, rowHeight);
        var rounding = 8f * scale;

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(Theme.Tinted(0.07f)), rounding);
        drawList.AddRectFilled(
            min, new Vector2(min.X + (3f * scale), max.Y),
            ImGui.GetColorU32(Theme.Accent), rounding, ImDrawFlags.RoundCornersLeft);

        var buttonHeight = 26f * scale;
        var buttonY = origin.Y + ((rowHeight - buttonHeight) / 2f);

        var declineWidth = EchoButton.ContentSize("Decline").X + (8f * scale);
        var acceptWidth = EchoButton.ContentSize("Accept").X + (8f * scale);

        var x = origin.X + width - declineWidth - padX;

        ImGui.SetCursorScreenPos(new Vector2(x, buttonY));

        if (EchoButton.Draw(
                $"##reqno{entry.Id}", "Decline", new Vector2(declineWidth, buttonHeight),
                enabled: !Friendships.Busy, tooltip: "They aren't told, and they can ask again."))
        {
            Friendships.Answer(entry.Id, accept: false);
        }

        x -= acceptWidth + gap;

        ImGui.SetCursorScreenPos(new Vector2(x, buttonY));

        if (EchoButton.Draw(
                $"##reqyes{entry.Id}", "Accept", new Vector2(acceptWidth, buttonHeight),
                enabled: !Friendships.Busy, primary: true,
                tooltip: "You'll see each other's glamours."))
        {
            Friendships.Answer(entry.Id, accept: true);
        }

        var textWidth = MathF.Max(0f, x - origin.X - padX - gap);
        DrawTwoLines(
            drawList, origin + new Vector2(padX, 0f), rowHeight, textWidth,
            Name(entry.Character), $"{World(entry.Character)} - asked to be friends");

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + rowHeight));
    }


    private void DrawFriendsPanel(float width)
    {
        var scale = UiHelpers.Scale;

        Theme.BeginPanel(width);

        var inner = width - (28f * scale);
        var friends = Friendships.Friends;

        Theme.SectionHeader($"Friends ({friends.Count})", ruleWidth: inner);
        ImGui.Dummy(new Vector2(0f, 8f * scale));

        if (friends.Count > 6)
        {
            UiHelpers.SearchField(
                "##friendsearch", "Search friends", ref search, inner, 28f * scale, plugin.Fonts.Icon);

            ImGui.Dummy(new Vector2(0f, 8f * scale));
        }

        var shown = friends
            .Where(f => search.Length == 0
                        || f.Character.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Character, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (friends.Count == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextDisabled);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + inner);

            ImGui.TextUnformatted(Friendships.Synced
                ? "No friends yet. Right-click somebody in game and choose Add EchoGlam Friend."
                : "Checking...");

            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();
        }
        else if (shown.Count == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextDisabled);
            ImGui.TextUnformatted("Nobody matches that.");
            ImGui.PopStyleColor();
        }

        foreach (var entry in shown)
        {
            DrawFriendRow(entry, inner);
            ImGui.Dummy(new Vector2(0f, 4f * scale));
        }

        Theme.EndPanel();
    }

    private void DrawFriendRow(FriendEntry entry, float width)
    {
        var scale = UiHelpers.Scale;
        var rowHeight = 46f * scale;
        var padX = 10f * scale;
        var gap = 6f * scale;
        var iconWidth = 24f * scale;

        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        ImGui.InvisibleButton($"##friend{entry.Id}", new Vector2(width, rowHeight), ImGuiButtonFlags.MouseButtonLeft);

        var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
        ImGui.SetItemAllowOverlap();

        var blocked = Friendships.IsBlocked(entry.Character);
        var confirm = string.Equals(confirming, entry.Id, StringComparison.Ordinal);

        var min = origin;
        var max = origin + new Vector2(width, rowHeight);
        var rounding = 8f * scale;

        var fill = confirm
            ? Vector4.Lerp(Theme.Panel, Theme.Bad, 0.16f)
            : hovered
                ? Theme.Tinted(0.13f)
                : Theme.Tinted(0.035f);

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(fill), rounding);

        if (hovered || confirm)
        {
            drawList.AddRect(
                min, max, ImGui.GetColorU32(confirm ? Theme.Bad with { W = 0.6f } : Theme.Border),
                rounding, ImDrawFlags.None, 1.2f * scale);
        }

        var buttonHeight = 24f * scale;
        var buttonY = origin.Y + ((rowHeight - buttonHeight) / 2f);
        var x = origin.X + width - padX;

        if (confirm)
        {
            var yesWidth = EchoButton.ContentSize("Remove").X + (8f * scale);
            var noWidth = EchoButton.ContentSize("Keep").X + (8f * scale);

            x -= noWidth;
            ImGui.SetCursorScreenPos(new Vector2(x, buttonY));

            if (EchoButton.Draw($"##keep{entry.Id}", "Keep", new Vector2(noWidth, buttonHeight)))
                confirming = string.Empty;

            x -= yesWidth + gap;
            ImGui.SetCursorScreenPos(new Vector2(x, buttonY));

            if (EchoButton.Draw(
                    $"##rm{entry.Id}", "Remove", new Vector2(yesWidth, buttonHeight),
                    accentOverride: Theme.Bad,
                    tooltip: "Ends it for both of you."))
            {
                Friendships.Remove(entry.Id);
                confirming = string.Empty;
            }
        }
        else
        {
            x -= iconWidth;

            if (hovered)
            {
                ImGui.SetCursorScreenPos(new Vector2(x, origin.Y + ((rowHeight - iconWidth) / 2f)));

                if (EchoButton.BareIcon(
                        $"##unfriend{entry.Id}", plugin.Fonts.Icon, FontAwesomeIcon.UserMinus, iconWidth,
                        "Remove this friend.", colourOverride: Theme.Bad))
                {
                    confirming = entry.Id;
                }
            }

            x -= iconWidth + gap;

            if (hovered || blocked)
            {
                ImGui.SetCursorScreenPos(new Vector2(x, origin.Y + ((rowHeight - iconWidth) / 2f)));

                if (EchoButton.BareIcon(
                        $"##block{entry.Id}", plugin.Fonts.Icon,
                        blocked ? FontAwesomeIcon.EyeSlash : FontAwesomeIcon.Eye, iconWidth,
                        blocked
                            ? "Hidden. Click to see their glamours again."
                            : "Hide their glamours without unfriending them.",
                        colourOverride: blocked ? Theme.Warning : Theme.TextDisabled))
                {
                    Friendships.SetBlocked(entry.Character, !blocked);
                }
            }
        }

        var textWidth = MathF.Max(0f, x - origin.X - padX - gap);

        var detail = blocked
            ? $"{World(entry.Character)} - hidden"
            : $"{World(entry.Character)} - friends since {Since(entry.WhenUtc)}";

        DrawTwoLines(drawList, origin + new Vector2(padX, 0f), rowHeight, textWidth,
            Name(entry.Character), detail);

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + rowHeight));
    }


    private void DrawOutgoingPanel(float width)
    {
        var scale = UiHelpers.Scale;

        Theme.BeginPanel(width);

        var inner = width - (28f * scale);

        Theme.SectionHeader($"Waiting ({Friendships.Outgoing.Count})", ruleWidth: inner);
        ImGui.Dummy(new Vector2(0f, 6f * scale));

        foreach (var entry in Friendships.Outgoing.ToList())
        {
            DrawOutgoingRow(entry, inner);
            ImGui.Dummy(new Vector2(0f, 4f * scale));
        }

        Theme.EndPanel();
    }

    private void DrawOutgoingRow(FriendEntry entry, float width)
    {
        var scale = UiHelpers.Scale;
        var rowHeight = 40f * scale;
        var padX = 10f * scale;
        var gap = 6f * scale;
        var iconWidth = 22f * scale;

        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        ImGui.InvisibleButton($"##out{entry.Id}", new Vector2(width, rowHeight), ImGuiButtonFlags.MouseButtonLeft);
        var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
        ImGui.SetItemAllowOverlap();

        drawList.AddRectFilled(
            origin, origin + new Vector2(width, rowHeight),
            ImGui.GetColorU32(hovered ? Theme.Tinted(0.09f) : Theme.Tinted(0.02f)), 8f * scale);

        var x = origin.X + width - iconWidth - padX;

        if (hovered)
        {
            ImGui.SetCursorScreenPos(new Vector2(x, origin.Y + ((rowHeight - iconWidth) / 2f)));

            if (EchoButton.BareIcon(
                    $"##withdraw{entry.Id}", plugin.Fonts.Icon, FontAwesomeIcon.Times, iconWidth,
                    "Take the request back.", colourOverride: Theme.Bad))
            {
                Friendships.Remove(entry.Id);
            }
        }

        var textWidth = MathF.Max(0f, x - origin.X - padX - gap);

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextDisabled);
        drawList.AddText(
            origin + new Vector2(padX, (rowHeight - ImGui.GetTextLineHeight()) / 2f),
            ImGui.GetColorU32(Theme.TextDisabled),
            UiHelpers.Truncate($"{Name(entry.Character)} - waiting for an answer", textWidth));
        ImGui.PopStyleColor();

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + rowHeight));
    }


    /// A name on top and a quieter line under it, vertically centred as a pair.
    private static void DrawTwoLines(
        ImDrawListPtr drawList, Vector2 origin, float rowHeight, float width, string top, string bottom)
    {
        var lineHeight = ImGui.GetTextLineHeight();
        var gap = 2f * UiHelpers.Scale;
        var blockHeight = (lineHeight * 2f) + gap;
        var y = origin.Y + ((rowHeight - blockHeight) / 2f);

        drawList.AddText(
            new Vector2(origin.X, y), ImGui.GetColorU32(Theme.Text), UiHelpers.Truncate(top, width));

        drawList.AddText(
            new Vector2(origin.X, y + lineHeight + gap), ImGui.GetColorU32(Theme.TextDisabled),
            UiHelpers.Truncate(bottom, width));
    }

    private static void Centred(string text, Vector4 colour, float width)
    {
        var size = ImGui.CalcTextSize(text);

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, (width - size.X) / 2f));
        ImGui.PushStyleColor(ImGuiCol.Text, colour);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    private static string Name(string characterKey)
    {
        var at = characterKey.LastIndexOf('@');
        return at > 0 ? characterKey[..at] : characterKey;
    }

    private static string World(string characterKey)
    {
        var at = characterKey.LastIndexOf('@');
        return at > 0 && at < characterKey.Length - 1 ? characterKey[(at + 1)..] : "Unknown world";
    }

    /// "today", "3 days ago", "2 months ago".
    private static string Since(DateTime whenUtc)
    {
        var days = (int)(DateTime.UtcNow - whenUtc).TotalDays;

        return days switch
        {
            <= 0 => "today",
            1 => "yesterday",
            < 30 => $"{days} days ago",
            < 60 => "a month ago",
            < 365 => $"{days / 30} months ago",
            _ => "over a year ago",
        };
    }
}
