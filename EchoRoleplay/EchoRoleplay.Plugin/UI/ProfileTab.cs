using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoRoleplay.Game;
using EchoRoleplay.Shared;
using EchoRoleplay.UI.Controls;

namespace EchoRoleplay.UI;

/// The character sheet.
public sealed class ProfileTab
{
    private readonly Plugin plugin;
    private readonly EchoTabs sections = new();

    private static readonly string[] SectionLabels = ["Identity", "Details", "Story"];

    private const int IdentitySection = 0;
    private const int DetailsSection = 1;
    private const int StorySection = 2;

    /// Which profile is on screen.
    private string? editingId;

    /// Which long-form field Story is showing.
    private int storyField;

    /// Where the segmented control's highlight currently is - x, then width.
    private Vector2 storySlide;

    /// The card above the sections: the profile as it reads, and the switcher.
    private readonly ProfileHeader header;

    /// Drag to reposition, scroll to zoom, Okay saves.
    private readonly ImageCropDialog cropper = new();

    /// The Eorzean calendar behind the nameday field.
    private readonly NamedayPicker namedayPicker = new();

    /// The theme song picker.
    private readonly MusicPicker musicPicker = new();

    /// Story's working copy, holding the soft line breaks its box put in.
    private string storyBuffer = string.Empty;

    /// Which profile and field storyBuffer currently belongs to, so switching either one reloads it instead
    /// of pasting one field's text into another.
    private string storyBufferKey = string.Empty;

    /// The width Story's box was drawn at last frame, which is the width its soft breaks have to be removed
    /// at.
    private float storyWidth;

    private static readonly (RpStatus Value, string Label)[] RpStatuses =
    [
        (RpStatus.Unspecified, "Not Said"),
        (RpStatus.InCharacter, "In Character"),
        (RpStatus.OutOfCharacter, "Out Of Character"),
        (RpStatus.LookingForRp, "Looking For RP"),
        (RpStatus.InAScene, "In A Scene"),
        (RpStatus.DoNotDisturb, "Do Not Disturb"),
    ];

    /// The at-a-glance grid, as a list rather than as forty hand-written blocks.
    private static readonly (string Label, Func<RoleplayProfile, string> Get, Action<RoleplayProfile, string> Set, string? Hint)[]
        Glance =
        [
            ("Age", p => p.Age, (p, v) => p.Age = v, null),
            ("Apparent Age", p => p.ApparentAge, (p, v) => p.ApparentAge = v, null),

            ("Race", p => p.Race, (p, v) => p.Race = v, null),

            ("Birthplace", p => p.Birthplace, (p, v) => p.Birthplace = v, null),
            ("Residence", p => p.Residence, (p, v) => p.Residence = v, null),
            ("Occupation", p => p.Occupation, (p, v) => p.Occupation = v, null),
            ("Height", p => p.Height, (p, v) => p.Height = v, null),
            ("Build", p => p.Build, (p, v) => p.Build = v, null),
            ("Eyes", p => p.Eyes, (p, v) => p.Eyes = v, null),
            ("Hair", p => p.Hair, (p, v) => p.Hair = v, null),
            ("Marks", p => p.Marks, (p, v) => p.Marks = v, null),
            ("Alignment", p => p.Alignment, (p, v) => p.Alignment = v, null),
            ("Voice", p => p.Voice, (p, v) => p.Voice = v, null),
        ];

    private static readonly (string Label, Func<RoleplayProfile, string> Get, Action<RoleplayProfile, string> Set)[]
        StoryFields =
        [
            ("Appearance", p => p.Appearance, (p, v) => p.Appearance = v),
            ("Personality", p => p.Personality, (p, v) => p.Personality = v),
            ("Backstory", p => p.Backstory, (p, v) => p.Backstory = v),

            ("Rumours", p => p.Rumours, (p, v) => p.Rumours = v),

            ("Hooks", p => p.Hooks, (p, v) => p.Hooks = v),
        ];

    /// The segmented control's labels, taken from the field list so the two cannot drift apart.
    private static readonly string[] StorySectionLabels = [.. StoryFields.Select(f => f.Label)];

    public ProfileTab(Plugin plugin)
    {
        this.plugin = plugin;
        header = new ProfileHeader(plugin);
        sections.SetImmediate(Math.Clamp(plugin.Configuration.LastProfileSection, 0, SectionLabels.Length - 1));
    }

    private ProfileStore Store => plugin.Profiles;

    public void Draw()
    {
        var scale = UiHelpers.Scale;

        if (Store.ReadOnly)
        {
            ImGui.TextColored(Theme.Bad, "Your profiles file could not be read, so nothing will be saved.");
            ImGui.TextColored(Theme.TextDim, "The file has been left alone rather than replaced. Check the log.");
            ImGui.Dummy(new Vector2(0f, 8f * scale));
        }

        var profile = Selected();

        ImGui.Dummy(new Vector2(0f, 6f * scale));

        switch (header.Draw(profile, Store.All, ref editingId))
        {
            case ProfileHeader.Action.New:
                Store.FlushPending(force: true);
                editingId = Store.Create(NewProfileName()).Id;
                ResetStoryBuffer();
                break;

            case ProfileHeader.Action.Duplicate when profile is not null:
                Store.FlushPending(force: true);
                editingId = Store.Duplicate(profile).Id;
                ResetStoryBuffer();
                break;

            case ProfileHeader.Action.Delete:
                ImGui.OpenPopup(DeletePopup);
                break;

            case ProfileHeader.Action.Wear when profile is not null:
                Store.Bind(plugin.LocalCharacterKey, profile.Id);
                break;

            case ProfileHeader.Action.Preview when profile is not null:
                Preview(profile);
                break;
        }


        cropper.Draw();

        if (profile is not null && header.TakePortraitRequest())
            ChoosePortrait(profile);

        if (profile is not null)
            DrawDeleteConfirmation(profile, scale);

        if (profile is null)
        {
            DrawEmptyState(scale);
            return;
        }

        ImGui.Dummy(new Vector2(0f, 10f * scale));

        var origin = ImGui.GetCursorScreenPos();
        var selected = sections.Draw(
            "##echorpprofilesections", SectionLabels, origin, ImGui.GetContentRegionAvail().X);

        if (selected != plugin.Configuration.LastProfileSection)
        {
            plugin.Configuration.LastProfileSection = selected;
            plugin.Configuration.Save();
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + EchoTabs.MeasureHeight() + (10f * scale)));

        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * sections.ContentAlpha);
        ImGui.PushID(profile.Id);

        switch (selected)
        {
            case IdentitySection:
                DrawScrolling("##identity", () => DrawIdentity(profile, scale));
                break;

            case DetailsSection:
                DrawScrolling("##details", () => DrawDetails(profile, scale));
                break;

            case StorySection:
                DrawStory(profile, scale);
                break;
        }

        ImGui.PopID();
        ImGui.PopStyleVar();
    }

    /// Everything but Story scrolls, because both are lists of fields longer than the window.
    private static void DrawScrolling(string id, Action content)
    {
        if (ImGui.BeginChild(id, ImGui.GetContentRegionAvail(), false))
            content();

        ImGui.EndChild();
    }

    /// The profile being edited, following the character's binding until somebody picks otherwise.
    private RoleplayProfile? Selected()
    {
        if (editingId is not null && Store.ById(editingId) is { } chosen)
            return chosen;

        editingId = null;

        return Store.ForCharacter(plugin.LocalCharacterKey) ?? Store.All.FirstOrDefault();
    }

    private const string DeletePopup = "##confirmdelete";

    /// Deleting a profile is the one irreversible thing in this tab, so it is asked about.
    private void DrawDeleteConfirmation(RoleplayProfile profile, float scale)
    {
        if (!ImGui.BeginPopup(DeletePopup))
            return;

        ImGui.TextColored(Theme.Bad, "Delete Profile");
        ImGui.TextUnformatted(ProfileHeader.DisplayName(profile));
        ImGui.TextColored(Theme.TextDim, "This cannot be undone.");
        ImGui.Dummy(new Vector2(0f, 6f * scale));

        if (EchoButton.Draw("##confirmdeleteyes", "Delete", new Vector2(92f * scale, 26f * scale),
                accentOverride: Theme.Bad))
        {
            Store.Delete(profile.Id);
            editingId = null;
            ResetStoryBuffer();
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine(0f, 8f * scale);

        if (EchoButton.Draw("##confirmdeleteno", "Cancel", new Vector2(92f * scale, 26f * scale)))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    /// Nothing written yet.
    private void DrawEmptyState(float scale)
    {
        ImGui.Dummy(new Vector2(0f, 20f * scale));

        Theme.BeginPanel("##noprofiles");

        ImGui.TextColored(Theme.Text, "No Profiles Yet");
        ImGui.Dummy(new Vector2(0f, 4f * scale));

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Theme.ContentWidth);
        ImGui.TextColored(Theme.TextDim,
            "A profile is one character. Make one for each person you play - alts and disguises are "
            + "the reason there can be several - and choose which one this character wears.");
        ImGui.PopTextWrapPos();

        ImGui.Dummy(new Vector2(0f, 10f * scale));

        if (EchoButton.Draw("##firstprofile", "New Profile", new Vector2(0f, 28f * scale),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Plus, enabled: !Store.ReadOnly))
        {
            editingId = Store.Create(NewProfileName()).Id;
            ResetStoryBuffer();
        }

        Theme.EndPanel();
    }


    /// Opens this profile in the reader, as everybody else sees it.
    private void Preview(RoleplayProfile profile)
    {
        var key = plugin.LocalCharacterKey;

        if (key.Length == 0 || !string.Equals(Store.BoundProfileId(key), profile.Id, StringComparison.Ordinal))
        {
            previewNeedsWearing = true;
            return;
        }

        previewNeedsWearing = false;

        plugin.ProfileView.Preview(key);
    }

    /// Set when Preview was pressed on a profile nobody is wearing, so the sheet can say why nothing happened
    /// rather than the button appearing broken.
    private bool previewNeedsWearing;

    /// Picks a file, then hands it to the crop dialog.
    private void ChoosePortrait(RoleplayProfile profile)
    {
        plugin.FileDialogs.OpenFileDialog(
            "Choose a portrait",
            "Images{.png,.jpg,.jpeg,.bmp,.gif}",
            (chosen, paths) =>
            {
                if (!chosen || paths.Count == 0)
                    return;

                _ = cropper.OpenAsync(paths[0], jpeg =>
                {
                    if (!plugin.Portraits.SaveOwn(profile.Id, jpeg))
                        return;

                    if (plugin.Publisher.RelayIdFor(profile.Id) is { Length: > 0 } relayId)
                        plugin.Portraits.Upload(profile.Id, relayId);
                });
            },
            1);
    }

    /// Whether this profile is on the relay, and the one control that changes that.
    private void DrawSharing(RoleplayProfile profile, float scale)
    {
        var key = plugin.LocalCharacterKey;
        var worn = key.Length > 0
                   && string.Equals(plugin.Profiles.BoundProfileId(key), profile.Id, StringComparison.Ordinal);

        Theme.BeginPanel("##sharing");
        Theme.PanelHeader("Sharing");

        if (!worn)
        {
            ImGui.TextColored(Theme.TextDim,
                key.Length == 0
                    ? "Log in to publish a profile."
                    : "Wear this profile on a character to publish it.");

            Theme.EndPanel();
            return;
        }

        if (previewNeedsWearing)
        {
            ImGui.TextColored(Theme.Warning, "Wear this profile on a character to preview it.");
            ImGui.Dummy(new Vector2(0f, 6f * scale));
        }

        var published = plugin.Publisher.IsPublished(key);
        var reached = plugin.Publisher.RelayReached;
        var working = plugin.Publisher.State == ProfilePublisher.Activity.Working;

        var (label, colour) = !reached
            ? ("Checking", Theme.TextDim)
            : published
                ? ("Published", Theme.Accent)
                : ("Not Published", Theme.TextDim);

        var rowStart = ImGui.GetCursorScreenPos();

        ProfileFields.StatePill(label, colour);

        var buttonSize = new Vector2(140f * scale, ProfileFields.StatePillHeight);

        ImGui.SameLine();
        ImGui.SetCursorScreenPos(new Vector2(
            Math.Max(rowStart.X, rowStart.X + Theme.ContentWidth - buttonSize.X),
            rowStart.Y));

        if (published)
        {
            if (EchoButton.Draw("##withdraw", "Withdraw", buttonSize, enabled: !working,
                    tooltip: "Takes it off the relay. Anyone who already read it keeps their copy."))
            {
                plugin.Publisher.Withdraw(key);
            }
        }
        else
        {
            if (EchoButton.Draw("##publish", "Publish", buttonSize, enabled: !working,
                    tooltip: "Lets anyone with EchoRoleplay read this profile when they meet this character."))
            {
                plugin.Publisher.Publish(key);
            }
        }

        ImGui.Dummy(new Vector2(0f, 6f * scale));

        ImGui.TextColored(Theme.TextDim, published
            ? "Anyone with EchoRoleplay can read this when they meet you."
            : "Only you can see this profile.");

        if (plugin.Publisher.Message.Length > 0)
        {
            ImGui.Dummy(new Vector2(0f, 4f * scale));

            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Theme.ContentWidth);
            ImGui.TextColored(
                plugin.Publisher.State == ProfilePublisher.Activity.Failed ? Theme.Warning : Theme.TextDim,
                plugin.Publisher.Message);
            ImGui.PopTextWrapPos();
        }

        ImGui.Dummy(new Vector2(0f, 8f * scale));
        Theme.PanelDivider(Theme.ContentWidth);
        ImGui.Dummy(new Vector2(0f, 8f * scale));

        DrawVerification(key, scale);

        Theme.EndPanel();
    }

    /// Where the verified badge is earned.
    private void DrawVerification(string characterKey, float scale)
    {
        var verifier = plugin.Verifier;

        if (verifier.IsVerified(characterKey))
        {
            ProfileFields.StatePill("Verified", Theme.Accent);
            ImGui.Dummy(new Vector2(0f, 6f * scale));
            ImGui.TextColored(Theme.TextDim, "The Lodestone confirms this character is yours.");
            return;
        }

        if (verifier.Available == false)
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Theme.ContentWidth);
            ImGui.TextColored(Theme.TextDim, "Character verification is unavailable at the moment.");
            ImGui.PopTextWrapPos();
            return;
        }

        var working = verifier.State is ProfileVerifier.Step.Working or ProfileVerifier.Step.Checking;

        if (verifier.State == ProfileVerifier.Step.Idle)
        {
            if (EchoButton.Draw("##verify", "Verify Character", new Vector2(160f * scale, 26f * scale),
                    enabled: !working,
                    tooltip: "Proves this character is yours, using your own Lodestone profile."))
            {
                verifier.Begin(characterKey);
            }

            ImGui.Dummy(new Vector2(0f, 6f * scale));
            ImGui.TextColored(Theme.TextDim, "Optional. Shows a badge others can trust.");
        }
        else
        {
            ImGui.TextColored(Theme.TextDim, "1. Copy this code:");
            ImGui.Dummy(new Vector2(0f, 4f * scale));

            var code = verifier.Code;
            ImGui.SetNextItemWidth(160f * scale);
            ImGui.InputText("##verifycode", ref code, 32, ImGuiInputTextFlags.ReadOnly);

            ImGui.Dummy(new Vector2(0f, 6f * scale));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Theme.ContentWidth);
            ImGui.TextColored(Theme.TextDim,
                "2. Paste it anywhere into your character's Lodestone profile and save. You can remove "
                + "it once this says verified.");
            ImGui.PopTextWrapPos();

            ImGui.Dummy(new Vector2(0f, 6f * scale));
            ImGui.TextColored(Theme.TextDim, "3. Paste your character's Lodestone address:");
            ImGui.Dummy(new Vector2(0f, 4f * scale));

            ImGui.SetNextItemWidth(Theme.ContentWidth);
            ImGui.InputTextWithHint("##lodestoneurl", "https://na.finalfantasyxiv.com/lodestone/character/...",
                ref lodestoneUrl, 200);

            ImGui.Dummy(new Vector2(0f, 6f * scale));

            var checking = verifier.State == ProfileVerifier.Step.Checking;

            if (EchoButton.Draw("##verifycheck", checking ? "Checking" : "Check",
                    new Vector2(120f * scale, 26f * scale), enabled: !working))
            {
                verifier.Check(characterKey, lodestoneUrl);
            }

            ImGui.SameLine();

            if (EchoButton.Draw("##verifycancel", "Cancel", new Vector2(100f * scale, 26f * scale),
                    enabled: !working))
            {
                verifier.Cancel();
            }
        }

        if (verifier.Message.Length > 0)
        {
            ImGui.Dummy(new Vector2(0f, 6f * scale));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Theme.ContentWidth);
            ImGui.TextColored(verifier.Failed ? Theme.Warning : Theme.TextDim, verifier.Message);
            ImGui.PopTextWrapPos();
        }
    }

    /// Held here rather than in the verifier, because it is a text box's contents rather than anything about
    /// verification - and it is deliberately not saved anywhere.
    private string lodestoneUrl = string.Empty;

    private void DrawIdentity(RoleplayProfile profile, float scale)
    {
        Theme.BeginPanel("##thisprofile");
        Theme.PanelHeader("This Profile");

        var row = ProfileFields.FieldRow.Begin(2, 200f);

        var profileName = profile.ProfileName;
        if (ProfileFields.Text("##profilename", "Profile Name", ref profileName, ProfileLimits.ProfileName,
                row.Width, "Seraphine, The Disguise",
                "What you call this profile in your own list. Nobody else ever sees it."))
        {
            profile.ProfileName = profileName;
            Edited(profile);
        }

        row.Next();

        DrawRpStatus(profile, row.Width);

        row.Next();
        row.End();

        Theme.EndPanel();

        ImGui.Dummy(new Vector2(0f, 10f * scale));

        DrawSharing(profile, scale);

        ImGui.Dummy(new Vector2(0f, 10f * scale));

        Theme.BeginPanel("##nameblock");
        Theme.PanelHeader("Name");

        row = ProfileFields.FieldRow.Begin(2, 200f);

        var name = profile.Name;
        if (ProfileFields.Text("##rpname", "Name", ref name, ProfileLimits.Name, row.Width,
                tooltip: "The name they roleplay under, which is often not their character name.",
                titleCase: true))
        {
            profile.Name = name;
            Edited(profile);
        }

        row.Next();

        var nickname = profile.Nickname;
        if (ProfileFields.Text("##nickname", "Nickname", ref nickname, ProfileLimits.Nickname, row.Width,
                titleCase: true))
        {
            profile.Nickname = nickname;
            Edited(profile);
        }

        row.Next();

        var house = profile.HouseName;
        if (ProfileFields.Text("##house", "House Name", ref house, ProfileLimits.HouseName, row.Width,
                tooltip: "A house, clan or family name.", titleCase: true))
        {
            profile.HouseName = house;
            Edited(profile);
        }

        row.Next();

        var title = profile.Title;
        if (ProfileFields.Text("##title", "Title", ref title, ProfileLimits.Title, row.Width))
        {
            profile.Title = title;
            Edited(profile);
        }

        row.Next();

        var pronouns = profile.Pronouns;
        if (ProfileFields.Text("##pronouns", "Pronouns", ref pronouns, ProfileLimits.Pronouns, row.Width))
        {
            profile.Pronouns = pronouns;
            Edited(profile);
        }

        row.Next();

        DrawNameColour(profile, scale);

        row.Next();
        row.End();

        ImGui.Dummy(new Vector2(0f, 8f * scale));

        DrawThemeSong(profile, scale);

        Theme.EndPanel();

        ImGui.Dummy(new Vector2(0f, 10f * scale));

    }

    /// The character's theme, chosen from the game's own music, with a way to hear it.
    private void DrawThemeSong(RoleplayProfile profile, float scale)
    {
        var button = 24f * scale;
        var width = MathF.Max(80f * scale, Theme.ContentWidth - button - (10f * scale));

        var theme = profile.ThemeSongId;

        if (musicPicker.Draw("Theme Song", ref theme, width, plugin.Music,
                "Plays for anybody who opens this profile, over the zone's own music."))
        {
            profile.ThemeSongId = theme;
            Edited(profile);

            if (theme != 0)
                plugin.GameMusic.Play(theme);
        }

        ImGui.SameLine(0f, 10f * scale);
        ImGui.BeginGroup();
        ImGui.Dummy(new Vector2(button, ImGui.GetTextLineHeight()));

        var playing = plugin.GameMusic.NowPlaying != 0;

        if (EchoButton.BareIcon("##playtheme", plugin.Fonts.Icon,
                playing ? FontAwesomeIcon.Stop : FontAwesomeIcon.Play, button,
                playing ? "Stop" : "Hear it", colourOverride: playing ? Theme.Accent : null))
        {
            if (playing)
                plugin.GameMusic.Stop();
            else if (profile.ThemeSongId != 0)
                plugin.GameMusic.Play(profile.ThemeSongId);
        }

        ImGui.EndGroup();
    }

    /// The colour the RP name is written in, and the way back to the theme's own.
    private void DrawNameColour(RoleplayProfile profile, float scale)
    {
        ImGui.BeginGroup();
        ImGui.TextColored(Theme.TextDim, "Name Colour");

        var stored = profile.NameColour;
        var colour = stored is { Length: >= 3 }
            ? new Vector3(stored[0], stored[1], stored[2])
            : new Vector3(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z);

        if (ImGui.ColorEdit3("##namecolour", ref colour, ImGuiColorEditFlags.NoInputs))
        {
            profile.NameColour = [colour.X, colour.Y, colour.Z];
            Edited(profile);
        }

        if (stored is not null)
        {
            ImGui.SameLine(0f, 8f * scale);

            if (EchoButton.Draw("##clearnamecolour", "Theme", new Vector2(0f, 22f * scale),
                    tooltip: "Back to the plugin's own accent colour."))
            {
                profile.NameColour = null;
                Edited(profile);
            }
        }

        ImGui.EndGroup();
    }

    /// GROUPED, LIKE EVERY OTHER FIELD ON THIS FORM, and that is load-bearing rather than tidiness.
    private void DrawRpStatus(RoleplayProfile profile, float width)
    {
        ImGui.BeginGroup();

        ImGui.TextColored(Theme.TextDim, "Roleplay Status");

        if (ImGui.IsItemHovered())
            UiHelpers.WrappedTooltip("Shows as a small icon beside the free company tag on your nameplate.");

        ImGui.SetNextItemWidth(width);

        var current = Array.Find(RpStatuses, s => s.Value == profile.RpStatus).Label ?? RpStatuses[0].Label;

        if (ImGui.BeginCombo("##rpstatus", current))
        {
            foreach (var (value, label) in RpStatuses)
            {
                if (!ImGui.Selectable(label, value == profile.RpStatus))
                    continue;

                profile.RpStatus = value;
                Edited(profile);
            }

            ImGui.EndCombo();
        }

        ImGui.EndGroup();
    }


    private void DrawDetails(RoleplayProfile profile, float scale)
    {
        Theme.BeginPanel("##glance");
        Theme.PanelHeader("At A Glance");

        var grid = ProfileFields.FieldRow.Begin(3, 150f);

        for (var i = 0; i < Glance.Length; i++)
        {
            var (label, get, set, hint) = Glance[i];
            var value = get(profile);

            if (ProfileFields.Text($"##glance{i}", label, ref value, ProfileLimits.ShortField, grid.Width, hint))
            {
                set(profile, value);
                Edited(profile);
            }

            grid.Next();
        }

        grid.End();

        Theme.EndPanel();

        ImGui.Dummy(new Vector2(0f, 10f * scale));
        DrawEorzea(profile, scale);

        ImGui.Dummy(new Vector2(0f, 10f * scale));
        DrawCustomFields(profile, scale);

        ImGui.Dummy(new Vector2(0f, 10f * scale));
    }

    /// The fields no port from another game thinks to include.
    private void DrawEorzea(RoleplayProfile profile, float scale)
    {
        Theme.BeginPanel("##eorzea");
        Theme.PanelHeader("Eorzea");

        var lists = plugin.Lists;
        var row = ProfileFields.FieldRow.Begin(2, 200f);

        var nameday = profile.Nameday;
        if (namedayPicker.Draw("Nameday", ref nameday, row.Width,
                "Pick a sun and a moon. Every nameday in the plugin then reads the same way."))
        {
            profile.Nameday = nameday;
            Edited(profile);
        }

        row.Next();

        var deity = profile.GuardianDeityId;
        if (ProfileFields.Choice("##deity", "Guardian Deity", lists.Deities, ref deity, row.Width))
        {
            profile.GuardianDeityId = deity;
            Edited(profile);
        }

        row.Next();

        var company = profile.GrandCompanyId;
        if (ProfileFields.Choice("##grandcompany", "Grand Company", lists.GrandCompanies, ref company, row.Width))
        {
            profile.GrandCompanyId = company;
            Edited(profile);
        }

        row.Next();

        var freeCompany = profile.FreeCompany;
        if (ProfileFields.Text("##freecompany", "Free Company", ref freeCompany, ProfileLimits.ShortField, row.Width))
        {
            profile.FreeCompany = freeCompany;
            Edited(profile);
        }

        row.Next();
        row.End();


        ImGui.Dummy(new Vector2(0f, 10f * scale));
        DrawJobs(profile, scale);

        Theme.EndPanel();
    }

    /// The classes this character uses in character, as a reflowing row of chips.
    private void DrawJobs(RoleplayProfile profile, float scale)
    {
        ImGui.TextColored(Theme.TextDim, "Jobs");
        ImGui.Dummy(new Vector2(0f, 4f * scale));

        var jobs = plugin.Lists.Jobs;

        if (jobs.Count == 0)
        {
            ImGui.TextColored(Theme.TextDim, "The game's class list could not be read.");
            return;
        }

        var available = Theme.ContentWidth;
        var chipGap = 5f * scale;
        var rowGap = 5f * scale;

        var origin = ImGui.GetCursorScreenPos();
        var x = 0f;
        var y = 0f;
        var rowHeight = 0f;

        foreach (var job in jobs)
        {
            var chipWidth = ImGui.CalcTextSize(job.Abbreviation).X + (22f * scale);

            if (x > 0f && x + chipWidth > available)
            {
                x = 0f;
                y += rowHeight + rowGap;
            }

            ImGui.SetCursorScreenPos(new Vector2(origin.X + x, origin.Y + y));

            var selected = profile.Jobs.Contains(job.Id);

            if (ProfileFields.Chip($"##job{job.Id}", job.Abbreviation, selected, job.Name))
            {
                if (selected)
                    profile.Jobs.Remove(job.Id);
                else
                    profile.Jobs.Add(job.Id);

                Edited(profile);
            }

            rowHeight = ImGui.GetItemRectSize().Y;
            x += chipWidth + chipGap;
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + y + rowHeight));
    }

    /// Label and value pairs for what the sheet did not anticipate.
    private void DrawCustomFields(RoleplayProfile profile, float scale)
    {
        Theme.BeginPanel("##customfields");
        Theme.PanelHeader($"Custom Fields  ({profile.CustomFields.Count}/{ProfileLimits.CustomFields})");

        if (profile.CustomFields.Count == 0)
        {
            ImGui.TextColored(Theme.TextDim, "Anything the fields above have no room for.");
            ImGui.Dummy(new Vector2(0f, 6f * scale));
        }

        var remove = -1;
        var buttonSize = 20f * scale;

        for (var i = 0; i < profile.CustomFields.Count; i++)
        {
            var field = profile.CustomFields[i];
            ImGui.PushID($"custom{i}");

            var rowOrigin = ImGui.GetCursorScreenPos();
            var gap = ProfileFields.ColumnGap;
            var available = Theme.ContentWidth;

            var labelWidth = MathF.Min(150f * scale, available * 0.35f);
            var valueWidth = MathF.Max(80f * scale, available - labelWidth - buttonSize - (gap * 2f));

            var label = field.Label;
            if (ProfileFields.Text("##label", "Label", ref label, ProfileLimits.CustomFieldLabel, labelWidth))
            {
                field.Label = label;
                Edited(profile);
            }

            var bottom = ImGui.GetCursorScreenPos().Y;

            ProfileFields.At(rowOrigin, labelWidth + gap);

            var value = field.Value;
            if (ProfileFields.Text("##value", "Value", ref value, ProfileLimits.CustomFieldValue, valueWidth))
            {
                field.Value = value;
                Edited(profile);
            }

            bottom = MathF.Max(bottom, ImGui.GetCursorScreenPos().Y);

            ProfileFields.At(rowOrigin, labelWidth + valueWidth + (gap * 2f));
            ImGui.Dummy(new Vector2(buttonSize, ImGui.GetTextLineHeight()));

            if (EchoButton.BareIcon("##removecustom", plugin.Fonts.Icon, FontAwesomeIcon.Times, buttonSize, "Remove"))
                remove = i;

            ImGui.PopID();
            ImGui.SetCursorScreenPos(new Vector2(rowOrigin.X, MathF.Max(bottom, ImGui.GetCursorScreenPos().Y)));
            ImGui.Dummy(new Vector2(0f, 4f * scale));
        }

        if (remove >= 0)
        {
            profile.CustomFields.RemoveAt(remove);
            Edited(profile);
        }

        if (profile.CustomFields.Count >= ProfileLimits.CustomFields)
        {
            ImGui.TextColored(Theme.TextDim, "That is the limit.");
            Theme.EndPanel();
            return;
        }

        if (EchoButton.Draw("##addcustom", "Add Field", new Vector2(0f, 24f * scale),
                iconFont: plugin.Fonts.Icon, icon: FontAwesomeIcon.Plus, enabled: !Store.ReadOnly))
        {
            profile.CustomFields.Add(new ProfileField());
            Edited(profile);
        }

        Theme.EndPanel();
    }


    private void DrawStory(RoleplayProfile profile, float scale)
    {
        storyField = ProfileFields.Segmented(
            "##storysections", StorySectionLabels, storyField, ref storySlide,
            i => StoryFields[i].Get(profile).Length > 0);

        ImGui.Dummy(new Vector2(0f, 10f * scale));

        var (label, get, set) = StoryFields[storyField];

        var line = ImGui.GetTextLineHeightWithSpacing();
        var remaining = ImGui.GetContentRegionAvail().Y;

        Theme.BeginPanel("##storypanel");
        Theme.PanelHeader(label);

        var available = Theme.ContentWidth;
        var height =
            remaining
            - (Theme.PanelPadding.Y * 2f)            - (line + (8f * scale))            - (line + (10f * scale));
        var size = new Vector2(available, MathF.Max(100f * scale, height));

        LoadStoryBuffer(profile, get, size);

        if (ProfileFields.Paragraph("##storytext", ref storyBuffer, ProfileLimits.LongForm, size))
        {
            set(profile, UiHelpers.Unwrap(storyBuffer, storyWidth));
            Edited(profile);
        }

        ImGui.Dummy(new Vector2(0f, 4f * scale));

        DrawStoryFooter(get(profile), available);

        Theme.EndPanel();
    }

    /// Fills the working copy from the profile, WRAPPED READY TO EDIT.
    private void LoadStoryBuffer(RoleplayProfile profile, Func<RoleplayProfile, string> get, Vector2 size)
    {
        var key = $"{profile.Id}:{storyField}";
        var moved = !string.Equals(storyBufferKey, key, StringComparison.Ordinal);
        var resized = MathF.Abs(storyWidth - size.X) > 1f && !ImGui.IsAnyItemActive();

        storyWidth = size.X;

        if (!moved && !resized)
            return;

        storyBuffer = UiHelpers.Wrap(get(profile), size.X);
        storyBufferKey = key;
    }

    /// How much has been written, at both ends of the line.
    private static void DrawStoryFooter(string text, float width)
    {
        var length = text.Length;

        var words = text.Split(
            [' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;

        ImGui.TextColored(Theme.TextDim, words == 1 ? "1 Word" : $"{words} Words");

        var count = $"{length}/{ProfileLimits.LongForm}";
        var countWidth = ImGui.CalcTextSize(count).X;

        ImGui.GetWindowDrawList().AddText(
            new Vector2(ImGui.GetItemRectMin().X + width - countWidth, ImGui.GetItemRectMin().Y),
            ImGui.GetColorU32(length >= ProfileLimits.LongForm ? Theme.Warning : Theme.TextDim),
            count);
    }


    /// Records an edit.
    private void Edited(RoleplayProfile profile) => Store.TouchLater(profile);

    private void ResetStoryBuffer()
    {
        storyBuffer = string.Empty;
        storyBufferKey = string.Empty;
    }

    /// Named after the character when there is one, since that is the name it will be recognised by in the
    /// list.
    private string NewProfileName()
    {
        var key = plugin.LocalCharacterKey;
        var name = key.Split('@')[0];

        return Store.All.Any(p => string.Equals(p.ProfileName, name, StringComparison.Ordinal)) || name.Length == 0
            ? $"Profile {Store.Count + 1}"
            : name;
    }
}
