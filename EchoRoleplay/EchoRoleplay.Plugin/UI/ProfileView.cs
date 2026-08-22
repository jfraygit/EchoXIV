using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using EchoRoleplay.Game;
using EchoRoleplay.Shared;
using EchoRoleplay.UI.Controls;

namespace EchoRoleplay.UI;

/// Somebody else's character sheet, read.
public sealed class ProfileView : Window
{
    private readonly Plugin plugin;

    /// Whose sheet is open, as a character key.
    private string characterKey = string.Empty;

    private readonly EchoTabs sections = new();

    private static readonly string[] SectionLabels = ["About", "Details", "Story"];

    private int themeColors;

    private ReportReason reportReason = ReportReason.Hateful;
    private string reportDetail = string.Empty;
    private bool reportRequested;

    private const string ReportPopup = "##reportprofile";

    /// The reasons, and the words for them.
    private static readonly (ReportReason Reason, string Label)[] ReportReasons =
    [
        (ReportReason.Hateful, "Hateful Content"),
        (ReportReason.Sexual, "Adult Content"),
        (ReportReason.Harassment, "Harassment"),
        (ReportReason.Impersonation, "Impersonation"),
        (ReportReason.Other, "Something Else"),
    ];

    /// The window at a scale of one.
    private static readonly Vector2 BaseSize = new(560f, 660f);

    public ProfileView(Plugin plugin)
        : base(
            "Roleplay Profile###EchoRoleplayProfileView",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoResize)
    {
        this.plugin = plugin;
    }

    /// Opens somebody's sheet, and remembers having read it.
    public void Open(string key)
    {
        characterKey = key;
        sections.SetImmediate(0);
        IsOpen = true;

        plugin.Directory.Request(key);

        themeStartedFor = string.Empty;

        var profile = plugin.Directory.Lookup(key, plugin.LocalCharacterKey);

        if (profile is not null)
            plugin.Contacts.Met(key, profile);
    }

    /// Opens the player's own sheet, as everybody else reads it.
    public void Preview(string key)
    {
        characterKey = key;
        sections.SetImmediate(0);
        IsOpen = true;
        themeStartedFor = string.Empty;
    }

    /// Which profile the theme currently playing belongs to, so it is started once.
    private string themeStartedFor = string.Empty;

    /// Starts this profile's theme, at most once per profile.
    private void EnsureTheme(RoleplayProfile? profile)
    {
        if (profile is null || !plugin.Configuration.PlayThemeSongs)
            return;

        if (profile.ThemeSongId == 0 || string.Equals(themeStartedFor, profile.Id, StringComparison.Ordinal))
            return;

        themeStartedFor = profile.Id;
        plugin.GameMusic.Play(profile.ThemeSongId);
    }

    /// Closing the window gives the zone its music back.
    public override void OnClose() => plugin.GameMusic.Stop();

    public override void PreDraw()
    {
        Size = BaseSize * Math.Clamp(
            plugin.Configuration.WindowScale,
            Configuration.MinimumWindowScale,
            Configuration.MaximumWindowScale);

        SizeCondition = ImGuiCond.Always;

        themeColors = Theme.Push();
    }

    public override void PostDraw() => Theme.Pop(themeColors);

    public override void Draw()
    {
        var scale = UiHelpers.Scale;

        DrawWindowBorder();

        plugin.Directory.Request(characterKey);

        var profile = plugin.Directory.Lookup(characterKey, plugin.LocalCharacterKey);

        EnsureTheme(profile);

        DrawHeader(profile, scale);

        if (profile is null)
        {
            ImGui.Dummy(new Vector2(0f, 20f * scale));
            ImGui.TextColored(Theme.TextDim, "This character's profile is no longer available.");
            return;
        }

        ImGui.Dummy(new Vector2(0f, 4f * scale));

        var origin = ImGui.GetCursorScreenPos();
        var selected = sections.Draw(
            "##profileviewsections", SectionLabels, origin, ImGui.GetContentRegionAvail().X);

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + EchoTabs.MeasureHeight() + (10f * scale)));

        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * sections.ContentAlpha);

        if (ImGui.BeginChild("##profileviewbody", ImGui.GetContentRegionAvail(), false))
        {
            switch (selected)
            {
                case 0:
                    DrawAbout(profile, scale);
                    break;

                case 1:
                    DrawDetails(profile, scale);
                    break;

                default:
                    DrawStory(profile, scale);
                    break;
            }
        }

        ImGui.EndChild();
        ImGui.PopStyleVar();

        if (reportRequested)
        {
            ImGui.OpenPopup(ReportPopup);
            reportRequested = false;
        }

        DrawReportDialog(profile, scale);
    }

    /// The report form.
    private void DrawReportDialog(RoleplayProfile? profile, float scale)
    {
        if (!ImGui.BeginPopup(ReportPopup, ImGuiWindowFlags.NoSavedSettings))
            return;

        var width = 340f * scale;

        ImGui.TextColored(Theme.Bad, "Report This Profile");

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
        ImGui.TextColored(Theme.TextDim,
            "Goes to the EchoRoleplay moderators with the profile's text attached, reported as "
            + $"{(plugin.LocalCharacterKey.Length > 0 ? plugin.LocalCharacterKey : "this character")}.");
        ImGui.PopTextWrapPos();

        ImGui.Dummy(new Vector2(0f, 8f * scale));

        var origin = ImGui.GetCursorScreenPos();
        var gap = 5f * scale;
        var x = 0f;
        var y = 0f;
        var rowHeight = 0f;

        foreach (var (reason, label) in ReportReasons)
        {
            var chipWidth = ImGui.CalcTextSize(label).X + (22f * scale);

            if (x > 0f && x + chipWidth > width)
            {
                x = 0f;
                y += rowHeight + gap;
            }

            ImGui.SetCursorScreenPos(new Vector2(origin.X + x, origin.Y + y));

            if (ProfileFields.Chip($"##reason{reason}", label, reportReason == reason))
                reportReason = reason;

            rowHeight = ImGui.GetItemRectSize().Y;
            x += chipWidth + gap;
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + y + rowHeight + (10f * scale)));

        UiHelpers.WrappingMultiline(
            "##reportdetail", ref reportDetail, ProfileReporter.MaximumDetail,
            new Vector2(width, 72f * scale));

        ImGui.TextColored(Theme.TextDim, "Anything worth adding. Optional.");

        ImGui.Dummy(new Vector2(0f, 8f * scale));

        var sending = ProfileReporter.State == ProfileReporter.SendState.Sending;

        if (EchoButton.Draw("##sendreport", sending ? "Sending..." : "Send Report",
                new Vector2(120f * scale, 28f * scale), accentOverride: Theme.Bad,
                enabled: !sending && profile is not null))
        {
            ProfileReporter.Send(
                new ProfileReport
                {
                    Subject = characterKey,
                    Reporter = plugin.LocalCharacterKey,
                    Reason = ReportReasons.First(r => r.Reason == reportReason).Label,
                    Detail = UiHelpers.Unwrap(reportDetail, width).Trim(),
                    Snapshot = profile is null ? string.Empty : ProfileReporter.Snapshot(profile),

                    Location = plugin.Location.Read(),
                    HasPortrait = plugin.Relay.PortraitStamp(characterKey).Length > 0,

                    Version = plugin.Version,
                },
                Plugin.PluginInterface.GetPluginConfigDirectory());
        }

        ImGui.SameLine(0f, 8f * scale);

        var blocked = plugin.Contacts.IsBlocked(characterKey);

        if (EchoButton.Draw("##reportblock", blocked ? "Blocked" : "Block Too",
                new Vector2(110f * scale, 28f * scale), selected: blocked, enabled: !blocked))
        {
            plugin.Contacts.SetBlocked(characterKey, profile?.Name ?? string.Empty, true);
            plugin.PlateNames.Refresh();
        }

        switch (ProfileReporter.State)
        {
            case ProfileReporter.SendState.Sent:
                ImGui.TextColored(Theme.Good, "Sent. Thank you.");
                break;

            case ProfileReporter.SendState.Failed:
                ImGui.TextColored(Theme.Warning, $"Failed: {ProfileReporter.LastError}");
                break;
        }

        ImGui.EndPopup();
    }

    /// The portrait for whoever is on screen, or null.
    private Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap? Portrait(RoleplayProfile? profile) =>
        ProfileFields.Portrait(plugin, profile, characterKey);

    /// The identity card, the same one the editor puts at the top of its own page.
    private void DrawHeader(RoleplayProfile? profile, float scale)
    {
        Theme.BeginPanel("##viewidentity", Theme.Accent, gradient: true);

        var origin = ImGui.GetCursorScreenPos();

        var width = Theme.ContentWidth;

        var medallion = 62f * scale;
        var chrome = 24f * scale;

        var colour = profile?.NameColour is { Length: >= 3 } c
            ? new Vector4(c[0], c[1], c[2], 1f)
            : Theme.Accent;

        DrawMedallion(profile, origin, medallion, colour, scale);

        var chromeWidth = DrawChrome(profile, origin, width, chrome, scale);

        var textX = origin.X + medallion + (16f * scale);
        var textWidth = MathF.Max(60f * scale, origin.X + width - chromeWidth - textX - (12f * scale));

        DrawIdentity(profile, new Vector2(textX, origin.Y), textWidth, colour, scale);

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + medallion + (2f * scale)));
        ImGui.Dummy(Vector2.Zero);

        Theme.EndPanel();
    }

    /// The portrait, or the letter standing in for one.
    private void DrawMedallion(
        RoleplayProfile? profile, Vector2 origin, float size, Vector4 colour, float scale)
    {
        var drawList = ImGui.GetWindowDrawList();
        var radius = size / 2f;
        var centre = new Vector2(origin.X + radius, origin.Y + radius);

        drawList.AddCircleFilled(
            centre, radius - (2f * scale),
            ImGui.GetColorU32(new Vector4(colour.X, colour.Y, colour.Z, 0.16f)));

        drawList.AddCircle(
            centre, radius - (2f * scale),
            ImGui.GetColorU32(new Vector4(colour.X, colour.Y, colour.Z, 0.5f)), 64, 2f * scale);

        var portrait = Portrait(profile);

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

            return;
        }

        var monogram = Monogram(profile);

        if (monogram.Length == 0)
            return;

        using (plugin.Fonts.Header.PushSafe())
        {
            var glyphSize = 26f * scale;
            var measured = ImGui.CalcTextSize(monogram) * (glyphSize / ImGui.GetFontSize());

            drawList.AddText(
                ImGui.GetFont(), glyphSize, centre - (measured / 2f), ImGui.GetColorU32(colour), monogram, 0f);
        }
    }

    /// The name, who the character behind it is, the small facts, and the status.
    private void DrawIdentity(
        RoleplayProfile? profile, Vector2 origin, float width, Vector4 colour, float scale)
    {
        var drawList = ImGui.GetWindowDrawList();
        var y = origin.Y + (2f * scale);

        var name = profile is not null && !string.IsNullOrWhiteSpace(profile.Name)
            ? profile.Name
            : characterKey.Split('@')[0];

        using (plugin.Fonts.Header.PushSafe())
        {
            var glyphSize = 21f * scale;
            var shown = UiHelpers.Truncate(name, width * (ImGui.GetFontSize() / glyphSize));

            drawList.AddText(
                ImGui.GetFont(), glyphSize, new Vector2(origin.X, y), ImGui.GetColorU32(colour), shown, 0f);

            y += glyphSize + (4f * scale);
        }

        var verified = plugin.Directory.IsVerified(characterKey, plugin.LocalCharacterKey);

        ImGui.SetCursorScreenPos(new Vector2(origin.X, y));
        ImGui.TextColored(verified ? Theme.Accent : Theme.TextDisabled, UiHelpers.Truncate(characterKey, width));

        if (verified && ImGui.IsItemHovered())
            ImGui.SetTooltip("Verified. The Lodestone confirms this character is theirs.");

        y += ImGui.GetTextLineHeight() + (5f * scale);

        if (profile is null)
            return;

        var facts = new List<string>();

        if (!string.IsNullOrWhiteSpace(profile.Title))
            facts.Add(profile.Title);

        if (!string.IsNullOrWhiteSpace(profile.HouseName))
            facts.Add(profile.HouseName);

        if (!string.IsNullOrWhiteSpace(profile.Nickname))
            facts.Add($"\"{profile.Nickname}\"");

        if (!string.IsNullOrWhiteSpace(profile.Pronouns))
            facts.Add(profile.Pronouns);

        if (facts.Count > 0)
        {
            drawList.AddText(
                new Vector2(origin.X, y), ImGui.GetColorU32(Theme.TextDim),
                UiHelpers.Truncate(string.Join("  ·  ", facts), width));

            y += ImGui.GetTextLineHeight() + (6f * scale);
        }

        var (label, statusColour) = ProfileHeader.Present(profile.RpStatus);

        if (label.Length == 0)
            return;

        ImGui.SetCursorScreenPos(new Vector2(origin.X, y));
        ProfileFields.StatePill(label, statusColour);
    }

    /// The four things this window can do, right-aligned along the card's top edge.
    private float DrawChrome(RoleplayProfile? profile, Vector2 origin, float width, float chrome, float scale)
    {
        var gap = 6f * scale;
        var right = origin.X + width;

        ImGui.SetCursorScreenPos(new Vector2(right - chrome, origin.Y));

        if (EchoButton.BareIcon("##closeview", plugin.Fonts.Icon, FontAwesomeIcon.Times, chrome, "Close"))
            IsOpen = false;

        ImGui.SetCursorScreenPos(new Vector2(right - (chrome * 2f) - gap, origin.Y));

        var playing = plugin.Configuration.PlayThemeSongs;

        if (EchoButton.BareIcon("##themetoggle", plugin.Fonts.Icon, FontAwesomeIcon.Music, chrome,
                playing ? "Theme songs on. Click to silence them." : "Theme songs off. Click to hear them.",
                selected: playing))
        {
            ToggleThemeSongs(profile);
        }

        var blocked = plugin.Contacts.IsBlocked(characterKey);

        ImGui.SetCursorScreenPos(new Vector2(right - (chrome * 3f) - (gap * 2f), origin.Y));

        if (EchoButton.BareIcon("##blockthem", plugin.Fonts.Icon,
                blocked ? FontAwesomeIcon.EyeSlash : FontAwesomeIcon.Eye, chrome,
                blocked
                    ? "Blocked. Click to see them again."
                    : "Block - hides their profile, statuses and roleplay name everywhere",
                selected: blocked, colourOverride: blocked ? Theme.Bad : null))
        {
            plugin.Contacts.SetBlocked(characterKey, profile?.Name ?? string.Empty, !blocked);

            plugin.PlateNames.Refresh();

            if (!blocked)
            {
                plugin.GameMusic.Stop();
                IsOpen = false;
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(right - (chrome * 4f) - (gap * 3f), origin.Y));

        if (EchoButton.BareIcon("##reportthem", plugin.Fonts.Icon, FontAwesomeIcon.Flag, chrome,
                "Report this profile"))
        {
            ProfileReporter.Reset();
            reportReason = ReportReason.Hateful;
            reportDetail = string.Empty;
            reportRequested = true;
        }

        return (chrome * 4f) + (gap * 3f);
    }

    /// The letter in the medallion, taken from the roleplay name where there is one - the name the medallion
    /// is standing in for.
    private string Monogram(RoleplayProfile? profile)
    {
        var source = profile is not null && !string.IsNullOrWhiteSpace(profile.Name)
            ? profile.Name
            : characterKey;

        var trimmed = source.TrimStart();

        return trimmed.Length == 0 ? string.Empty : char.ToUpperInvariant(trimmed[0]).ToString();
    }

    /// Flips the standing preference, and makes it true of the profile already on screen.
    private void ToggleThemeSongs(RoleplayProfile? profile)
    {
        var on = !plugin.Configuration.PlayThemeSongs;

        plugin.Configuration.PlayThemeSongs = on;
        plugin.Configuration.Save();

        if (!on)
        {
            plugin.GameMusic.Stop();
            return;
        }

        if (profile is { ThemeSongId: not 0 })
            plugin.GameMusic.Play(profile.ThemeSongId);
    }

    /// What there is to notice about somebody standing in front of you.
    private void DrawAbout(RoleplayProfile profile, float scale)
    {
        if (profile.Statuses.Count == 0)
        {
            ImGui.TextColored(Theme.TextDim, "Nothing to notice here yet.");
            return;
        }

        DrawStatuses(profile, scale);
        ImGui.Dummy(new Vector2(0f, 10f * scale));
    }

    private void DrawStatuses(RoleplayProfile profile, float scale)
    {
        Theme.BeginPanel("##viewstatuses");

        Theme.PanelHeader("You Notice");

        var box = ImGui.GetTextLineHeight() * 1.4f;
        var drawList = ImGui.GetWindowDrawList();

        foreach (var status in profile.Statuses)
        {
            var origin = ImGui.GetCursorScreenPos();

            plugin.Icons.Draw(drawList, status.IconId, origin, box, 0xFFFFFFFF);

            ImGui.SetCursorScreenPos(new Vector2(origin.X + box + (8f * scale), origin.Y));
            ImGui.BeginGroup();

            ImGui.TextUnformatted(string.IsNullOrWhiteSpace(status.Label) ? "(Unnamed)" : status.Label);

            if (!string.IsNullOrWhiteSpace(status.Detail))
                ImGui.TextColored(Theme.TextDim, status.Detail);

            ImGui.EndGroup();

            ImGui.SetCursorScreenPos(
                new Vector2(origin.X, MathF.Max(origin.Y + box, ImGui.GetItemRectMax().Y) + (5f * scale)));
        }

        Theme.EndPanel();
    }

    private void DrawDetails(RoleplayProfile profile, float scale)
    {
        var glance = new List<(string Label, string Value)>
        {
            ("Race", profile.Race),
            ("Age", profile.Age),
            ("Apparent Age", profile.ApparentAge),
            ("Birthplace", profile.Birthplace),
            ("Residence", profile.Residence),
            ("Occupation", profile.Occupation),
            ("Height", profile.Height),
            ("Build", profile.Build),
            ("Eyes", profile.Eyes),
            ("Hair", profile.Hair),
            ("Marks", profile.Marks),
            ("Alignment", profile.Alignment),
            ("Voice", profile.Voice),
        };

        DrawFactPanel("##viewglance", "At A Glance", glance, scale);

        var lists = plugin.Lists;

        var eorzea = new List<(string Label, string Value)>
        {
            ("Nameday", profile.Nameday),
            ("Guardian Deity", Game.GameLists.NameOf(lists.Deities, profile.GuardianDeityId)),
            ("Grand Company", Game.GameLists.NameOf(lists.GrandCompanies, profile.GrandCompanyId)),
            ("Free Company", profile.FreeCompany),
        };

        ImGui.Dummy(new Vector2(0f, 10f * scale));
        DrawFactPanel("##vieweorzea", "Eorzea", eorzea, scale);

        if (profile.Jobs.Count > 0)
        {
            ImGui.Dummy(new Vector2(0f, 10f * scale));
            DrawJobs(profile, scale);
        }

        if (profile.CustomFields.Count > 0)
        {
            var custom = new List<(string Label, string Value)>();

            foreach (var field in profile.CustomFields)
                custom.Add((field.Label, field.Value));

            ImGui.Dummy(new Vector2(0f, 10f * scale));
            DrawFactPanel("##viewcustom", "More", custom, scale);
        }

        ImGui.Dummy(new Vector2(0f, 10f * scale));
    }

    /// A panel of label-and-value pairs, with the empty ones left out entirely.
    private static void DrawFactPanel(
        string id, string heading, IReadOnlyList<(string Label, string Value)> facts, float scale)
    {
        var written = 0;

        foreach (var fact in facts)
        {
            if (!string.IsNullOrWhiteSpace(fact.Value))
                written++;
        }

        if (written == 0)
            return;

        Theme.BeginPanel(id);
        Theme.PanelHeader(heading);

        var row = ProfileFields.FieldRow.Begin(2, 190f);

        foreach (var (label, value) in facts)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            ImGui.BeginGroup();
            ImGui.TextColored(Theme.TextDim, label);

            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + row.Width);
            ImGui.TextUnformatted(value);
            ImGui.PopTextWrapPos();

            ImGui.EndGroup();

            row.Next();
        }

        row.End();
        Theme.EndPanel();

        _ = scale;
    }

    private void DrawJobs(RoleplayProfile profile, float scale)
    {
        Theme.BeginPanel("##viewjobs");
        Theme.PanelHeader("Jobs");

        var available = Theme.ContentWidth;
        var gap = 5f * scale;
        var origin = ImGui.GetCursorScreenPos();

        var x = 0f;
        var y = 0f;
        var rowHeight = 0f;

        foreach (var job in plugin.Lists.Jobs)
        {
            if (!profile.Jobs.Contains(job.Id))
                continue;

            var width = ImGui.CalcTextSize(job.Abbreviation).X + (22f * scale);

            if (x > 0f && x + width > available)
            {
                x = 0f;
                y += rowHeight + gap;
            }

            ImGui.SetCursorScreenPos(new Vector2(origin.X + x, origin.Y + y));

            ProfileFields.Chip($"##viewjob{job.Id}", job.Abbreviation, true, job.Name);

            rowHeight = ImGui.GetItemRectSize().Y;
            x += width + gap;
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + y + rowHeight));
        Theme.EndPanel();
    }

    private static readonly (string Heading, Func<RoleplayProfile, string> Get)[] StorySections =
    [
        ("Appearance", p => p.Appearance),
        ("Personality", p => p.Personality),
        ("Backstory", p => p.Backstory),
        ("Rumours", p => p.Rumours),
        ("Hooks", p => p.Hooks),
    ];

    private static void DrawStory(RoleplayProfile profile, float scale)
    {
        var any = false;

        foreach (var (heading, get) in StorySections)
        {
            var text = get(profile);

            if (string.IsNullOrWhiteSpace(text))
                continue;

            any = true;

            Theme.BeginPanel($"##view{heading}");
            Theme.PanelHeader(heading);

            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Theme.ContentWidth);
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();

            Theme.EndPanel();
            ImGui.Dummy(new Vector2(0f, 10f * scale));
        }

        if (!any)
            ImGui.TextColored(Theme.TextDim, "Nothing written here yet.");
    }

    /// The suite's accent frame.
    private static void DrawWindowBorder()
    {
        var scale = UiHelpers.Scale;
        var inset = 1.5f * scale;

        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetWindowPos() + new Vector2(inset, inset);
        var max = ImGui.GetWindowPos() + ImGui.GetWindowSize() - new Vector2(inset, inset);

        drawList.PushClipRectFullScreen();
        drawList.AddRect(min, max,
            ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0.75f)),
            8f * scale, ImDrawFlags.None, 2f * scale);
        drawList.PopClipRect();
    }
}
