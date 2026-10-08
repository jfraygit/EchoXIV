using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoMix.Plugin.UI.Controls.V2;
using EchoMix.Plugin.UI.Design;
using EchoMix.Shared;

namespace EchoMix.Plugin.UI.Screens;

/// Everything about being live, as a two-column master/detail.
public sealed class BroadcastScreen
{
    private enum Category
    {
        Live,
        Listing,
        Requests,
    }

    private static readonly CategoryList.Item[] Categories =
    {
        new(nameof(Category.Live), "Live", FontAwesomeIcon.BroadcastTower, "Go live, see the room"),
        new(nameof(Category.Listing), "Public Listing", FontAwesomeIcon.Globe, "Show name, art, venue"),
        new(nameof(Category.Requests), "Requests", FontAwesomeIcon.Music, "Who can request songs"),
    };

    private readonly Plugin plugin;
    private readonly VenueForm venue;
    private readonly SongRequestInbox requestInbox;
    private readonly CategoryList categoryList = new();
    private Category selected = Category.Live;

    public BroadcastScreen(Plugin plugin)
    {
        this.plugin = plugin;
        venue = new VenueForm(plugin);
        requestInbox = new SongRequestInbox(plugin);
    }

    private Configuration Config => plugin.Configuration;

    /// The shared form buffers, reached directly.
    private State.EchoMixEditState Edit => plugin.EditState;

    private static readonly string[] AccessModes = { "Anyone", "Whitelist", "Blacklist" };

    public void Draw()
    {
        var avail = ImGui.GetContentRegionAvail();
        var listWidth = CategoryList.DefaultWidth;
        var origin = ImGui.GetCursorScreenPos();

        var picked = categoryList.Draw("##broadcastCat", origin, new Vector2(listWidth, avail.Y),
            Categories, selected.ToString());
        if (Enum.TryParse<Category>(picked, out var next))
            selected = next;

        ImGui.SetCursorScreenPos(origin + new Vector2(listWidth + Metrics.Xxl, 0f));
        var detailWidth = MathF.Max(1f, avail.X - listWidth - Metrics.Xxl);

        Surfaces.ReserveScrollbar = true;

        ImGui.BeginChild("##broadcastDetail", new Vector2(detailWidth, avail.Y), false);
        switch (selected)
        {
            case Category.Listing:
                DrawListingDetail();
                break;
            case Category.Requests:
                DrawSongRequests();
                break;
            default:
                DrawLiveDetail();
                break;
        }
        ImGui.EndChild();

        Surfaces.ReserveScrollbar = false;
    }


    private void DrawLiveDetail()
    {
        var broadcast = plugin.AudioHostClient.LatestStatus.Broadcast;
        var characterName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty;

        DrawGoLive(broadcast, characterName);
        Surfaces.Gap(Metrics.Xxl);
        DrawRoom(broadcast);
    }


    private void DrawListingDetail()
    {
        var broadcast = plugin.AudioHostClient.LatestStatus.Broadcast;

        DrawListing(broadcast);

        if (!Config.IsPubliclyListed)
            return;

        Surfaces.Gap(Metrics.Xxl);
        Surfaces.SectionHeader("Show Artwork");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();
        DrawArtwork(broadcast);
        Surfaces.EndPanel();

        Surfaces.Gap(Metrics.Xxl);
        Surfaces.SectionHeader("Venue");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();
        venue.Draw(Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty);
        Surfaces.EndPanel();
    }

    private void DrawGoLive(BroadcastStatusMessage broadcast, string characterName)
    {
        var client = plugin.AudioHostClient;

        Surfaces.SectionHeader(broadcast.IsLive ? "On Air" : "Go Live");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        if (broadcast.IsLive)
        {
            DrawLiveSummary(broadcast);
            Surfaces.EndPanel();
            return;
        }

        if (broadcast.IsHostReconnecting)
        {
            using (TypeScale.Body())
                ImGui.TextColored(Semantic.Warning, $"Reconnecting to room {broadcast.RoomCode}...");

            Surfaces.Gap(Metrics.Lg);
            if (Fields.Button("Give Up and Stop", Fields.ButtonStyle.Danger, icon: FontAwesomeIcon.Stop))
                client.Send(MessageType.StopBroadcast, new object());

            Surfaces.EndPanel();
            return;
        }

        var listed = Config.IsPubliclyListed;

        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary, "Saved for next time once you go live.");

        Surfaces.Gap(Metrics.Lg);

        TextField("##v2HostPassword", "Co-Host Password", ref Edit.BroadcastHostPasswordBuffer, 48,
            "Needed even if you're DJing alone. Another DJ enters this to join you on the decks.",
            required: true, password: true, hint: "Set a password", divider: true);

        TextField("##v2RoomPassword", "Listener Password", ref Edit.BroadcastPasswordBuffer, 48,
            listed
                ? "Does nothing on a listed show - anyone who finds it in Browse can join."
                : "Leave it blank and the room code alone gets listeners in.",
            password: true,
            hint: listed ? "Not needed" : "Optional", divider: true);

        TextField("##v2DjName", "DJ Name", ref Edit.BroadcastDjNameBuffer, 48,
            "The name listeners see on your show.",
            hint: string.IsNullOrEmpty(characterName) ? "Your DJ name" : characterName,
            divider: true);

        TextField("##v2RoomCode", "Room Code", ref Edit.BroadcastRoomCodeBuffer, 24,
            "Leave blank and the relay assigns a short random one.", hint: "Random if blank",
            divider: false);

        var djName = Edit.BroadcastDjNameBuffer.Trim();
        var effectiveDjName = string.IsNullOrWhiteSpace(djName) ? characterName : djName;
        var needsAddress = Config.IsProximityAudio && listed;

        var blocker = string.IsNullOrWhiteSpace(Edit.BroadcastHostPasswordBuffer)
            ? "A co-host password is required, even if you're DJing alone."
            : needsAddress && !venue.IsComplete
                ? "Proximity Audio needs a full venue address before you can go live."
                : string.IsNullOrWhiteSpace(effectiveDjName)
                    ? "Enter a DJ name first."
                    : null;

        Fields.Divider();

        using (TypeScale.Caption())
        {
            using var _ = Sty.New().Col(ImGuiCol.Text, Semantic.TextTertiary);
            ImGui.TextWrapped(Config.IsProximityAudio
                ? $"Starts in Proximity Audio - listeners hear you positioned in the world, up to {Config.ProximityRange:0} yalms. Change it in the sidebar."
                : "Starts in Global Audio - every listener hears you at full volume. Change it in the sidebar.");
        }

        Surfaces.Gap(Metrics.Lg);

        if (Fields.Button("Go Live", Fields.ButtonStyle.Primary, enabled: blocker == null,
                icon: FontAwesomeIcon.BroadcastTower)
            && blocker == null)
        {
            StartBroadcast(effectiveDjName, characterName, listed);
        }

        if (blocker != null)
        {
            Surfaces.Gap(Metrics.Md);
            using (TypeScale.Caption())
                ImGui.TextColored(Semantic.Warning, blocker);
        }

        if (!string.IsNullOrEmpty(broadcast.BroadcastError))
        {
            Surfaces.Gap(Metrics.Md);
            using (TypeScale.Body())
                ImGui.TextColored(Semantic.Danger, broadcast.BroadcastError);
        }

        Surfaces.EndPanel();
    }

    private void StartBroadcast(string djName, string characterName, bool listed)
    {
        plugin.AudioHostClient.Send(MessageType.StartBroadcast, new StartBroadcastCommand
        {
            RoomCode = string.IsNullOrWhiteSpace(Edit.BroadcastRoomCodeBuffer) ? null : Edit.BroadcastRoomCodeBuffer.Trim(),
            Password = Edit.BroadcastPasswordBuffer,
            HostPassword = Edit.BroadcastHostPasswordBuffer,
            DjName = djName,
            CharacterName = characterName,
            IsProximityAudio = Config.IsProximityAudio,
            ProximityRange = Config.ProximityRange,
            IsPubliclyListed = listed,
            ShowName = Edit.PublicShowNameBuffer.Trim(),
            VenueName = Edit.VenueNameBuffer.Trim(),
            VenueDataCenter = Edit.VenueDataCenterBuffer.Trim(),
            VenueWorld = Edit.VenueWorldBuffer.Trim(),
            VenueHousingArea = Edit.VenueHousingAreaBuffer.Trim(),
            VenueWard = Edit.VenueWardBuffer.Trim(),
            VenuePlot = Edit.VenuePlotBuffer.Trim(),
            VenueIsApartment = Edit.VenueIsApartmentBuffer,
            VenueSubdivision = Edit.VenueSubdivisionBuffer,
        });

        Config.HostDisplayName = Edit.BroadcastDjNameBuffer.Trim();
        Config.LastVanityRoomCode = Edit.BroadcastRoomCodeBuffer.Trim();
        Config.LastShowName = Edit.PublicShowNameBuffer.Trim();
        Config.LastVenueName = Edit.VenueNameBuffer.Trim();
        Config.LastVenueDataCenter = Edit.VenueDataCenterBuffer.Trim();
        Config.LastVenueWorld = Edit.VenueWorldBuffer.Trim();
        Config.LastVenueHousingArea = Edit.VenueHousingAreaBuffer.Trim();
        Config.LastVenueWard = Edit.VenueWardBuffer.Trim();
        Config.LastVenuePlot = Edit.VenuePlotBuffer.Trim();
        Config.LastVenueIsApartment = Edit.VenueIsApartmentBuffer;
        Config.LastVenueSubdivision = Edit.VenueSubdivisionBuffer;
        Config.Save();
    }

    private void DrawLiveSummary(BroadcastStatusMessage broadcast)
    {
        var client = plugin.AudioHostClient;
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var rowHeight = ImGui.GetTextLineHeight();

        var pulse = 0.55f + (Motion.Pulse(0.6f) * 0.45f);
        drawList.AddCircleFilled(Chrome.Snap(new Vector2(pos.X + (5f * Metrics.Scale), pos.Y + (rowHeight * 0.5f))),
            5f * Metrics.Scale, ImGui.GetColorU32(Semantic.Alpha(Semantic.Live, pulse)));

        ImGui.Dummy(new Vector2(16f * Metrics.Scale, 0f));
        ImGui.SameLine();

        using (TypeScale.Heading())
            ImGui.TextColored(Semantic.Live, broadcast.IsLead ? "LIVE" : "ON THE DECKS");

        Surfaces.Gap(Metrics.Md);

        Detail("Room Code", broadcast.RoomCode ?? "-");
        Detail("Listeners", broadcast.ListenerCount.ToString());

        if (!broadcast.IsLead)
            Detail("Lead DJ", broadcast.HostDjName ?? "-");

        if (broadcast.LiveSinceUtc is { } since)
        {
            var elapsed = DateTime.UtcNow - since;
            Detail("On Air For", elapsed.TotalHours >= 1
                ? $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m"
                : $"{elapsed.Minutes}m {elapsed.Seconds}s");
        }

        Surfaces.Gap(Metrics.Lg);

        if (Fields.Button("Copy Room Code", icon: FontAwesomeIcon.Copy) && broadcast.RoomCode != null)
        {
            ImGui.SetClipboardText(Config.IsPubliclyListed
                ? $"{broadcast.RoomCode} (publicly listed - no password needed)"
                : $"{broadcast.RoomCode} (password protected - send them the password separately)");
        }

        ImGui.SameLine(0f, Metrics.Md);

        if (Fields.Button(broadcast.IsLead ? "End Show" : "Leave the Decks", Fields.ButtonStyle.Danger,
                icon: FontAwesomeIcon.Stop, idSuffix: "endOrLeave"))
        {
            client.Send(MessageType.StopBroadcast, new object());
        }

        Surfaces.Gap(Metrics.Md);
        Fields.Divider();

        var webListen = !string.IsNullOrEmpty(broadcast.WebListenUrl);
        if (Fields.Switch("##v2WebListen", "Web Listen Link", ref webListen,
                "A link anyone can open in a browser, no game or plugin needed."))
        {
            client.Send(MessageType.SetWebListenLink, new SetWebListenLinkCommand { Enabled = webListen });
        }

        if (string.IsNullOrEmpty(broadcast.WebListenUrl))
            return;

        Surfaces.Gap(Metrics.Sm);
        using (TypeScale.Caption())
        {
            using var _ = Sty.New().Col(ImGuiCol.Text, Semantic.DeckA);
            ImGui.TextWrapped(broadcast.WebListenUrl);
        }

        Surfaces.Gap(Metrics.Md);
        if (Fields.Button("Copy Link", icon: FontAwesomeIcon.Copy, idSuffix: "webListen"))
            ImGui.SetClipboardText(broadcast.WebListenUrl);

        if (!Config.IsProximityAudio)
            return;

        Surfaces.Gap(Metrics.Md);
        using (TypeScale.Caption())
        {
            using var _ = Sty.New().Col(ImGuiCol.Text, Semantic.Warning);
            ImGui.TextWrapped("Proximity Audio is on, but anyone with this link hears the mix regardless of distance.");
        }
    }

    /// Who's in the room - co-hosts and listeners.
    private void DrawRoom(BroadcastStatusMessage broadcast)
    {
        Surfaces.SectionHeader("Room");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        if (!broadcast.IsLive)
        {
            using (TypeScale.Caption())
                ImGui.TextColored(Semantic.TextTertiary, "Not live. Co-hosts and listeners appear here once your show starts.");
            Surfaces.EndPanel();
            return;
        }

        RosterGroup("On Stage", new List<string> { broadcast.HostDjName ?? "You" }, Semantic.DeckA);

        if (broadcast.HostRoster.Count > 1)
        {
            Surfaces.Gap(Metrics.Lg);
            DrawCoHosts(broadcast);
        }

        Surfaces.Gap(Metrics.Lg);

        var listeners = new List<string>();
        foreach (var listener in broadcast.ListenerRoster)
            listeners.Add(listener.CharacterName);

        RosterGroup($"Listeners ({broadcast.ListenerCount})", listeners, Semantic.Primary);

        Surfaces.EndPanel();
    }

    /// The co-hosts, each with Pass Host beside them while you hold lead.
    private void DrawCoHosts(BroadcastStatusMessage broadcast)
    {
        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary, "CO-HOSTS");

        Surfaces.Gap(Metrics.Sm);

        var width = Surfaces.ContentWidth;
        var drawList = ImGui.GetWindowDrawList();
        var rowHeight = MathF.Round(Metrics.ControlSm);
        var buttonWidth = MathF.Round(96f * Metrics.Scale);

        foreach (var host in broadcast.HostRoster)
        {
            if (host.HostId == broadcast.LeadHostId)
                continue;

            var name = string.IsNullOrWhiteSpace(host.DjName) ? host.CharacterName : host.DjName;
            var pos = Chrome.Snap(ImGui.GetCursorScreenPos());

            drawList.AddCircleFilled(
                Chrome.Snap(new Vector2(pos.X + (4f * Metrics.Scale), pos.Y + (rowHeight * 0.5f))),
                4f * Metrics.Scale, ImGui.GetColorU32(Semantic.DeckB));

            using (TypeScale.Body())
            {
                var shown = UiHelpers.TruncateToWidth(name, width - buttonWidth - (Metrics.Xxl * 2f));
                var size = ImGui.CalcTextSize(shown);
                Chrome.Text(drawList, Chrome.CenterY(pos.X + Metrics.Xl, pos.Y, rowHeight, size.Y),
                    ImGui.GetColorU32(Semantic.TextSecondary), shown);
            }

            if (broadcast.IsLead)
            {
                ImGui.SetCursorScreenPos(new Vector2(pos.X + width - buttonWidth, pos.Y));
                if (Fields.Button("Pass Host", Fields.ButtonStyle.Secondary, buttonWidth,
                        height: rowHeight, idSuffix: host.HostId.ToString()))
                {
                    plugin.AudioHostClient.Send(MessageType.PromoteHost, new PromoteHostCommand
                    {
                        TargetHostId = host.HostId,
                    });
                }

                Tip.Hovered("Pass Host", $"Hands the decks to {name}. Their audio goes out instead of yours, and you keep monitoring.");
            }

            ImGui.SetCursorScreenPos(new Vector2(pos.X, pos.Y + rowHeight + Metrics.Sm));
        }
    }

    private void RosterGroup(string title, List<string> names, Vector4 accent)
    {
        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary, title.ToUpperInvariant());

        Surfaces.Gap(Metrics.Sm);

        if (names.Count == 0)
        {
            using (TypeScale.Caption())
                ImGui.TextColored(Semantic.TextDisabled, "None yet.");
            return;
        }

        var drawList = ImGui.GetWindowDrawList();
        foreach (var name in names)
        {
            var pos = ImGui.GetCursorScreenPos();
            var rowHeight = ImGui.GetTextLineHeight();

            drawList.AddCircleFilled(
                Chrome.Snap(new Vector2(pos.X + (3f * Metrics.Scale), pos.Y + (rowHeight * 0.5f))),
                3f * Metrics.Scale, ImGui.GetColorU32(accent));

            ImGui.Dummy(new Vector2(12f * Metrics.Scale, 0f));
            ImGui.SameLine();

            using (TypeScale.Body())
                ImGui.TextColored(Semantic.TextSecondary, name);
        }
    }

    private void DrawListing(BroadcastStatusMessage broadcast)
    {
        Surfaces.SectionHeader("Public Listing");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        var listed = Config.IsPubliclyListed;
        if (Fields.Switch("##v2PublicListing", "List This Show Publicly", ref listed,
                "Anyone can find and join it from Browse - no room code, no password."))
        {
            Config.IsPubliclyListed = listed;
            Config.Save();

            plugin.AudioHostClient.Send(MessageType.SetPublicListing, new SetPublicListingCommand
            {
                IsPubliclyListed = listed,
            });
        }

        if (!listed)
        {
            Surfaces.Gap(Metrics.Md);
            Surfaces.RowText(
                "Off, so your show is private: listeners need the room code and the listener password. "
                + "Turn it on to add a show name, artwork and a venue address, and to appear in Browse.",
                Semantic.TextTertiary);

            Surfaces.EndPanel();
            return;
        }

        Fields.Divider();

        if (Fields.TextRow("##v2ShowName", "Show Name", ref Edit.PublicShowNameBuffer, 64,
                "What listeners see in Browse. Defaults to your DJ name.", hint: "Name your show"))
        {
            Config.LastShowName = Edit.PublicShowNameBuffer.Trim();
            Config.Save();

            if (broadcast.IsLive)
            {
                plugin.AudioHostClient.Send(MessageType.SetShowName, new SetShowNameCommand
                {
                    ShowName = Edit.PublicShowNameBuffer.Trim(),
                });
            }
        }

        Fields.Divider();
        Surfaces.Gap(Metrics.Sm);
        Surfaces.RowText(
            "Public listing is for real shows and venues. Harassment, offensive content or "
            + "impersonation will get your access to it revoked.",
            Semantic.Warning);

        Surfaces.EndPanel();
    }

    /// The show's card image in Browse.
    private void DrawArtwork(BroadcastStatusMessage broadcast)
    {
        var window = plugin.DjDeckWindow;
        var preview = window.ShowImagePreview;

        var previewWidth = MathF.Round(150f * Metrics.Scale);
        var previewHeight = MathF.Round(previewWidth * (ShowImageProcessor.TargetHeight / (float)ShowImageProcessor.TargetWidth));
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        if (preview != null)
        {
            ImGui.Image(preview.Handle, new Vector2(previewWidth, previewHeight));
            drawList.AddRect(Chrome.Snap(origin), Chrome.Snap(origin + new Vector2(previewWidth, previewHeight)),
                ImGui.GetColorU32(Elevation.LineStrong), Metrics.RadiusSoft, ImDrawFlags.None, Metrics.Hairline);
        }
        else
        {
            ImGui.Dummy(new Vector2(previewWidth, previewHeight));
            Elevation.DrawSurface(drawList, Chrome.Snap(origin),
                Chrome.Snap(origin + new Vector2(previewWidth, previewHeight)),
                Elevation.Sunken, Metrics.RadiusSoft, Elevation.ShadowSpec.None,
                topEdge: false, Elevation.Line);

            using (TypeScale.IconLarge())
                UiHelpers.DrawScaledIcon(drawList, FontAwesomeIcon.Image,
                    Chrome.Snap(origin + new Vector2(previewWidth * 0.5f, previewHeight * 0.5f)),
                    ImGui.GetColorU32(Semantic.TextDisabled));
        }

        ImGui.SameLine(0f, Metrics.Lg);
        ImGui.BeginGroup();

        using (TypeScale.Caption())
        {
            using var _ = Sty.New().Col(ImGuiCol.Text, Semantic.TextTertiary);
            ImGui.TextWrapped(broadcast.IsLive
                ? $"{ShowImageProcessor.TargetWidth}x{ShowImageProcessor.TargetHeight} works best. Anything else is cropped to fit."
                : $"{ShowImageProcessor.TargetWidth}x{ShowImageProcessor.TargetHeight} works best. Uploads the moment you go live.");
        }

        Surfaces.Gap(Metrics.Md);
        if (Fields.Button(preview == null ? "Choose Image" : "Replace", icon: FontAwesomeIcon.Upload))
            window.OpenShowImagePicker();

        ImGui.EndGroup();

        if (string.IsNullOrEmpty(window.ShowImageError))
            return;

        Surfaces.Gap(Metrics.Md);
        using (TypeScale.Caption())
        {
            using var _ = Sty.New().Col(ImGuiCol.Text, Semantic.Danger);
            ImGui.TextWrapped(window.ShowImageError!);
        }
    }

    /// Who may request a song while you're live.
    private void DrawSongRequests()
    {
        requestInbox.Draw();
        Surfaces.Gap(Metrics.Xxl);

        Surfaces.SectionHeader("Who Can Request");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        var mode = Config.SongRequestAccessMode;
        var modeIndex = (int)mode;

        var segmentWidth = Surfaces.BeginRowAligned();

        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary, "Who Can Request");

        Surfaces.Gap(Metrics.Sm);

        if (Fields.Segmented("##v2RequestAccess", ref modeIndex, AccessModes, segmentWidth))
        {
            Config.SongRequestAccessMode = (SongRequestAccessMode)modeIndex;
            Config.Save();
            PushAccessControl();
            mode = Config.SongRequestAccessMode;
        }

        Surfaces.EndRowAligned();

        if (mode == SongRequestAccessMode.Open)
        {
            Surfaces.Gap(Metrics.Sm);
            Surfaces.RowText("Any listener in the room can send you a request.", Semantic.TextTertiary);

            Surfaces.EndPanel();
            return;
        }

        var whitelist = mode == SongRequestAccessMode.Whitelist;

        Fields.Divider();
        Surfaces.Gap(Metrics.Sm);

        Surfaces.RowText(whitelist
            ? "Only these characters may request a song."
            : "Everyone except these characters may request a song.", Semantic.TextTertiary);

        Surfaces.Gap(Metrics.Md);

        var list = whitelist ? Config.SongRequestWhitelist : Config.SongRequestBlacklist;

        var listWidth = Surfaces.BeginRowAligned();
        var listChanged = Fields.NameList(whitelist ? "##v2Whitelist" : "##v2Blacklist", list,
            ref Edit.SongRequestNameBuffer, "Character name",
            whitelist ? "Nobody's allowed yet - add a name above." : "Nobody's blocked.",
            listWidth);
        Surfaces.EndRowAligned();

        if (listChanged)
        {
            Config.Save();
            PushAccessControl();
        }

        Surfaces.EndPanel();
    }

    private void PushAccessControl() =>
        plugin.AudioHostClient.Send(MessageType.SetSongRequestAccessControl, new SetSongRequestAccessControlCommand
        {
            Mode = Config.SongRequestAccessMode,
            Whitelist = Config.SongRequestWhitelist,
            Blacklist = Config.SongRequestBlacklist,
        });

    /// A form field laid out in a STACK - label, input, then wrapped helper text - rather than on the Fields
    /// row layout used for settings.
    private void TextField(
        string id,
        string label,
        ref string value,
        int maxLength,
        string? helper = null,
        bool required = false,
        bool password = false,
        string hint = "",
        bool divider = false)
    {
        var fullWidth = ImGui.GetContentRegionAvail().X;

        using (TypeScale.Body())
            ImGui.TextColored(Semantic.TextPrimary, label);

        ImGui.SameLine(0f, Metrics.Md);
        using (TypeScale.Caption())
        {
            ImGui.TextColored(
                required ? Semantic.Alpha(Semantic.Warning, 0.9f) : Semantic.TextDisabled,
                required ? "Required" : "Optional");
        }

        Surfaces.Gap(Metrics.Xs);

        Fields.TextInput(id, ref value, maxLength, fullWidth - Metrics.Lg, hint, password);

        if (!string.IsNullOrEmpty(helper))
        {
            Surfaces.Gap(Metrics.Xs);
            using (TypeScale.Caption())
            {
                using var _ = Sty.New().Col(ImGuiCol.Text, Semantic.TextTertiary);
                ImGui.TextWrapped(helper);
            }
        }

        if (divider)
        {
            Surfaces.Gap(Metrics.Sm);
            Fields.Divider();
            return;
        }

        Surfaces.Gap(Metrics.Lg);
    }

    private void Detail(string label, string value)
    {
        using (TypeScale.Caption())
            ImGui.TextColored(Semantic.TextTertiary, label);

        using (TypeScale.Body())
            ImGui.TextColored(Semantic.TextPrimary, value);
    }

    /// Moves the category selection one place, for the Up/Down hotkeys while the nav rail is collapsed - see
    /// EchoMixShellWindow.StepRailSelection.
    public bool StepCategory(int delta)
    {
        if (CategoryList.StepKey(Categories, selected.ToString(), delta) is not { } key
            || !Enum.TryParse<Category>(key, out var next))
        {
            return false;
        }

        selected = next;
        return true;
    }

}
