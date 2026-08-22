using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoSim.Shared;
using EchoSim.Sim.Jobs;
using EchoSim.UI.Controls;

namespace EchoSim.UI;

/// The Profile tab: one player, every board they are on.
public sealed partial class MainWindow
{
    private LeaderboardProfile? profile;
    private string? profileError;
    private bool profileLoading;
    private string profileLoadedFor = string.Empty;

    /// Who the tab is showing.
    private (string Character, string World)? profileTarget;

    /// Whether arriving here came from a board, which is what puts a way back on the page.
    private bool profileFromBoard;


    private void DrawProfileTab()
    {
        ImGui.Spacing();

        var target = profileTarget ?? LocalCharacter();

        if (target is not { } who)
        {
            ImGui.TextColored(Theme.TextDim,
                "Log in to a character to see your profile, or open one from a leaderboard.");

            return;
        }

        var key = $"{who.Character}|{who.World}";
        if (key != profileLoadedFor && !profileLoading)
            LoadProfile(who.Character, who.World, key);

        if (profileFromBoard)
            DrawBackToBoard();

        if (profileLoading && profile is null)
        {
            ImGui.TextColored(Theme.TextDim, "Loading...");
            return;
        }

        if (profileError is { } error)
        {
            ImGui.TextColored(Theme.Warning, error);
            return;
        }

        if (profile is null)
        {
            DrawEmptyProfile(who);
            return;
        }

        DrawProfileHeader(profile);
        ImGui.Spacing();
        ImGui.Spacing();

        Theme.SectionHeader(Seasons.Label(profile.Season));
        ImGui.Spacing();

        ImGui.BeginChild("##profilescores", new Vector2(0, ImGui.GetContentRegionAvail().Y), false,
            ImGuiWindowFlags.NoBackground);

        DrawProfileScores(profile);

        ImGui.EndChild();
    }

    private void DrawBackToBoard()
    {
        if (EchoButton.Draw("##profileback", label: null, new Vector2(UiHelpers.S(34f), ImGui.GetFrameHeight()),
                icon: FontAwesomeIcon.ArrowLeft, iconFont: plugin.Fonts.Icon,
                tooltip: "Back to the leaderboard"))
        {
            ReturnToBoard();
        }

        ImGui.SameLine(0f, 8f);
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.TextDisabled, "Leaderboards");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }

    /// A character who is on no board yet, which for most people is what their own profile is.
    private void DrawEmptyProfile((string Character, string World) who)
    {
        DrawProfileHeader(new LeaderboardProfile
        {
            Character = who.Character,
            World = who.World,
            IsYou = profileTarget is null,
        });

        ImGui.Spacing();

        ImGui.TextColored(Theme.TextDim, profileTarget is null
            ? "No scores yet. Clear one of the ranked fights with sharing on and it'll appear here."
            : "This player has no ranked scores this season.");
    }

    /// The face, the name, and how much there is below.
    private void DrawProfileHeader(LeaderboardProfile shown)
    {
        var height = PortraitSize + (ProfileCardPad * 2f);

        Theme.BeginCard("##profileheader", new Vector2(ImGui.GetContentRegionAvail().X, height));

        DrawPortrait(shown);

        ImGui.SameLine(0f, 14f);
        ImGui.BeginGroup();

        using (plugin.Fonts.Header.PushSafe())
            ImGui.TextColored(shown.IsYou ? Theme.Accent : Theme.Text, shown.Character);

        ImGui.TextColored(Theme.TextDisabled, shown.World);

        if (shown.Entries.Count > 0)
        {
            var jobs = shown.Entries.Select(e => e.JobId).Distinct().Count();

            ImGui.Spacing();
            ImGui.TextColored(Theme.TextDim,
                $"{Plural(jobs, "job")}  -  {Plural(shown.Entries.Count, "fight")}");
        }

        ImGui.EndGroup();

        Theme.EndCard();
    }

    private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

    /// The headshot, or the job glyph standing in for one.
    private void DrawPortrait(LeaderboardProfile shown)
    {
        var size = new Vector2(PortraitSize, PortraitSize);
        var texture = Game.Portraits.Get(shown.Character, shown.World);

        if (texture is not null)
        {
            ImGui.Image(texture.Handle, size);
            return;
        }

        var icon = Game.GameData.Icon(JobIconBase + BestJob(shown));

        if (icon is null)
        {
            ImGui.Dummy(size);
            return;
        }

        ImGui.Image(icon.Handle, size, Vector2.Zero, Vector2.One, Theme.TextDim);
    }

    /// The job to wear when there is no portrait: their best.
    private uint BestJob(LeaderboardProfile shown)
        => shown.Entries.Count == 0
            ? Config.SelectedJobId
            : shown.Entries.OrderByDescending(e => e.Score).First().JobId;

    private static float PortraitSize => UiHelpers.S(96f);

    /// Matches Theme.BeginCard's inner padding, which it does not expose.
    private static float ProfileCardPad => UiHelpers.S(14f);

    /// The season's fights, one card each, under a heading per job.
    private void DrawProfileScores(LeaderboardProfile shown)
    {
        if (shown.Entries.Count == 0)
        {
            DrawNoScoresAtAll(shown);
            return;
        }

        var jobs = shown.Entries
            .GroupBy(e => e.JobId)
            .Select(g => (JobId: g.Key, Entries: g.OrderByDescending(e => e.Score).ToList()))
            .OrderByDescending(g => g.Entries[0].Score)
            .ToList();

        var width = FightCardWidth(shown.Entries, out var perRow);

        for (var i = 0; i < jobs.Count; i++)
        {
            if (i > 0)
                ImGui.Spacing();

            DrawJobSection(jobs[i].JobId, jobs[i].Entries, width, perRow, shown.IsYou);
        }
    }

    /// One job's heading and its grid.
    private void DrawJobSection(
        uint jobId, List<LeaderboardProfileEntry> entries, float width, int perRow, bool isYou)
    {
        DrawJobHeading(jobId);

        var medals = entries.Any(e => e.Verified && e.Rank <= PodiumRanks);
        var height = FightCardHeight(medals);

        for (var i = 0; i < entries.Count; i++)
        {
            if (i % perRow != 0)
                ImGui.SameLine(0f, FightCardGap);

            DrawFightCard(entries[i], width, height, medals, isYou);
        }

        ImGui.Spacing();
    }

    /// The job's name, over its grid, in the season heading's own treatment.
    private static void DrawJobHeading(uint jobId)
    {
        Theme.SectionHeader(JobRegistry.For(jobId)?.Name ?? $"Job {jobId}");
        ImGui.Spacing();
    }

    /// How wide a card can be, and how many fit across.
    private static float FightCardWidth(List<LeaderboardProfileEntry> all, out int perRow)
    {
        var minWidth = MinFightCardWidth;

        foreach (var entry in all.Where(e => e.Verified && e.Rank <= PodiumRanks))
            minWidth = Math.Max(minWidth, TitleWidth(entry));

        var available = ImGui.GetContentRegionAvail().X;

        perRow = Math.Max(1, (int)((available + FightCardGap) / (minWidth + FightCardGap)));

        return ((available - (FightCardGap * (perRow - 1))) / perRow) - 1f;
    }

    /// The card width this entry's medal row needs to sit on one line.
    private static float TitleWidth(LeaderboardProfileEntry entry)
    {
        var title = PodiumTitles.For(entry.JobId, entry.Rank);

        return ImGui.CalcTextSize(title).X
               + ImGui.GetFrameHeight()
               + PodiumIconGap
               + (ProfileCardPad * 2f);
    }

    /// Places that get a medal.
    private const int PodiumRanks = 3;

    /// A profile the relay returned with nothing in it.
    private static void DrawNoScoresAtAll(LeaderboardProfile shown)
    {
        ImGui.TextColored(Theme.TextDim, shown.IsYou
            ? "No scores yet this season."
            : "No ranked scores this season.");
    }

    private static float FightCardGap => UiHelpers.S(10f);

    /// Narrow enough for two across on a small window, wide enough for "1st of 1,024".
    private static float MinFightCardWidth => UiHelpers.S(168f);

    /// One fight: what it was, how it scored, and where that puts them.
    private void DrawFightCard(
        LeaderboardProfileEntry entry, float width, float height, bool reserveMedalRow, bool isYou)
    {
        var encounter = Encounters.ByKey(entry.EncounterKey);
        var band = ParseBands.For(entry.Score);
        var colour = new Vector4(band.R / 255f, band.G / 255f, band.B / 255f, 1f);

        var podium = entry.Verified && entry.Rank <= PodiumRanks;

        Theme.BeginCard(
            $"##fight{entry.JobId}_{entry.EncounterKey}", new Vector2(width, height), colour, colour);

        ImGui.TextColored(Theme.Text, encounter?.Short ?? entry.EncounterKey);

        if (encounter is not null && ImGui.IsItemHovered())
            ImGui.SetTooltip(encounter.Name);

        if (reserveMedalRow)
            DrawFightMedal(entry, podium);

        using (plugin.Fonts.Numeric.PushSafe())
            ImGui.TextColored(colour, $"{entry.Score:F2}");

        if (entry.Verified)
        {
            ImGui.TextColored(Theme.TextDim, $"{Ordinal(entry.Rank)} of {entry.TotalEntries:N0}");
        }
        else
        {
            ImGui.TextColored(Theme.TextDisabled, "unranked");

            if (isYou)
            {
                ImGui.SameLine(0f, 6f);

                using (plugin.Fonts.Icon.PushSafe())
                    ImGui.TextColored(Theme.Warning, FontAwesomeIcon.ExclamationCircle.ToIconString());
            }

            if (ImGui.IsItemHovered())
            {
                UiHelpers.WrappedTooltip(
                    "Not confirmed against an FFLogs report yet, so it isn't on the board.\n\n" +
                    $"{Ordinal(entry.Rank)} is where this would place once it is.");
            }
        }

        Theme.EndCard();
    }

    /// The rank and title of a top-three finish, struck in its metal.
    private void DrawFightMedal(LeaderboardProfileEntry entry, bool podium)
    {
        var start = ImGui.GetCursorPosY();
        var reserved = ImGui.GetFrameHeight();

        if (podium)
        {
            var medal = MedalFor(entry.Rank);

            DrawJobMedal(entry.JobId, medal);
            ImGui.SameLine(0f, PodiumIconGap);

            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(medal, PodiumTitles.For(entry.JobId, entry.Rank));
        }

        ImGui.SetCursorPosY(start + reserved);
    }

    /// Fight, score and rank, plus a medal row when anything on the page earned one.
    private float FightCardHeight(bool withMedalRow)
    {
        float numeric;
        using (plugin.Fonts.Numeric.PushSafe())
            numeric = ImGui.GetTextLineHeight();

        var body = ImGui.GetTextLineHeight();
        var spacing = ImGui.GetStyle().ItemSpacing.Y;

        var medal = withMedalRow ? ImGui.GetFrameHeight() + spacing : 0f;

        return (body * 2f) + numeric + medal + (spacing * 2f) + (ProfileCardPad * 2f);
    }

    /// The character the player is logged in as, which is whose profile this is by default.
    private static (string Character, string World)? LocalCharacter()
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player is null)
            return null;

        var world = player.HomeWorld.ValueNullable?.Name.ExtractText();

        return string.IsNullOrEmpty(world)
            ? null
            : (player.Name.TextValue, world);
    }

    /// Opens somebody's profile from a board row, remembering the way back.
    private void OpenProfile(string character, string world)
    {
        profileTarget = (character, world);
        profileFromBoard = true;

        mainTabs.SetImmediate(ProfileTabIndex);
    }

    private void ReturnToBoard()
    {
        profileTarget = null;
        profileFromBoard = false;

        mainTabs.SetImmediate(LeaderboardsTabIndex);
    }

    private void LoadProfile(string character, string world, string key)
    {
        profileLoading = true;
        profileError = null;
        profileLoadedFor = key;

        var ownerKey = plugin.OwnerKey();

        _ = Task.Run(async () =>
        {
            try
            {
                profile = await Game.LeaderboardClient
                    .GetProfileAsync(character, world, ownerKey, CancellationToken.None)
                    .ConfigureAwait(false);

                profileError = null;
            }
            catch (Exception ex)
            {
                profile = null;
                profileError = ex.Message;
            }
            finally
            {
                profileLoading = false;
            }
        });
    }
}
