using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoRoleplay.Game;
using EchoRoleplay.Shared;
using EchoRoleplay.UI.Controls;

namespace EchoRoleplay.UI;

/// What a profile looks like, sitting above the fields that make it.
public sealed class ProfileHeader
{
    private readonly Plugin plugin;

    /// The ring's eased value, so completeness grows into place as fields are filled rather than jumping.
    private float shownCompleteness;

    public ProfileHeader(Plugin plugin) => this.plugin = plugin;

    /// What the header offers to do to the profile list.
    public enum Action
    {
        None,
        New,
        Duplicate,
        Delete,
        Wear,

        /// Open this profile in the reader, exactly as anybody else sees it.
        Preview,
    }

    /// Which fields count toward "complete".
    private static readonly Func<RoleplayProfile, string>[] Counted =
    [
        p => p.Name,
        p => p.Title,
        p => p.Pronouns,
        p => p.Race,
        p => p.Age,
        p => p.Occupation,
        p => p.Residence,
        p => p.Height,
        p => p.Eyes,
        p => p.Hair,
        p => p.Appearance,
        p => p.Personality,
        p => p.Backstory,

    ];

    /// How a roleplay status reads at a glance.
    public static (string Label, Vector4 Colour) Present(RpStatus status) => status switch
    {
        RpStatus.InCharacter => ("In Character", Theme.Good),
        RpStatus.LookingForRp => ("Looking For RP", Theme.Accent),
        RpStatus.InAScene => ("In A Scene", Theme.Warning),
        RpStatus.DoNotDisturb => ("Do Not Disturb", Theme.Bad),
        RpStatus.OutOfCharacter => ("Out Of Character", Theme.TextDim),
        _ => (string.Empty, Theme.TextDim),
    };

    public Action Draw(RoleplayProfile? profile, IReadOnlyList<RoleplayProfile> all, ref string? editingId)
    {
        var scale = UiHelpers.Scale;
        var action = Action.None;

        Theme.BeginPanel("##profileheader", Theme.Accent, gradient: true);

        var medallion = 62f * scale;
        var origin = ImGui.GetCursorScreenPos();
        var width = Theme.ContentWidth;

        DrawMedallion(profile, origin, medallion, scale);

        var controlsWidth = MathF.Min(210f * scale, MathF.Max(150f * scale, width * 0.42f));
        var controlsX = origin.X + width - controlsWidth;

        action = DrawControls(profile, all, ref editingId, new Vector2(controlsX, origin.Y), controlsWidth, scale);

        var textX = origin.X + medallion + (16f * scale);
        var textWidth = MathF.Max(60f * scale, controlsX - textX - (12f * scale));

        DrawNameBlock(profile, new Vector2(textX, origin.Y), textWidth, scale);

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + medallion + (4f * scale)));

        ImGui.Dummy(Vector2.Zero);

        if (profile is not null)
        {
            Theme.PanelDivider(width);
            ImGui.Dummy(new Vector2(0f, 6f * scale));

            if (DrawBinding(profile, scale))
                action = Action.Wear;
        }

        Theme.EndPanel();

        return action;
    }

    /// The monogram, its ring, and how much of the sheet is written.
    private void DrawMedallion(RoleplayProfile? profile, Vector2 origin, float size, float scale)
    {
        var drawList = ImGui.GetWindowDrawList();
        var radius = size / 2f;
        var centre = new Vector2(origin.X + radius, origin.Y + radius);

        var colour = NameColour(profile);

        drawList.AddCircleFilled(
            centre, radius - (2f * scale),
            ImGui.GetColorU32(new Vector4(colour.X, colour.Y, colour.Z, 0.16f)));

        drawList.AddCircle(
            centre, radius - (2f * scale),
            ImGui.GetColorU32(new Vector4(colour.X, colour.Y, colour.Z, 0.28f)), 0, 2.5f * scale);

        var target = Completeness(profile);
        shownCompleteness = UiHelpers.Lerp(shownCompleteness, target, 9f, ImGui.GetIO().DeltaTime);

        if (shownCompleteness > 0.005f)
        {
            const float top = -MathF.PI / 2f;

            drawList.PathArcTo(
                centre, radius - (2f * scale), top, top + (MathF.Tau * shownCompleteness), 64);
            drawList.PathStroke(ImGui.GetColorU32(colour), ImDrawFlags.None, 2.5f * scale);
        }

        var portrait = profile is null ? null : plugin.Portraits.Own(profile.Id);

        if (portrait is not null)
        {
            var inset = 4f * scale;

            drawList.AddImageRounded(
                portrait.Handle,
                origin + new Vector2(inset, inset),
                origin + new Vector2(size - inset, size - inset),
                Vector2.Zero,
                Vector2.One,
                0xFFFFFFFF,
                radius - inset);
        }

        var monogram = portrait is null ? Monogram(profile) : string.Empty;

        if (monogram.Length > 0)
        {
            using (plugin.Fonts.Header.PushSafe())
            {
                var glyphSize = 26f * scale;
                var measured = ImGui.CalcTextSize(monogram) * (glyphSize / ImGui.GetFontSize());

                drawList.AddText(
                    ImGui.GetFont(), glyphSize, centre - (measured / 2f), ImGui.GetColorU32(colour), monogram, 0f);
            }
        }

        if (!ImGui.IsMouseHoveringRect(origin, origin + new Vector2(size, size)))
            return;

        var completeness = $"{(int)MathF.Round(target * 100f)}% of the fields most readers look for are filled in.";

        if (profile is null)
        {
            UiHelpers.WrappedTooltip(completeness);
            return;
        }

        drawList.AddCircleFilled(
            centre, radius - (2f * scale), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.55f)));

        using (plugin.Fonts.Icon.PushSafe())
        {
            var glyph = FontAwesomeIcon.Camera.ToIconString();
            var glyphSize = ImGui.CalcTextSize(glyph);

            drawList.AddText(centre - (glyphSize / 2f), ImGui.GetColorU32(Theme.Text), glyph);
        }

        UiHelpers.WrappedTooltip(
            portrait is null
                ? $"Click to choose a portrait.\n\n{completeness}"
                : $"Click to change your portrait, or right-click to remove it.\n\n{completeness}");

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            portraitRequested = true;

        if (portrait is not null && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            plugin.Portraits.DeleteOwn(profile.Id);

            if (plugin.Publisher.RelayIdFor(profile.Id) is { Length: > 0 } relayId)
                _ = plugin.RelayClient.DeletePortraitAsync(relayId, System.Threading.CancellationToken.None);
        }
    }

    /// Set when the medallion is clicked, and consumed by the tab that owns the file picker.
    private bool portraitRequested;

    /// Whether the medallion was clicked since this was last asked.
    public bool TakePortraitRequest()
    {
        if (!portraitRequested)
            return false;

        portraitRequested = false;
        return true;
    }

    /// The name as it will be read, and the line of small facts under it.
    private void DrawNameBlock(RoleplayProfile? profile, Vector2 origin, float width, float scale)
    {
        var drawList = ImGui.GetWindowDrawList();
        var y = origin.Y + (2f * scale);

        var named = profile is not null && !string.IsNullOrWhiteSpace(profile.Name);
        var colour = named ? NameColour(profile) : Theme.TextDisabled;

        using (plugin.Fonts.Header.PushSafe())
        {
            var text = named ? profile!.Name : "Unnamed";
            var glyphSize = 21f * scale;
            var shown = UiHelpers.Truncate(text, width * (ImGui.GetFontSize() / glyphSize));

            drawList.AddText(
                ImGui.GetFont(), glyphSize, new Vector2(origin.X, y), ImGui.GetColorU32(colour), shown, 0f);

            y += glyphSize + (5f * scale);
        }

        if (profile is null)
            return;

        var facts = new List<string>();

        if (!string.IsNullOrWhiteSpace(profile.Title))
            facts.Add(profile.Title);

        if (!string.IsNullOrWhiteSpace(profile.HouseName))
            facts.Add(profile.HouseName);

        if (!string.IsNullOrWhiteSpace(profile.Pronouns))
            facts.Add(profile.Pronouns);

        if (facts.Count > 0)
        {
            drawList.AddText(
                new Vector2(origin.X, y), ImGui.GetColorU32(Theme.TextDim),
                UiHelpers.Truncate(string.Join("  ·  ", facts), width));

            y += ImGui.GetTextLineHeight() + (6f * scale);
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, y));

        var (label, statusColour) = Present(profile.RpStatus);

        if (label.Length > 0)
        {
            ProfileFields.StatePill(label, statusColour);
            ImGui.SameLine(0f, 8f * scale);
        }

        DrawStatusIcons(profile, scale);
    }

    /// The statuses, at the size they hang under a character.
    public void DrawStatusIcons(RoleplayProfile profile, float scale)
    {
        if (profile.Statuses.Count == 0)
            return;

        var box = 20f * scale;
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var x = origin.X;

        foreach (var status in profile.Statuses)
        {
            plugin.Icons.Draw(drawList, status.IconId, new Vector2(x, origin.Y), box, 0xFFFFFFFF);

            if (ImGui.IsMouseHoveringRect(new Vector2(x, origin.Y), new Vector2(x + box, origin.Y + box)))
            {
                UiHelpers.WrappedTooltip(
                    string.IsNullOrWhiteSpace(status.Detail)
                        ? status.Label
                        : $"{status.Label}\n{status.Detail}");
            }

            x += box + (3f * scale);
        }

        ImGui.Dummy(new Vector2(x - origin.X, box));
    }

    /// The switcher and the four things that can be done to a profile.
    private Action DrawControls(
        RoleplayProfile? profile, IReadOnlyList<RoleplayProfile> all, ref string? editingId,
        Vector2 origin, float width, float scale)
    {
        var action = Action.None;

        ImGui.SetCursorScreenPos(origin);
        ImGui.SetNextItemWidth(width);

        if (ImGui.BeginCombo("##profilepicker", profile is null ? "No Profiles" : DisplayName(profile)))
        {
            foreach (var candidate in all)
            {
                if (ImGui.Selectable($"{DisplayName(candidate)}##{candidate.Id}", candidate.Id == profile?.Id))
                    editingId = candidate.Id;
            }

            ImGui.EndCombo();
        }

        var button = 24f * scale;
        var gap = 6f * scale;
        var buttons = profile is null ? 1 : 4;
        var row = (button * buttons) + (gap * (buttons - 1));

        ImGui.SetCursorScreenPos(new Vector2(origin.X + width - row, ImGui.GetCursorScreenPos().Y + (6f * scale)));

        if (EchoButton.BareIcon("##newprofile", plugin.Fonts.Icon, FontAwesomeIcon.Plus, button, "New Profile"))
            action = Action.New;

        if (profile is null)
            return action;

        ImGui.SameLine(0f, gap);

        if (EchoButton.BareIcon("##previewprofile", plugin.Fonts.Icon, FontAwesomeIcon.Eye, button,
                "Preview - see this the way everybody else does"))
        {
            action = Action.Preview;
        }

        ImGui.SameLine(0f, gap);

        if (EchoButton.BareIcon("##duplicateprofile", plugin.Fonts.Icon, FontAwesomeIcon.Copy, button,
                "Duplicate this profile. The copy is not worn by anybody until you say so."))
        {
            action = Action.Duplicate;
        }

        ImGui.SameLine(0f, gap);

        if (EchoButton.BareIcon("##deleteprofile", plugin.Fonts.Icon, FontAwesomeIcon.TrashAlt, button,
                "Delete this profile", colourOverride: Theme.Bad))
        {
            action = Action.Delete;
        }

        return action;
    }

    /// The footer: which character this is, and whether they are wearing what is on screen.
    private bool DrawBinding(RoleplayProfile profile, float scale)
    {
        var characterKey = plugin.LocalCharacterKey;

        if (string.IsNullOrEmpty(characterKey))
        {
            ImGui.TextColored(Theme.TextDim, "Log in to a character to wear a profile.");
            return false;
        }

        var bound = plugin.Profiles.BoundProfileId(characterKey);
        var wearing = string.Equals(bound, profile.Id, StringComparison.Ordinal);

        var origin = ImGui.GetCursorScreenPos();
        var lineHeight = ImGui.GetTextLineHeight();

        var button = MathF.Max(22f * scale, EchoButton.ContentSize("Wear This").Y);
        var pill = ProfileFields.StatePillHeight;

        var trailingHeight = wearing ? pill : button;
        var rowHeight = MathF.Max(lineHeight, MathF.Max(pill, button));

        var textY = origin.Y + ((rowHeight - lineHeight) / 2f);
        var trailingY = origin.Y + ((rowHeight - trailingHeight) / 2f);

        var inkCentre = UiHelpers.TextInkCentre();

        using (plugin.Fonts.Icon.PushSafe())
        {
            UiHelpers.DrawIconSized(
                ImGui.GetWindowDrawList(), FontAwesomeIcon.User,
                new Vector2(origin.X + (6f * scale), textY + inkCentre),
                12f * scale, ImGui.GetColorU32(Theme.TextDim));
        }

        var textX = origin.X + (18f * scale);

        ImGui.SetCursorScreenPos(new Vector2(textX, textY));
        ImGui.TextColored(Theme.TextDim, characterKey);

        var trailingX = textX + ImGui.GetItemRectSize().X + (10f * scale);
        ImGui.SetCursorScreenPos(new Vector2(trailingX, trailingY));

        var wear = false;

        if (wearing)
        {
            ProfileFields.StatePill("Wearing This", Theme.Good);
        }
        else
        {
            wear = EchoButton.Draw("##wearprofile", "Wear This", new Vector2(0f, button),
                enabled: !plugin.Profiles.ReadOnly,
                tooltip: "Use this profile on this character. Others see it, and the statuses tab edits it.");
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + rowHeight));
        ImGui.Dummy(Vector2.Zero);

        return wear;
    }

    private static float Completeness(RoleplayProfile? profile)
    {
        if (profile is null)
            return 0f;

        var written = 0;

        foreach (var field in Counted)
        {
            if (!string.IsNullOrWhiteSpace(field(profile)))
                written++;
        }

        return (float)written / Counted.Length;
    }

    /// The profile's own colour, or the theme's when it has not chosen one.
    private static Vector4 NameColour(RoleplayProfile? profile) =>
        profile?.NameColour is { Length: >= 3 } c
            ? new Vector4(c[0], c[1], c[2], 1f)
            : Theme.Accent;

    /// The letter in the medallion.
    private static string Monogram(RoleplayProfile? profile)
    {
        if (profile is null)
            return string.Empty;

        var source = !string.IsNullOrWhiteSpace(profile.Name) ? profile.Name : profile.ProfileName;
        var trimmed = source.TrimStart();

        return trimmed.Length == 0 ? string.Empty : char.ToUpperInvariant(trimmed[0]).ToString();
    }

    public static string DisplayName(RoleplayProfile profile) =>
        !string.IsNullOrWhiteSpace(profile.ProfileName) ? profile.ProfileName
        : !string.IsNullOrWhiteSpace(profile.Name) ? profile.Name
        : "(Unnamed)";
}
