using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoSim.Shared;
using EchoSim.Sim.Jobs;
using EchoSim.UI.Controls;

namespace EchoSim.UI;

/// The Leaderboards tab: who is playing each fight best, on your job.
public sealed partial class MainWindow
{
    /// Breathing room either side of a fight's short name on its picker button.
    private static float EncounterButtonPad => UiHelpers.S(12f);

    private LeaderboardBoard? board;
    private string? boardError;
    private bool boardLoading;
    private string boardLoadedFor = string.Empty;

    /// How many rows a board asks for.
    private const int BoardLimit = 20;

    private void DrawLeaderboardsTab()
    {
        ImGui.Spacing();

        var jobId = Config.SelectedJobId;
        var definition = JobRegistry.For(jobId);

        if (definition is null)
        {
            ImGui.TextColored(Theme.TextDim, "That job isn't simulated yet, so it has no board.");
            return;
        }

        if (Encounters.ByKey(Config.LeaderboardEncounter) is null)
        {
            Config.LeaderboardEncounter = Encounters.All[0].Key;
            plugin.SaveConfig();
        }

        DrawEncounterPicker();
        ImGui.Spacing();


        var key = $"{Config.LeaderboardEncounter}:{jobId}";
        if (key != boardLoadedFor && !boardLoading)
            LoadBoard(Config.LeaderboardEncounter, jobId, key);

        if (!Config.ShareToLeaderboards)
            DrawOptInPrompt();

        if (boardLoading && board is null)
        {
            ImGui.TextColored(Theme.TextDim, "Loading...");
            return;
        }

        if (boardError is { } error)
        {
            ImGui.TextColored(Theme.Warning, error);
            return;
        }

        if (board is { } shown)
            DrawBoard(shown, definition);
    }

    private void DrawBoard(LeaderboardBoard shown, JobDefinition definition)
    {
        if (shown.Top.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(Theme.TextDim,
                $"No {definition.Name} has posted a score on this fight yet. Yours would be first.");
            return;
        }

        DrawPodium(shown);
        ImGui.Spacing();
        DrawBoardList(shown);
    }


    /// The fight picker, grouped the way a player thinks about the content rather than alphabetically.
    private void DrawEncounterPicker()
    {
        var first = true;

        foreach (var encounter in Encounters.All)
        {
            if (!first)
                ImGui.SameLine(0f, 6f);

            first = false;

            var selected = Config.LeaderboardEncounter == encounter.Key;
            var size = new Vector2(
                ImGui.CalcTextSize(encounter.Short).X + (EncounterButtonPad * 2f),
                ImGui.GetFrameHeight());

            if (EchoButton.Draw($"##board_{encounter.Key}", encounter.Short, size,
                    selected: selected, tooltip: encounter.Name))
            {
                Config.LeaderboardEncounter = encounter.Key;
                plugin.SaveConfig();
            }
        }

    }


    /// Says what sharing would publish, shown only while it is off.
    private void DrawOptInPrompt()
    {
        ImGui.Spacing();

        var share = Config.ShareToLeaderboards;
        if (EchoToggle.Draw("##sharelb_inline", "Post my kills to these boards", ref share))
        {
            Config.ShareToLeaderboards = share;
            plugin.SaveConfig();
        }

        TextWrappedColored(Theme.TextDisabled,
            "Off. Nothing about you is sent anywhere. Switching it on publishes your character name " +
            "and world alongside a score, but only for fights you actually clear.");

        ImGui.Spacing();
        ImGui.Separator();
    }

    /// The first three, given room.
    private void DrawPodium(LeaderboardBoard shown)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var cardWidth = (width - (PodiumGap * 2f)) / 3f;

        var titleLines = 1;
        foreach (var entry in shown.Top.Take(3))
            titleLines = Math.Max(titleLines, TitleLines(entry, cardWidth));

        var extra = (titleLines - 1) * ImGui.GetTextLineHeight();

        int[] order = [1, 0, 2];

        for (var slot = 0; slot < order.Length; slot++)
        {
            var index = order[slot];
            if (index >= shown.Top.Count)
                continue;

            if (slot > 0)
                ImGui.SameLine(0f, PodiumGap);

            DrawPodiumCard(shown.Top[index], cardWidth, index == 0, extra);
        }
    }

    /// How many lines the rank-and-title row needs at this card width.
    private static int TitleLines(LeaderboardEntry entry, float cardWidth)
    {
        var available = cardWidth - (PodiumCardPad * 2f) - ImGui.GetFrameHeight() - PodiumIconGap;
        return ImGui.CalcTextSize(RankLine(entry)).X > available ? 2 : 1;
    }

    private static string RankLine(LeaderboardEntry entry)
        => $"{Ordinal(entry.Rank)} - {PodiumTitles.For(entry.JobId, entry.Rank)}";

    private static float PodiumGap => UiHelpers.S(10f);

    /// Matches the inner padding Theme.BeginCard applies, which it does not expose.
    private static float PodiumCardPad => UiHelpers.S(14f);

    private static float PodiumIconGap => UiHelpers.S(8f);

    private void DrawPodiumCard(LeaderboardEntry entry, float width, bool winner, float extraHeight)
    {
        var height = UiHelpers.S(winner ? 132f : 116f) + extraHeight;

        var medal = MedalFor(entry.Rank);

        Theme.BeginCard($"##podium{entry.Rank}", new Vector2(width, height), medal);

        DrawJobMedal(entry.JobId, medal);
        ImGui.SameLine(0f, PodiumIconGap);

        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, medal);
        ImGui.PushTextWrapPos(width - PodiumCardPad);
        ImGui.TextWrapped(RankLine(entry));
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();

        var band = ParseBands.For(entry.Score);
        var colour = new Vector4(band.R / 255f, band.G / 255f, band.B / 255f, 1f);

        using (plugin.Fonts.Numeric.PushSafe())
            ImGui.TextColored(colour, $"{entry.Score:F2}");

        DrawPlayerName(entry);
        ImGui.TextColored(Theme.TextDisabled, entry.World);

        Theme.EndCard();
    }

    /// The job's own icon, struck in the medal's metal.
    private static void DrawJobMedal(uint jobId, Vector4 medal)
    {
        var size = ImGui.GetFrameHeight();
        var icon = Game.GameData.Icon(JobIconBase + jobId);

        if (icon is null)
        {
            ImGui.Dummy(new Vector2(size, size));
            return;
        }

        ImGui.Image(icon.Handle, new Vector2(size, size), Vector2.Zero, Vector2.One, medal);
    }

    /// Base of the monochrome job-glyph range: this plus the ClassJob row id.
    private const uint JobIconBase = 62800;

    private static Vector4 MedalFor(int rank) => rank switch
    {
        1 => new Vector4(0.92f, 0.76f, 0.33f, 1f),
        2 => new Vector4(0.78f, 0.81f, 0.86f, 1f),
        _ => new Vector4(0.80f, 0.52f, 0.29f, 1f),
    };

    /// Ranks four and down, plus the caller's own row when it is not among them.
    private void DrawBoardList(LeaderboardBoard shown)
    {
        if (!ImGui.BeginTable("##boardlist", 4,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            return;
        }

        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 44f);
        ImGui.TableSetupColumn("Player");
        ImGui.TableSetupColumn("World");
        ImGui.TableSetupColumn("Score", ImGuiTableColumnFlags.WidthFixed, 78f);

        foreach (var entry in shown.Top.Skip(3))
            DrawBoardRow(entry);

        if (shown.You is { } you)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(Theme.TextDisabled, "...");

            DrawBoardRow(you);
        }

        ImGui.EndTable();

        ImGui.Spacing();
        ImGui.TextColored(Theme.TextDisabled,
            shown.TotalEntries == 1
                ? "1 ranked player on this board."
                : $"{shown.TotalEntries:N0} ranked players on this board.");
    }

    private void DrawBoardRow(LeaderboardEntry entry)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextColored(Theme.TextDim, $"{entry.Rank}");

        ImGui.TableNextColumn();
        DrawPlayerName(entry);

        ImGui.TableNextColumn();
        ImGui.TextColored(Theme.TextDisabled, entry.World);

        ImGui.TableNextColumn();

        var band = ParseBands.For(entry.Score);
        ImGui.TextColored(new Vector4(band.R / 255f, band.G / 255f, band.B / 255f, 1f), $"{entry.Score:F2}");

        if (!entry.Verified)
        {
            ImGui.SameLine(0f, 6f);
            DrawProvisionalMark(entry);
        }
    }

    /// A player's name, which is also the way into their profile.
    private void DrawPlayerName(LeaderboardEntry entry)
    {
        ImGui.TextColored(entry.IsYou ? Theme.Accent : Theme.Text, entry.Character);

        if (!ImGui.IsItemHovered())
            return;

        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var colour = ImGui.GetColorU32(entry.IsYou ? Theme.Accent : Theme.Text);

        ImGui.GetWindowDrawList().AddLine(
            new Vector2(min.X, max.Y), new Vector2(max.X, max.Y), colour, 1f);

        if (ImGui.IsItemClicked())
            OpenProfile(entry.Character, entry.World);
    }

    /// Marks a provisional score, and explains it only if asked.
    private void DrawProvisionalMark(LeaderboardEntry entry)
    {
        using (plugin.Fonts.Icon.PushSafe())
            ImGui.TextColored(Theme.Warning, FontAwesomeIcon.ExclamationCircle.ToIconString());

        if (!ImGui.IsItemHovered())
            return;

        UiHelpers.WrappedTooltip(
            $"Not ranked yet. The board only lists scores confirmed against an FFLogs report, and " +
            $"yours hasn't been matched to one.\n\n" +
            $"{Ordinal(entry.Rank)} is where this {entry.Score:F2} would place you once it is.\n\n" +
            "Upload the fight to FFLogs and it'll be found and ranked on its own - there's nothing " +
            "else for you to do.");
    }

    private void LoadBoard(string encounter, uint jobId, string key)
    {
        boardLoading = true;
        boardError = null;
        boardLoadedFor = key;

        var ownerKey = plugin.OwnerKey();

        _ = Task.Run(async () =>
        {
            try
            {
                var loaded = await Game.LeaderboardClient
                    .GetBoardAsync(encounter, jobId, BoardLimit, ownerKey, CancellationToken.None)
                    .ConfigureAwait(false);

                board = loaded;
                boardError = null;
            }
            catch (Exception ex)
            {
                board = null;
                boardError = ex.Message;
            }
            finally
            {
                boardLoading = false;
            }
        });
    }
}
