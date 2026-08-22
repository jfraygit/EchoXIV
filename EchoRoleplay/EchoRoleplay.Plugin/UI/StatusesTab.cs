using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoRoleplay.Game;
using EchoRoleplay.Shared;
using EchoRoleplay.UI.Controls;

namespace EchoRoleplay.UI;

/// Where a player sets what their character is showing right now.
public sealed class StatusesTab
{
    private readonly Plugin plugin;
    private readonly StatusEditor editor = new();

    /// Which status the editor is working on, so a commit knows whether to replace or add.
    private RoleplayStatus? pending;

    public StatusesTab(Plugin plugin)
    {
        this.plugin = plugin;
        editor.Committed += OnCommitted;
    }

    private ProfileStore Store => plugin.Profiles;

    private void OnCommitted(RoleplayStatus status)
    {
        var profile = Store.ForCharacter(plugin.LocalCharacterKey);
        if (profile is null)
            return;

        if (pending is null && profile.Statuses.Count < ProfileLimits.Statuses)
            profile.Statuses.Add(status);

        pending = null;
        Store.Touch(profile);
    }

    public void Draw()
    {
        var scale = UiHelpers.Scale;

        if (Store.ReadOnly)
        {
            ImGui.TextColored(Theme.Bad, "Your profiles file could not be read, so nothing will be saved.");
            ImGui.TextColored(Theme.TextDim, "The file has been left alone rather than replaced. Check the log.");
            ImGui.Dummy(new Vector2(0f, 8f * scale));
        }

        ImGui.Dummy(new Vector2(0f, 6f * scale));

        var characterKey = plugin.LocalCharacterKey;

        if (string.IsNullOrEmpty(characterKey))
        {
            DrawNotice("Not Logged In", "Log in to a character to set statuses.", scale);
            return;
        }

        var profile = Store.ForCharacter(characterKey);

        if (profile is null)
        {
            DrawNoProfile(characterKey, scale);
            return;
        }

        DrawWearing(characterKey, profile, scale);

        ImGui.Dummy(new Vector2(0f, 10f * scale));

        if (ImGui.BeginChild("##statusbody", ImGui.GetContentRegionAvail(), false))
            DrawStatusList(profile, scale);

        ImGui.EndChild();

        editor.Draw(plugin.Icons);
    }

    private static void DrawNotice(string heading, string body, float scale)
    {
        Theme.BeginPanel("##statusnotice");

        ImGui.TextColored(Theme.Text, heading);
        ImGui.Dummy(new Vector2(0f, 4f * scale));

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Theme.ContentWidth);
        ImGui.TextColored(Theme.TextDim, body);
        ImGui.PopTextWrapPos();

        Theme.EndPanel();
    }

    private void DrawNoProfile(string characterKey, float scale)
    {
        Theme.BeginPanel("##statusnoprofile");

        ImGui.TextColored(Theme.Text, "No Profile On This Character");
        ImGui.Dummy(new Vector2(0f, 4f * scale));

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Theme.ContentWidth);
        ImGui.TextColored(Theme.TextDim,
            "Statuses belong to a profile, so this character needs one before it can show any. The "
            + "Profile tab makes and assigns them.");
        ImGui.PopTextWrapPos();

        ImGui.Dummy(new Vector2(0f, 10f * scale));

        if (EchoButton.Draw("##makeprofile", "Create Profile", new Vector2(0f, 28f * scale),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Plus, enabled: !Store.ReadOnly))
        {
            var created = Store.Create(characterKey.Split('@')[0]);
            Store.Bind(characterKey, created.Id);
        }

        Theme.EndPanel();
    }

    /// Who these statuses belong to.
    private void DrawWearing(string characterKey, RoleplayProfile profile, float scale)
    {
        Theme.BeginPanel("##statuswearing", Theme.Accent, gradient: true);

        var origin = ImGui.GetCursorScreenPos();
        var inkCentre = UiHelpers.TextInkCentre();

        using (plugin.Fonts.Icon.PushSafe())
        {
            UiHelpers.DrawIconSized(
                ImGui.GetWindowDrawList(), FontAwesomeIcon.User,
                new Vector2(origin.X + (6f * scale), origin.Y + inkCentre),
                12f * scale, ImGui.GetColorU32(Theme.TextDim));
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X + (18f * scale), origin.Y));
        ImGui.TextColored(Theme.TextDim, characterKey);

        var name = !string.IsNullOrWhiteSpace(profile.Name) ? profile.Name
            : !string.IsNullOrWhiteSpace(profile.ProfileName) ? profile.ProfileName
            : "Unnamed";

        var colour = profile.NameColour is { Length: >= 3 } c
            ? new Vector4(c[0], c[1], c[2], 1f)
            : Theme.Accent;

        ImGui.TextColored(colour, name);

        Theme.EndPanel();
    }

    private void DrawStatusList(RoleplayProfile profile, float scale)
    {
        Theme.BeginPanel("##statuslist");
        Theme.PanelHeader($"Statuses  ({profile.Statuses.Count}/{ProfileLimits.Statuses})");

        if (profile.Statuses.Count == 0)
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Theme.ContentWidth);
            ImGui.TextColored(Theme.TextDim,
                plugin.Configuration.Statuses == StatusPlacement.UnderCharacter
                    ? "Nothing set. These hang under your character for anybody to read."
                    : "Nothing set. These show when somebody rests their pointer on you.");
            ImGui.PopTextWrapPos();

            ImGui.Dummy(new Vector2(0f, 10f * scale));
        }

        var now = DateTime.UtcNow;
        var remove = -1;

        for (var i = 0; i < profile.Statuses.Count; i++)
        {
            ImGui.PushID(i);

            if (DrawStatusCard(profile.Statuses[i], now, scale))
                remove = i;

            ImGui.PopID();
            ImGui.Dummy(new Vector2(0f, 8f * scale));
        }

        if (remove >= 0)
        {
            profile.Statuses.RemoveAt(remove);
            Store.Touch(profile);
        }

        if (profile.Statuses.Count >= ProfileLimits.Statuses)
        {
            ImGui.TextColored(Theme.TextDim, "That is the limit. Four is already a wide thing to hang under somebody.");
            Theme.EndPanel();
            return;
        }

        if (EchoButton.Draw("##addstatus", "New Status", new Vector2(0f, 28f * scale),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Plus, enabled: !Store.ReadOnly))
        {
            pending = null;
            editor.OpenForNew();
        }

        Theme.EndPanel();
    }

    /// One status: its art at world size, its words, and its remaining life.
    private bool DrawStatusCard(RoleplayStatus status, DateTime now, float scale)
    {
        var box = 38f * scale;
        var buttons = 20f * scale;
        var gap = 8f * scale;

        var origin = ImGui.GetCursorScreenPos();
        var width = Theme.ContentWidth;
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(
            origin, origin + new Vector2(box, box), ImGui.GetColorU32(Theme.Tinted(0.10f)), 8f * scale);

        plugin.Icons.Draw(drawList, status.IconId, origin, box, 0xFFFFFFFF);

        drawList.AddRect(
            origin, origin + new Vector2(box, box),
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0.45f)),
            8f * scale, ImDrawFlags.None, 1f * scale);

        ProfileFields.At(origin, box + gap);
        ImGui.BeginGroup();

        ImGui.TextUnformatted(string.IsNullOrWhiteSpace(status.Label) ? "(Unnamed)" : status.Label);

        if (!string.IsNullOrWhiteSpace(status.Detail))
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width - box - gap - (buttons * 2f) - (gap * 2f));
            ImGui.TextColored(Theme.TextDim, status.Detail);
            ImGui.PopTextWrapPos();
        }

        ImGui.EndGroup();

        var textBottom = ImGui.GetItemRectMax().Y;

        ProfileFields.At(origin, width - (buttons * 2f) - gap);

        var removed = false;

        if (EchoButton.BareIcon("##edit", plugin.Fonts.Icon, FontAwesomeIcon.PencilAlt, buttons, "Edit"))
        {
            pending = status;
            editor.OpenForEdit(status);
        }

        ImGui.SameLine(0f, gap);

        if (EchoButton.BareIcon("##remove", plugin.Fonts.Icon, FontAwesomeIcon.Times, buttons, "Remove",
                colourOverride: Theme.Bad))
        {
            removed = true;
        }

        var bottom = MathF.Max(MathF.Max(textBottom, ImGui.GetItemRectMax().Y), origin.Y + box);

        ImGui.SetCursorScreenPos(new Vector2(origin.X, bottom + (4f * scale)));

        DrawExpiry(status, now, scale);

        return removed;
    }

    /// What is left of a status's life.
    private static void DrawExpiry(RoleplayStatus status, DateTime now, float scale)
    {
        if (status.ExpiresUtc is not { } expiry)
        {
            ImGui.TextColored(Theme.TextDisabled, "Stays until removed");
            return;
        }

        var left = expiry - now;

        if (left.TotalMinutes < 1)
        {
            ProfileFields.StatePill("Expiring", Theme.Warning);
            return;
        }

        var text = left.TotalHours >= 1
            ? $"{(int)left.TotalHours}h {left.Minutes}m Left"
            : $"{(int)left.TotalMinutes}m Left";

        ProfileFields.StatePill(text, left.TotalMinutes <= 15 ? Theme.Warning : Theme.Accent);

        _ = scale;
    }

}
