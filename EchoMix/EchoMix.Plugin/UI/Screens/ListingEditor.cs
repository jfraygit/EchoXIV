using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures.TextureWraps;
using EchoMix.Plugin.UI.Controls.V2;
using EchoMix.Plugin.UI.Cosmetics;
using EchoMix.Plugin.UI.Design;
using EchoMix.Shared;

namespace EchoMix.Plugin.UI.Screens;

/// Your own DJ listing: the form that produces what everyone else sees in Browse.
public sealed class ListingEditor
{
    private readonly Plugin plugin;

    /// Set once the relay has been asked for this character's own detail, so the request doesn't repeat every
    /// frame - each hop opens a fresh TCP+TLS connection against a 60-per-minute-per-IP limit.
    private string? detailRequestedForId;

    /// The whole state machine, in one field.
    private string? seededFor;

    /// The form's contents as one string, captured at seed and after each save.
    private string savedSignature = string.Empty;

    private bool requestedProfiles;

    private string redeemCodeBuffer = string.Empty;

    public ListingEditor(Plugin plugin) => this.plugin = plugin;

    private State.EchoMixEditState Edit => plugin.EditState;

    private DjDeckWindow Window => plugin.DjDeckWindow;

    private static string CharacterName => Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty;


    /// Loads an existing profile into the form from outside - the profile view's own Edit button, which
    /// already has the full detail in hand.
    public void OpenFor(DjProfileDetailDto profile)
    {
        Window.SeedDjListingEditorFrom(profile);
        seededFor = profile.Id;
        detailRequestedForId = profile.Id;
        savedSignature = Signature();
    }

    /// Called when Browse moves off this category.
    public void OnLeaving()
    {
        if (seededFor == null || string.IsNullOrWhiteSpace(Edit.DjEditDjNameBuffer))
            return;

        if (Signature() == savedSignature)
            return;

        Save();
    }


    public void Draw(bool isSample)
    {
        if (isSample)
        {
            Surfaces.SectionHeader("Your Listing");
            Surfaces.Gap(Metrics.Md);
            Surfaces.BeginPanel();
            Surfaces.RowText(
                "Turn off Sample Browse Data in Settings > Appearance to edit your real listing - "
                + "the sample list isn't yours to change.",
                Semantic.Warning);
            Surfaces.EndPanel();
            return;
        }

        var snapshot = plugin.AudioHostClient.LatestDjProfiles;
        Sync(snapshot);

        if (seededFor == null)
        {
            Surfaces.SectionHeader("Your Listing");
            Surfaces.Gap(Metrics.Md);

            if (!string.IsNullOrEmpty(snapshot?.Error))
            {
                DrawNotice(FontAwesomeIcon.ExclamationTriangle, $"Couldn't reach the relay: {snapshot!.Error}");
            }
            else if (snapshot == null || OwnSummary(snapshot) != null)
            {
                DrawNotice(FontAwesomeIcon.IdBadge, "Loading your listing...");
            }
            else
            {
                DrawEmptyState();
            }

            return;
        }

        DrawActionBar();

        var avail = ImGui.GetContentRegionAvail();
        if (avail.Y <= 1f)
            return;

        ImGui.BeginChild("##listingForm", new Vector2(0f, avail.Y), false);

        DrawAvailability();
        Surfaces.Gap(Metrics.Xxl);
        DrawIdentity();
        Surfaces.Gap(Metrics.Xxl);
        DrawImages();
        Surfaces.Gap(Metrics.Xxl);
        DrawAppearance();
        Surfaces.Gap(Metrics.Xxl);
        DrawGenres();
        Surfaces.Gap(Metrics.Xxl);
        DrawVenues();

        if (Window.EditingDjListingId != null)
        {
            Surfaces.Gap(Metrics.Xxl);
            DrawLinkedCharacters();
        }

        Surfaces.Gap(Metrics.Xxl);
        ImGui.EndChild();
    }

    /// Keeps the buffers and the relay's idea of this character's listing in step.
    private void Sync(DjProfilesSnapshotMessage? snapshot)
    {
        if (!requestedProfiles)
        {
            requestedProfiles = true;
            Window.RefreshDjProfiles(CharacterName);
        }

        if (snapshot == null)
            return;

        var own = OwnSummary(snapshot);

        if (own == null)
        {
            if (seededFor is { Length: > 0 })
            {
                seededFor = null;
                detailRequestedForId = null;
                savedSignature = string.Empty;
            }

            return;
        }

        if (seededFor == own.Id)
            return;

        if (seededFor == string.Empty && Window.EditingDjListingId == own.Id)
        {
            seededFor = own.Id;
            detailRequestedForId = own.Id;
            savedSignature = Signature();
            return;
        }

        var detail = plugin.AudioHostClient.LatestDjProfileDetail?.Profile;
        if (detail != null && string.Equals(detail.Id, own.Id, StringComparison.Ordinal))
        {
            Window.SeedDjListingEditorFrom(detail);
            seededFor = own.Id;
            savedSignature = Signature();
            return;
        }

        seededFor = null;

        if (detailRequestedForId == own.Id)
            return;

        detailRequestedForId = own.Id;
        Window.RequestDjProfileDetail(own.Id, CharacterName);
    }

    private static DjProfileSummaryDto? OwnSummary(DjProfilesSnapshotMessage snapshot)
    {
        foreach (var profile in snapshot.Profiles)
        {
            if (profile.IsOwnProfile)
                return profile;
        }

        return null;
    }


    private void DrawEmptyState()
    {
        Surfaces.BeginPanel();

        Surfaces.RowText(
            "A listing puts you on the DJ list with a name, an avatar, your genres and when you "
            + "usually play - so listeners can find you between shows, not only while you're live.",
            Semantic.TextSecondary);

        Surfaces.Gap(Metrics.Lg);

        if (Fields.Button("Create a Listing", Fields.ButtonStyle.Primary, icon: FontAwesomeIcon.Plus))
        {
            Window.BeginNewDjListing();
            seededFor = string.Empty;
            savedSignature = Signature();
        }

        Fields.Divider();
        Surfaces.Gap(Metrics.Sm);

        using (TypeScale.Body())
            ImGui.TextColored(Semantic.TextPrimary, "Already Have One?");

        Surfaces.Gap(Metrics.Xs);
        Surfaces.RowText(
            "If another of your characters has a listing, generate a link code there and enter it "
            + "here. Both characters then share the one listing.",
            Semantic.TextTertiary);

        Surfaces.Gap(Metrics.Md);

        var codeWidth = MathF.Round(140f * Metrics.Scale);
        Fields.TextInput("##v2RedeemCode", ref redeemCodeBuffer, 6, codeWidth, "e.g. AB12CD");

        ImGui.SameLine(0f, Metrics.Md);
        var canRedeem = !string.IsNullOrWhiteSpace(redeemCodeBuffer) && !Window.IsRedeemingDjLinkCode;
        if (Fields.Button(Window.IsRedeemingDjLinkCode ? "Linking..." : "Link",
                Fields.ButtonStyle.Secondary, enabled: canRedeem,
                height: MathF.Round(ImGui.GetTextLineHeight() + (Metrics.Md * 2f)), idSuffix: "redeem")
            && canRedeem)
        {
            Window.RedeemDjLinkCode(redeemCodeBuffer);
        }

        if (Window.DjLinkRedeemResult is { } result)
        {
            Surfaces.Gap(Metrics.Md);

            if (result.Success)
            {
                Surfaces.RowText($"Linked to \"{result.DjName}\". It'll appear here in a moment.", Semantic.Success);
            }
            else
            {
                Surfaces.RowText(result.Error ?? "Couldn't link that code.", Semantic.Danger);
            }
        }

        Surfaces.EndPanel();
    }


    /// Title, save status and the Save button, above the scroll region.
    private void DrawActionBar()
    {
        var width = Surfaces.AutoWidth();
        var buttonWidth = MathF.Round(132f * Metrics.Scale);
        var buttonHeight = MathF.Round(Metrics.ControlMd);
        var origin = Chrome.Snap(ImGui.GetCursorScreenPos());
        var drawList = ImGui.GetWindowDrawList();

        var title = Window.EditingDjListingId == null ? "New Listing" : "Your Listing";
        var titleSize = TypeScale.Measure(TypeScale.Heading, title);

        using (TypeScale.Heading())
            Chrome.Text(drawList, Chrome.CenterY(origin.X, origin.Y, buttonHeight, titleSize.Y),
                ImGui.GetColorU32(Semantic.TextPrimary), title);

        var (status, statusColor) = SaveStatus();
        float statusWidth;
        using (TypeScale.Caption())
            statusWidth = ImGui.CalcTextSize(status).X;

        var statusX = origin.X + width - buttonWidth - Metrics.Lg - statusWidth;
        using (TypeScale.Caption())
            Chrome.Text(drawList,
                Chrome.CenterY(statusX, origin.Y, buttonHeight, ImGui.GetTextLineHeight()),
                ImGui.GetColorU32(statusColor), status);

        var hasName = !string.IsNullOrWhiteSpace(Edit.DjEditDjNameBuffer);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + width - buttonWidth, origin.Y));
        if (Fields.Button(Window.IsSavingDjListing ? "Saving..." : "Save Listing", Fields.ButtonStyle.Primary,
                buttonWidth, enabled: hasName && !Window.IsSavingDjListing, height: buttonHeight,
                idSuffix: "listing"))
        {
            Save();
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + buttonHeight + Metrics.Md));

        drawList.AddLine(
            Chrome.Snap(new Vector2(origin.X, origin.Y + buttonHeight + (Metrics.Md * 0.5f))),
            Chrome.Snap(new Vector2(origin.X + width, origin.Y + buttonHeight + (Metrics.Md * 0.5f))),
            ImGui.GetColorU32(Elevation.Line), Metrics.Hairline);

        Surfaces.Gap(Metrics.Lg);
    }

    private (string Text, Vector4 Color) SaveStatus()
    {
        if (Window.IsSavingDjListing)
            return (string.Empty, Semantic.TextTertiary);

        if (string.IsNullOrWhiteSpace(Edit.DjEditDjNameBuffer))
            return ("A DJ name is required", Semantic.Warning);

        if (Signature() != savedSignature)
            return ("Unsaved changes", Semantic.Warning);

        if (Window.DjListingSaveResult is { } result)
        {
            return result.Success
                ? ("Saved", Semantic.Success)
                : (UiHelpers.TruncateToWidth(result.Error ?? "Couldn't save", 240f * Metrics.Scale), Semantic.Danger);
        }

        return ("Up to date", Semantic.TextTertiary);
    }

    private void Save()
    {
        Window.SaveDjListing();

        savedSignature = Signature();
    }


    private void DrawIdentity()
    {
        Surfaces.SectionHeader("Identity");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        var fieldWidth = Surfaces.ContentWidth - Metrics.Lg;

        Field("DJ Name", required: true);
        Fields.TextInput("##v2DjName", ref Edit.DjEditDjNameBuffer, 32, fieldWidth, "What listeners see");
        Surfaces.Gap(Metrics.Sm);
        Fields.Divider();

        Field("Bio");
        Surfaces.RowText("A couple of lines on what you play. Shown on your profile.", Semantic.TextTertiary);
        Surfaces.Gap(Metrics.Xs);

        var boxHeight = MathF.Round(84f * Metrics.Scale);
        Fields.Multiline("##v2DjBio", ref Edit.DjEditBioBuffer, 300, fieldWidth, boxHeight,
            out var wrapWidth, Window.ConsumeDjEditBioWrapPending());

        Window.DjEditBioWrapWidth = wrapWidth;

        Surfaces.EndPanel();
    }

    private void DrawImages()
    {
        Surfaces.SectionHeader("Images");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        DrawAvatarSlot();
        Fields.Divider();
        DrawBannerSlot();

        if (Window.DjEditImageError is { Length: > 0 } error)
        {
            Surfaces.Gap(Metrics.Md);
            using (TypeScale.Caption())
            {
                using var _ = Sty.New().Col(ImGuiCol.Text, Semantic.Danger);
                ImGui.TextWrapped(error);
            }
        }

        Surfaces.Gap(Metrics.Md);
        Surfaces.RowText("Images upload when you save. Anything else is cropped to fit.", Semantic.TextTertiary);

        Surfaces.EndPanel();
    }

    /// Square preview on the left, explanation and button beside it.
    private void DrawAvatarSlot()
    {
        var preview = Window.DjEditAvatarPreview;
        var box = MathF.Round(116f * Metrics.Scale);
        var origin = ImGui.GetCursorScreenPos();

        DrawImageWell(preview, origin, new Vector2(box, box));
        ImGui.Dummy(new Vector2(box, box));

        ImGui.SameLine(0f, Metrics.Lg);
        ImGui.BeginGroup();

        using (TypeScale.Body())
            ImGui.TextColored(Semantic.TextPrimary, "Avatar");

        Surfaces.Gap(Metrics.Xs);

        using (TypeScale.Caption())
        {
            using var _ = Sty.New().Col(ImGuiCol.Text, Semantic.TextTertiary);
            ImGui.TextWrapped("Square. Shown everywhere your name appears. "
                + $"{ShowImageProcessor.DjAvatarSize}x{ShowImageProcessor.DjAvatarSize} works best.");
        }

        Surfaces.Gap(Metrics.Md);
        if (Fields.Button(preview == null ? "Choose" : "Replace", icon: FontAwesomeIcon.Upload,
                height: MathF.Round(Metrics.ControlSm), idSuffix: "avatar"))
        {
            Window.OpenDjEditAvatarPicker();
        }

        ImGui.EndGroup();
    }

    /// Label and button on one row, then the banner across the panel's full width.
    private void DrawBannerSlot()
    {
        var preview = Window.DjEditBannerPreview;
        var width = Surfaces.ContentWidth;
        var buttonWidth = MathF.Round(104f * Metrics.Scale);
        var rowHeight = MathF.Round(Metrics.ControlSm);
        var origin = Chrome.Snap(ImGui.GetCursorScreenPos());
        var drawList = ImGui.GetWindowDrawList();

        using (TypeScale.Body())
            Chrome.Text(drawList,
                Chrome.CenterY(origin.X, origin.Y, rowHeight, ImGui.GetTextLineHeight()),
                ImGui.GetColorU32(Semantic.TextPrimary), "Banner");

        float labelWidth;
        using (TypeScale.Body())
            labelWidth = ImGui.CalcTextSize("Banner").X;

        var helperLeft = origin.X + labelWidth + Metrics.Lg;
        var helperRoom = MathF.Max(0f, width - buttonWidth - Metrics.Lg - (helperLeft - origin.X));
        using (TypeScale.Caption())
            Chrome.Text(drawList,
                Chrome.CenterY(helperLeft, origin.Y, rowHeight, ImGui.GetTextLineHeight()),
                ImGui.GetColorU32(Semantic.TextTertiary),
                UiHelpers.TruncateToWidth(
                    $"Runs across the top of your profile. {ShowImageProcessor.DjBannerWidth}x{ShowImageProcessor.DjBannerHeight} works best.",
                    helperRoom));

        ImGui.SetCursorScreenPos(new Vector2(origin.X + width - buttonWidth, origin.Y));
        if (Fields.Button(preview == null ? "Choose" : "Replace", Fields.ButtonStyle.Secondary,
                buttonWidth, icon: FontAwesomeIcon.Upload, height: rowHeight, idSuffix: "banner"))
        {
            Window.OpenDjEditBannerPicker();
        }

        var stripTop = origin.Y + rowHeight + Metrics.Md;
        var stripHeight = MathF.Round(width * (ShowImageProcessor.DjBannerHeight / (float)ShowImageProcessor.DjBannerWidth));

        DrawImageWell(preview, new Vector2(origin.X, stripTop), new Vector2(width, stripHeight));

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, rowHeight + Metrics.Md + stripHeight));
    }

    /// The image, or an empty well in its place.
    private static void DrawImageWell(IDalamudTextureWrap? preview, Vector2 pos, Vector2 size)
    {
        var drawList = ImGui.GetWindowDrawList();
        var min = Chrome.Snap(pos);
        var max = Chrome.Snap(pos + size);

        if (preview != null)
        {
            drawList.AddImageRounded(preview.Handle, min, max, Vector2.Zero, Vector2.One,
                ImGui.GetColorU32(Vector4.One), Metrics.RadiusSoft);
            drawList.AddRect(min, max, ImGui.GetColorU32(Elevation.LineStrong),
                Metrics.RadiusSoft, ImDrawFlags.None, Metrics.Hairline);
            return;
        }

        Elevation.DrawSurface(drawList, min, max, Elevation.Sunken, Metrics.RadiusSoft,
            Elevation.ShadowSpec.None, topEdge: false, Elevation.Line);

        using (TypeScale.IconLarge())
            UiHelpers.DrawScaledIcon(drawList, FontAwesomeIcon.Image,
                Chrome.Snap(min + (size * 0.5f)),
                ImGui.GetColorU32(Semantic.TextDisabled),
                MathF.Min(28f * Metrics.Scale, size.Y * 0.45f));
    }

    /// The live preview, then the four controls that drive it.
    private void DrawAppearance()
    {
        Surfaces.SectionHeader("Appearance");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        DrawCosmeticPreview();

        Surfaces.Gap(Metrics.Lg);
        Fields.Divider();

        Fields.ColorRow("##v2FrameColor", "Frame Colour", ref Edit.DjEditFrameColor);
        StyleRow("##v2FrameStyle", "Frame Style", DjDeckWindow.DjFrameStyles, ref Edit.DjEditFrameStyle);
        Fields.ColorRow("##v2NameColor", "Name Colour", ref Edit.DjEditNameColor);
        StyleRow("##v2NameEffect", "Name Effect", DjDeckWindow.DjNameEffects, ref Edit.DjEditNameEffect);

        Surfaces.EndPanel();
    }

    /// Avatar with its frame, and the DJ name with its effect, on one band.
    private void DrawCosmeticPreview()
    {
        var width = Surfaces.ContentWidth;
        var avatar = MathF.Round(96f * Metrics.Scale);
        var bleed = MathF.Round(14f * Metrics.Scale);
        var height = avatar + (bleed * 2f);

        var origin = Chrome.Snap(ImGui.GetCursorScreenPos());
        var drawList = ImGui.GetWindowDrawList();

        Elevation.DrawSurface(drawList, origin, Chrome.Snap(origin + new Vector2(width, height)),
            Elevation.Sunken, Metrics.RadiusCard, Elevation.ShadowSpec.None, topEdge: false, Elevation.Line);

        var frameColor = Opaque(Edit.DjEditFrameColor);
        var avatarPos = Chrome.Snap(new Vector2(origin.X + Metrics.Xxl, origin.Y + bleed));
        var box = new Vector2(avatar, avatar);
        var texture = Window.DjEditAvatarPreview;

        if (texture != null)
        {
            drawList.AddImageRounded(texture.Handle, avatarPos, avatarPos + box, Vector2.Zero, Vector2.One,
                ImGui.GetColorU32(Vector4.One), Metrics.RadiusCard);
        }
        else
        {
            drawList.AddRectFilled(avatarPos, avatarPos + box, ImGui.GetColorU32(Elevation.Raised), Metrics.RadiusCard);

            using (TypeScale.IconLarge())
                UiHelpers.DrawScaledIcon(drawList, FontAwesomeIcon.User,
                    Chrome.Snap(avatarPos + (box * 0.5f)), ImGui.GetColorU32(Semantic.TextDisabled));
        }

        DjCosmetics.DrawAvatarFrame(drawList, avatarPos, box, Metrics.RadiusCard,
            frameColor, Edit.DjEditFrameStyle, Metrics.Scale);

        var nameLeft = avatarPos.X + avatar + Metrics.Xxl;
        var nameWidth = MathF.Max(Metrics.Xxxl, origin.X + width - nameLeft - Metrics.Lg);

        var nameColor = Opaque(Edit.DjEditNameColor);
        if (nameColor.X + nameColor.Y + nameColor.Z < 0.05f)
            nameColor = Semantic.TextPrimary;

        var shown = Edit.DjEditDjNameBuffer.Trim() is { Length: > 0 } typed ? typed : "Your DJ Name";

        ImGui.SetCursorScreenPos(new Vector2(nameLeft, origin.Y + (height * 0.5f) - ImGui.GetTextLineHeight()));
        DjCosmetics.DrawDjName(plugin.Fonts, UiHelpers.TruncateToWidth(shown, nameWidth),
            nameColor, Edit.DjEditNameEffect, 1.1f, Metrics.Scale);

        using (TypeScale.Caption())
            Chrome.Text(drawList,
                new Vector2(nameLeft, origin.Y + (height * 0.5f) + (Metrics.Sm * 2f)),
                ImGui.GetColorU32(Semantic.TextTertiary),
                UiHelpers.TruncateToWidth($"{Edit.DjEditFrameStyle} frame  ·  {Edit.DjEditNameEffect} name", nameWidth));

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
    }

    /// A style/effect picker.
    private static void StyleRow(string id, string label, IReadOnlyList<string> options, ref string value)
    {
        var index = 0;
        for (var i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i], value, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        if (Fields.Dropdown(id, label, ref index, options, controlWidth: MathF.Round(160f * Metrics.Scale)))
            value = options[index];
    }

    private void DrawGenres()
    {
        Surfaces.SectionHeader("Genres");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        Surfaces.RowText(
            "What you play. The DJ list and the live grid both filter on these, and show the first "
            + "three on your row.",
            Semantic.TextTertiary);

        Surfaces.Gap(Metrics.Md);

        Fields.TagChips("##v2DjGenres", Edit.DjEditGenres, ref Edit.DjEditGenreEntryBuffer,
            "Genre (e.g. House)", "No genres yet.", Surfaces.ContentWidth, Semantic.FixedA,
            DjDeckWindow.TitleCaseGenre);

        Surfaces.EndPanel();
    }

    /// Saved venues, plus the form that adds one.
    private void DrawVenues()
    {
        Surfaces.SectionHeader("Saved Venues");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        Surfaces.RowText(
            "Places you play. These become selectable on the Broadcast screen when you go live, so "
            + "you don't retype an address every show.",
            Semantic.TextTertiary);

        Surfaces.Gap(Metrics.Md);

        var width = Surfaces.ContentWidth;
        var venues = Edit.DjEditSavedVenues;

        if (venues.Count == 0)
        {
            using (TypeScale.Caption())
                ImGui.TextColored(Semantic.TextTertiary, "No saved venues yet.");
        }
        else
        {
            var drawList = ImGui.GetWindowDrawList();
            var rowHeight = MathF.Round(Metrics.ControlLg);
            var closeSize = MathF.Round(Metrics.ControlXs);
            int? removeAt = null;

            for (var i = 0; i < venues.Count; i++)
            {
                var venue = venues[i];
                var rowPos = Chrome.Snap(ImGui.GetCursorScreenPos());

                var hitWidth = MathF.Max(Metrics.Xxl, width - closeSize - (Metrics.Md * 2f));
                ImGui.InvisibleButton($"##venueRow{i}", new Vector2(hitWidth, rowHeight));

                if (ImGui.IsItemHovered())
                    drawList.AddRectFilled(rowPos, rowPos + new Vector2(width, rowHeight),
                        ImGui.GetColorU32(Semantic.Alpha(Semantic.TextPrimary, 0.04f)), Metrics.RadiusSoft);

                var textWidth = MathF.Max(Metrics.Xxl, width - closeSize - (Metrics.Lg * 2f));
                var lineHeight = ImGui.GetTextLineHeight();
                var block = (lineHeight * 2f) + Metrics.Xs;
                var y = rowPos.Y + ((rowHeight - block) * 0.5f);

                using (TypeScale.Body())
                    Chrome.Text(drawList, new Vector2(rowPos.X + Metrics.Md, y),
                        ImGui.GetColorU32(Semantic.TextPrimary),
                        UiHelpers.TruncateToWidth(venue.Name, textWidth));

                var address = VenueAddressFields.FormatAddress(venue.DataCenter, venue.World,
                    venue.HousingArea, venue.Ward, venue.Plot, venue.IsApartment, venue.Subdivision);

                using (TypeScale.Caption())
                    Chrome.Text(drawList, new Vector2(rowPos.X + Metrics.Md, y + lineHeight + Metrics.Xs),
                        ImGui.GetColorU32(Semantic.TextTertiary),
                        UiHelpers.TruncateToWidth(address, textWidth));

                var closeMin = Chrome.Snap(new Vector2(
                    rowPos.X + width - closeSize - Metrics.Md,
                    rowPos.Y + ((rowHeight - closeSize) * 0.5f)));

                ImGui.SetCursorScreenPos(closeMin);
                if (Fields.IconButton($"##venueDel{i}", FontAwesomeIcon.Trash, closeSize,
                        "Remove", $"Takes {venue.Name} off your listing.", danger: true))
                {
                    removeAt = i;
                }

                ImGui.SetCursorScreenPos(new Vector2(rowPos.X, rowPos.Y + rowHeight));
            }

            if (removeAt is { } index)
                venues.RemoveAt(index);
        }

        if (venues.Count >= DjDeckWindow.MaxDjSavedVenues)
        {
            Surfaces.Gap(Metrics.Md);
            using (TypeScale.Caption())
                ImGui.TextColored(Semantic.TextTertiary, $"Up to {DjDeckWindow.MaxDjSavedVenues} saved venues.");

            Surfaces.EndPanel();
            return;
        }

        Fields.Divider();
        Surfaces.Gap(Metrics.Sm);

        using (TypeScale.Body())
            ImGui.TextColored(Semantic.TextPrimary, "Add a Venue");

        Surfaces.Gap(Metrics.Sm);

        VenueAddressFields.Draw("##v2DjVenue", MathF.Round(170f * Metrics.Scale),
            ref Edit.DjEditVenueNameBuffer,
            ref Edit.DjEditVenueDataCenterBuffer,
            ref Edit.DjEditVenueWorldBuffer,
            ref Edit.DjEditVenueHousingAreaBuffer,
            ref Edit.DjEditVenueWardBuffer,
            ref Edit.DjEditVenuePlotBuffer,
            ref Edit.DjEditVenueIsApartment,
            ref Edit.DjEditVenueSubdivision,
            nameRequired: true);

        var canAdd = !string.IsNullOrWhiteSpace(Edit.DjEditVenueNameBuffer)
            && VenueAddressFields.IsAddressComplete(Edit.DjEditVenueDataCenterBuffer,
                Edit.DjEditVenueWorldBuffer, Edit.DjEditVenueHousingAreaBuffer,
                Edit.DjEditVenueWardBuffer, Edit.DjEditVenuePlotBuffer);

        Surfaces.Gap(Metrics.Md);

        if (Fields.Button("Add Venue", Fields.ButtonStyle.Secondary, enabled: canAdd,
                icon: FontAwesomeIcon.Plus, height: MathF.Round(Metrics.ControlSm)) && canAdd)
        {
            venues.Add(new SavedVenueDto
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = Edit.DjEditVenueNameBuffer.Trim(),
                DataCenter = Edit.DjEditVenueDataCenterBuffer,
                World = Edit.DjEditVenueWorldBuffer,
                HousingArea = Edit.DjEditVenueHousingAreaBuffer,
                Ward = Edit.DjEditVenueWardBuffer,
                Plot = Edit.DjEditVenuePlotBuffer,
                IsApartment = Edit.DjEditVenueIsApartment,
                Subdivision = Edit.DjEditVenueSubdivision,
            });

            ClearVenueEntry();
        }

        if (!canAdd)
        {
            Surfaces.Gap(Metrics.Sm);
            using (TypeScale.Caption())
                ImGui.TextColored(Semantic.TextTertiary, "A name and all five address fields are needed.");
        }

        Surfaces.EndPanel();
    }

    private void ClearVenueEntry()
    {
        Edit.DjEditVenueNameBuffer = string.Empty;
        Edit.DjEditVenueDataCenterBuffer = string.Empty;
        Edit.DjEditVenueWorldBuffer = string.Empty;
        Edit.DjEditVenueHousingAreaBuffer = string.Empty;
        Edit.DjEditVenueWardBuffer = string.Empty;
        Edit.DjEditVenuePlotBuffer = string.Empty;
        Edit.DjEditVenueIsApartment = false;
        Edit.DjEditVenueSubdivision = false;
    }

    /// One timezone, seven day cells, then a fixed time slot per day that's on.
    private void DrawAvailability()
    {
        Surfaces.SectionHeader("Usually On");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        Surfaces.RowText("When listeners can expect to find you. Click a day to turn it on.", Semantic.TextTertiary);
        Surfaces.Gap(Metrics.Md);

        var days = Edit.DjEditAvailability;
        var labels = DjDeckWindow.DjAvailabilityDays;
        var width = Surfaces.ContentWidth;
        var gap = Metrics.Sm;
        var cellWidth = (width - (gap * 6f)) / 7f;
        var cellHeight = MathF.Round(Metrics.ControlLg);
        var origin = Chrome.Snap(ImGui.GetCursorScreenPos());
        var drawList = ImGui.GetWindowDrawList();

        for (var i = 0; i < 7 && i < days.Count; i++)
        {
            var day = days[i];
            var pos = Chrome.Snap(new Vector2(origin.X + (i * (cellWidth + gap)), origin.Y));
            var box = new Vector2(cellWidth, cellHeight);

            ImGui.SetCursorScreenPos(pos);
            var clicked = ImGui.InvisibleButton($"##availDay{i}", box);
            var hovered = ImGui.IsItemHovered();

            if (hovered)
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            var on = day.IsAvailable;
            var fill = on
                ? Semantic.Alpha(Semantic.Primary, hovered ? 0.3f : 0.22f)
                : hovered ? Semantic.Alpha(Semantic.TextPrimary, 0.06f) : Elevation.Sunken;

            drawList.AddRectFilled(pos, pos + box, ImGui.GetColorU32(fill), Metrics.RadiusSoft);
            drawList.AddRect(pos, pos + box,
                ImGui.GetColorU32(on ? Semantic.Alpha(Semantic.Primary, 0.6f) : Elevation.Line),
                Metrics.RadiusSoft, ImDrawFlags.None, Metrics.Hairline);

            using (TypeScale.Body())
            {
                var size = ImGui.CalcTextSize(labels[i]);
                Chrome.Text(drawList,
                    Chrome.CenterY(pos.X + ((cellWidth - size.X) * 0.5f), pos.Y, cellHeight, size.Y),
                    ImGui.GetColorU32(on ? Semantic.TextPrimary : Semantic.TextTertiary), labels[i]);
            }

            if (clicked)
                day.IsAvailable = !day.IsAvailable;
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, cellHeight));

        var anyOn = false;
        for (var i = 0; i < 7 && i < days.Count; i++)
            anyOn |= days[i].IsAvailable;

        if (!anyOn)
        {
            Surfaces.EndPanel();
            return;
        }

        Surfaces.Gap(Metrics.Md);
        Fields.Divider();

        var zoneIndex = AvailabilitySlots.ZoneIndex(Edit.DjEditAvailabilityZone);
        if (Fields.Dropdown("##v2AvailZone", "Timezone", ref zoneIndex, AvailabilitySlots.Zones,
                "Shown once on your profile, not on every day.", MathF.Round(110f * Metrics.Scale)))
        {
            Edit.DjEditAvailabilityZone = AvailabilitySlots.Zones[zoneIndex];
            RestampAvailabilityZone();
        }

        for (var i = 0; i < 7 && i < days.Count; i++)
        {
            if (days[i].IsAvailable)
                DrawDayRange(days[i], labels[i], i);
        }

        Surfaces.EndPanel();
    }

    /// One day's start and end hour, as two dropdowns sharing a row's control slot.
    private void DrawDayRange(DjAvailabilityDayDto day, string label, int index)
    {
        var partWidth = MathF.Round(96f * Metrics.Scale);
        var dashWidth = Metrics.Xxl;

        var (start, end, _) = AvailabilitySlots.Parse(day.TimeNote);
        var hasRange = start >= 0;

        var row = Fields.BeginRow($"##availDayRow{index}", label, null,
            (partWidth * 2f) + dashWidth, rowClickable: false);

        var startOption = hasRange ? start + 1 : 0;

        ImGui.SetCursorScreenPos(row.ControlMin);
        if (Fields.DropdownInline($"##availStart{index}", ref startOption,
                AvailabilitySlots.StartOptions, partWidth, row.ControlHeight))
        {
            day.TimeNote = startOption <= 0
                ? string.Empty
                : AvailabilitySlots.Compose(
                    startOption - 1,
                    hasRange ? end : AvailabilitySlots.DefaultEndFor(startOption - 1),
                    Edit.DjEditAvailabilityZone);
        }

        using (TypeScale.Body())
        {
            var size = ImGui.CalcTextSize("to");
            Chrome.Text(ImGui.GetWindowDrawList(),
                Chrome.CenterY(
                    row.ControlMin.X + partWidth + ((dashWidth - size.X) * 0.5f),
                    row.ControlMin.Y, row.ControlHeight, size.Y),
                ImGui.GetColorU32(hasRange ? Semantic.TextTertiary : Semantic.TextDisabled), "to");
        }

        var endOption = hasRange ? end : -1;

        ImGui.BeginDisabled(!hasRange);
        ImGui.SetCursorScreenPos(new Vector2(row.ControlMin.X + partWidth + dashWidth, row.ControlMin.Y));
        if (Fields.DropdownInline($"##availEnd{index}", ref endOption,
                AvailabilitySlots.Hours, partWidth, row.ControlHeight, "End")
            && hasRange)
        {
            day.TimeNote = AvailabilitySlots.Compose(start, endOption, Edit.DjEditAvailabilityZone);
        }

        ImGui.EndDisabled();

        Fields.EndRow(row);
    }

    /// Rewrites every set day's note in the newly chosen zone.
    private void RestampAvailabilityZone()
    {
        foreach (var day in Edit.DjEditAvailability)
        {
            if (!day.IsAvailable)
                continue;

            var (start, end, _) = AvailabilitySlots.Parse(day.TimeNote);
            if (start >= 0)
                day.TimeNote = AvailabilitySlots.Compose(start, end, Edit.DjEditAvailabilityZone);
        }
    }

    /// Other characters sharing this one listing.
    private void DrawLinkedCharacters()
    {
        Surfaces.SectionHeader("Linked Characters");
        Surfaces.Gap(Metrics.Md);
        Surfaces.BeginPanel();

        Surfaces.RowText(
            "Other characters with full access to this listing. Their broadcasts, likes and follows "
            + "all count as this listing's.",
            Semantic.TextTertiary);

        Surfaces.Gap(Metrics.Md);

        var names = Edit.DjEditLinkedCharacterNames;
        var width = Surfaces.ContentWidth;

        if (names.Count == 0)
        {
            using (TypeScale.Caption())
                ImGui.TextColored(Semantic.TextTertiary, "None linked yet.");
        }
        else
        {
            var drawList = ImGui.GetWindowDrawList();
            var rowHeight = MathF.Round(Metrics.ControlSm);
            var buttonWidth = MathF.Round(84f * Metrics.Scale);

            foreach (var name in names.ToArray())
            {
                var rowPos = Chrome.Snap(ImGui.GetCursorScreenPos());
                var pending = Window.IsUnlinkPending(name);

                using (TypeScale.Body())
                {
                    var shown = UiHelpers.TruncateToWidth(name, width - buttonWidth - (Metrics.Lg * 2f));
                    var size = ImGui.CalcTextSize(shown);
                    Chrome.Text(drawList, Chrome.CenterY(rowPos.X + Metrics.Md, rowPos.Y, rowHeight, size.Y),
                        ImGui.GetColorU32(Semantic.TextSecondary), shown);
                }

                ImGui.SetCursorScreenPos(new Vector2(rowPos.X + width - buttonWidth, rowPos.Y));
                if (Fields.Button(pending ? "..." : "Unlink", Fields.ButtonStyle.Danger, buttonWidth,
                        enabled: !pending, height: rowHeight, idSuffix: name))
                {
                    Window.UnlinkDjProfileCharacter(name);
                }

                ImGui.SetCursorScreenPos(new Vector2(rowPos.X, rowPos.Y + rowHeight + Metrics.Sm));
            }
        }

        Surfaces.Gap(Metrics.Md);
        Fields.Switch("##v2ShowLinked", "Show On My Profile", ref Edit.DjEditShowLinkedCharacters,
            "Lists these names publicly as \"Also Known As\".");

        Fields.Divider();

        if (names.Count >= 5)
        {
            using (TypeScale.Caption())
                ImGui.TextColored(Semantic.TextTertiary, "Already at the 5-character link limit.");

            Surfaces.EndPanel();
            return;
        }

        if (Window.DjLinkCode is { } code)
        {
            var remaining = MathF.Max(0f, Window.DjLinkCodeExpiresInSeconds);
            var minutes = (int)remaining / 60;
            var seconds = (int)remaining % 60;

            using (TypeScale.Heading())
                ImGui.TextColored(Semantic.Primary, code);

            ImGui.SameLine(0f, Metrics.Lg);
            if (Fields.Button("Copy", Fields.ButtonStyle.Secondary, icon: FontAwesomeIcon.Copy,
                    height: MathF.Round(Metrics.ControlSm), idSuffix: "linkcode"))
            {
                ImGui.SetClipboardText(code);
            }

            Surfaces.Gap(Metrics.Sm);
            Surfaces.RowText(
                $"Expires in {minutes}:{seconds.ToString("D2", CultureInfo.InvariantCulture)}. Enter it from the "
                + "other character under Browse > Your Listing.",
                Semantic.TextTertiary);

            Surfaces.EndPanel();
            return;
        }

        if (Window.DjLinkCodeError is { Length: > 0 } error)
        {
            Surfaces.RowText(error, Semantic.Danger);
            Surfaces.Gap(Metrics.Md);
        }

        if (Fields.Button(Window.IsGeneratingDjLinkCode ? "Generating..." : "Generate Link Code",
                Fields.ButtonStyle.Secondary, icon: FontAwesomeIcon.Link,
                enabled: !Window.IsGeneratingDjLinkCode, height: MathF.Round(Metrics.ControlSm)))
        {
            Window.GenerateDjLinkCode();
        }

        Surfaces.EndPanel();
    }


    /// A field's label, with whether it's required beside it - the stacked form shape, so the explanation
    /// under it gets the panel's full width rather than a narrow column beside a control.
    private static void Field(string label, bool required = false)
    {
        using (TypeScale.Body())
            ImGui.TextColored(Semantic.TextPrimary, label);

        ImGui.SameLine(0f, Metrics.Md);
        using (TypeScale.Caption())
            ImGui.TextColored(
                required ? Semantic.Alpha(Semantic.Warning, 0.9f) : Semantic.TextDisabled,
                required ? "Required" : "Optional");

        Surfaces.Gap(Metrics.Xs);
    }

    private static Vector4 Opaque(Vector3 color) => new(color.X, color.Y, color.Z, 1f);

    /// Everything in the form, as one string.
    private string Signature()
    {
        var builder = new StringBuilder();
        builder.Append(Edit.DjEditDjNameBuffer).Append('\u001f');
        builder.Append(Edit.DjEditBioBuffer).Append('\u001f');
        builder.Append(Edit.DjEditFrameStyle).Append('\u001f');
        builder.Append(Edit.DjEditNameEffect).Append('\u001f');
        builder.Append(Edit.DjEditFrameColor.ToString()).Append('\u001f');
        builder.Append(Edit.DjEditNameColor.ToString()).Append('\u001f');
        builder.Append(Edit.DjEditShowLinkedCharacters).Append('\u001f');
        builder.Append(string.Join(",", Edit.DjEditGenres)).Append('\u001f');

        foreach (var venue in Edit.DjEditSavedVenues)
        {
            builder.Append(venue.Id).Append('|').Append(venue.Name).Append('|')
                .Append(venue.DataCenter).Append('|').Append(venue.World).Append('|')
                .Append(venue.HousingArea).Append('|').Append(venue.Ward).Append('|')
                .Append(venue.Plot).Append('|').Append(venue.IsApartment).Append('|')
                .Append(venue.Subdivision).Append(';');
        }

        builder.Append('\u001f');

        foreach (var day in Edit.DjEditAvailability)
            builder.Append(day.IsAvailable).Append('|').Append(day.TimeNote).Append(';');

        return builder.ToString();
    }

    private static void DrawNotice(FontAwesomeIcon icon, string message)
    {
        Surfaces.BeginPanel();

        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var lineHeight = ImGui.GetTextLineHeight();

        using (TypeScale.Icon())
            UiHelpers.DrawScaledIcon(drawList, icon,
                Chrome.Snap(new Vector2(pos.X + Metrics.Md, pos.Y + (lineHeight * 0.5f))),
                ImGui.GetColorU32(Semantic.TextTertiary));

        ImGui.Dummy(new Vector2(Metrics.Xxl, 0f));
        ImGui.SameLine();

        using (TypeScale.Body())
            ImGui.TextColored(Semantic.TextTertiary, message);

        Surfaces.EndPanel();
    }
}
