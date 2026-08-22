using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using EchoRoleplay.Game;
using EchoRoleplay.UI.Controls;

namespace EchoRoleplay.UI;

/// Everyone you have looked up, and what you made of them.
public sealed class ContactsTab
{
    private readonly Plugin plugin;

    private string search = string.Empty;

    /// Which group the list is filtered to.
    private Filter filter = Filter.Everyone;

    /// Set on the frame Forget is pressed, so the confirmation knows who it is asking about - and so the
    /// popup can be opened from OUTSIDE the row's id scope.
    private string? forgetting;

    private bool forgetRequested;

    public ContactsTab(Plugin plugin) => this.plugin = plugin;

    private ContactBook Book => plugin.Contacts;

    public void Draw()
    {
        var scale = UiHelpers.Scale;

        if (Book.ReadOnly)
        {
            ImGui.TextColored(Theme.Bad, "Your contacts file could not be read, so nothing will be saved.");
            ImGui.Dummy(new Vector2(0f, 8f * scale));
        }

        ImGui.Dummy(new Vector2(0f, 6f * scale));

        DrawRequests(scale);

        if (Book.Count == 0)
        {
            DrawEmptyState(scale);
            return;
        }

        DrawFilters(scale);

        if (plugin.Friends.Message.Length > 0)
        {
            ImGui.Dummy(new Vector2(0f, 6f * scale));

            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);
            ImGui.TextColored(plugin.Friends.Failed ? Theme.Warning : Theme.TextDim, plugin.Friends.Message);
            ImGui.PopTextWrapPos();

            ImGui.SameLine(0f, 8f * scale);

            if (EchoButton.BareIcon("##dismissfriendmsg", plugin.Fonts.Icon, FontAwesomeIcon.Times,
                    18f * scale, "Dismiss"))
            {
                plugin.Friends.ClearMessage();
            }
        }

        ImGui.Dummy(new Vector2(0f, 8f * scale));

        if (ImGui.BeginChild("##contactlist", ImGui.GetContentRegionAvail(), false))
            DrawList(scale);

        ImGui.EndChild();
    }

    private void DrawEmptyState(float scale)
    {
        Theme.BeginPanel("##nocontacts");

        ImGui.TextColored(Theme.Text, "Nobody Yet");
        ImGui.Dummy(new Vector2(0f, 4f * scale));

        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Theme.ContentWidth);
        ImGui.TextColored(Theme.TextDim,
            "Right-click somebody and choose Roleplay Profile to read them. Anyone you read is "
            + "remembered here, with room for your own notes.");
        ImGui.PopTextWrapPos();

        Theme.EndPanel();
    }

    /// People waiting for an answer, above everything else.
    private void DrawRequests(float scale)
    {
        var incoming = plugin.Friends.Incoming;

        if (incoming.Count == 0)
            return;

        Theme.BeginPanel("##friendrequests", Theme.Accent);

        ImGui.TextColored(Theme.Accent, incoming.Count == 1 ? "Friend Request" : $"Friend Requests  {incoming.Count}");
        ImGui.Dummy(new Vector2(0f, 6f * scale));

        foreach (var request in incoming.ToList())
        {
            ImGui.PushID(request.Id);

            var rowOrigin = ImGui.GetCursorScreenPos();
            var buttons = 22f * scale;
            var gap = 6f * scale;

            ImGui.BeginGroup();
            ImGui.TextColored(Theme.Text, request.Character);

            if (!string.IsNullOrEmpty(request.YourCharacter))
                ImGui.TextColored(Theme.TextDisabled, "asked " + request.YourCharacter);

            ImGui.EndGroup();

            ProfileFields.At(rowOrigin, Theme.ContentWidth - (buttons * 2f) - gap);

            if (EchoButton.BareIcon("##accept", plugin.Fonts.Icon, FontAwesomeIcon.Check, buttons,
                    "Accept", colourOverride: Theme.Good))
            {
                plugin.Friends.Answer(request.Id, accept: true);
            }

            ImGui.SameLine(0f, gap);

            if (EchoButton.BareIcon("##decline", plugin.Fonts.Icon, FontAwesomeIcon.Times, buttons,
                    "Decline. They are not told.", colourOverride: Theme.Bad))
            {
                plugin.Friends.Answer(request.Id, accept: false);
            }

            ImGui.PopID();
            ImGui.Dummy(new Vector2(0f, 4f * scale));
        }

        Theme.EndPanel();
        ImGui.Dummy(new Vector2(0f, 8f * scale));
    }

    /// The search box, and the groups as a row of chips.
    private void DrawFilters(float scale)
    {
        var width = MathF.Min(280f * scale, ImGui.GetContentRegionAvail().X);

        ImGui.SetNextItemWidth(width);
        ImGui.InputTextWithHint("##contactsearch", "Search", ref search, 64);

        ImGui.Dummy(new Vector2(0f, 6f * scale));

        var origin = ImGui.GetCursorScreenPos();
        var gap = 6f * scale;
        var available = ImGui.GetContentRegionAvail().X;
        var x = 0f;
        var y = 0f;
        var rowHeight = 0f;

        void Chip(string label, Filter value)
        {
            var width = ProfileFields.ChipWidth(label);

            if (x > 0f && x + width > available)
            {
                x = 0f;
                y += rowHeight + gap;
            }

            ImGui.SetCursorScreenPos(new Vector2(origin.X + x, origin.Y + y));

            if (ProfileFields.Chip($"##filter{value}", label, filter == value))
                filter = value;

            rowHeight = ImGui.GetItemRectSize().Y;
            x += width + gap;
        }

        Chip($"Everyone  {Book.Count}", Filter.Everyone);
        Chip($"Friends  {Book.Friends.Count()}", Filter.Friends);
        Chip($"Favourites  {Book.Favourites.Count()}", Filter.Favourites);
        Chip($"Blocked  {Book.Blocks.Count()}", Filter.Blocked);

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + y + rowHeight));
    }

    /// Which slice of the book is on screen.
    private enum Filter
    {
        Everyone,
        Friends,
        Favourites,
        Blocked,
    }

    private void DrawList(float scale)
    {
        var terms = search.Trim();
        var shown = 0;

        foreach (var contact in Book.All.ToList())
        {
            if (filter == Filter.Friends && !contact.Friend)
                continue;

            if (filter == Filter.Favourites && !contact.Favourite)
                continue;

            if (filter == Filter.Blocked && !contact.Blocked)
                continue;

            if (terms.Length > 0
                && !contact.Name.Contains(terms, StringComparison.OrdinalIgnoreCase)
                && !contact.CharacterKey.Contains(terms, StringComparison.OrdinalIgnoreCase)
                && !contact.Note.Contains(terms, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            shown++;

            ImGui.PushID(contact.CharacterKey);

            Theme.BeginPanel("##contact", contact.Blocked ? Theme.Bad : null);

            DrawRow(contact, scale);

            Theme.EndPanel();
            ImGui.PopID();

            ImGui.Dummy(new Vector2(0f, 8f * scale));
        }

        if (shown == 0)
            ImGui.TextColored(Theme.TextDim, "Nobody here.");

        if (forgetRequested)
        {
            ImGui.OpenPopup(ForgetPopup);
            forgetRequested = false;
        }

        DrawForgetConfirmation(scale);
    }

    private void DrawRow(Contact contact, float scale)
    {
        var buttons = 22f * scale;
        var gap = 6f * scale;
        var rowOrigin = ImGui.GetCursorScreenPos();
        var width = Theme.ContentWidth;

        var favourite = contact.Favourite;

        if (EchoButton.BareIcon("##favourite", plugin.Fonts.Icon,
                favourite ? FontAwesomeIcon.Star : FontAwesomeIcon.Star, buttons,
                favourite ? "Remove Favourite" : "Add Favourite",
                selected: favourite, colourOverride: favourite ? Theme.Warning : null))
        {
            Book.SetFavourite(contact.CharacterKey, !favourite);
        }

        ImGui.SameLine(0f, gap);

        var friend = contact.Friend;
        var asked = plugin.Friends.Outgoing.Any(o =>
            string.Equals(o.Character, contact.CharacterKey, StringComparison.Ordinal));

        if (EchoButton.BareIcon("##friend", plugin.Fonts.Icon,
                friend ? FontAwesomeIcon.UserCheck : asked ? FontAwesomeIcon.UserClock : FontAwesomeIcon.UserPlus,
                buttons,
                friend ? "Remove Friend" : asked ? "Asked - waiting for them to answer" : "Add Friend",
                selected: friend || asked,
                colourOverride: friend ? Theme.Accent : asked ? Theme.TextDim : null))
        {
            if (friend && plugin.Friends.IdFor(contact.CharacterKey) is { Length: > 0 } bond)
                plugin.Friends.Remove(bond);
            else if (!friend && !asked)
                plugin.Friends.Ask(contact.CharacterKey);
        }

        ProfileFields.At(rowOrigin, (buttons * 2f) + (gap * 2f));
        ImGui.BeginGroup();
        ImGui.TextColored(contact.Blocked ? Theme.TextDisabled : Theme.Accent, contact.Name);
        ImGui.TextColored(Theme.TextDisabled, contact.CharacterKey);
        ImGui.EndGroup();

        var right = width - (buttons * 3f) - (gap * 2f);

        ProfileFields.At(rowOrigin, right);

        if (EchoButton.BareIcon("##openprofile", plugin.Fonts.Icon, FontAwesomeIcon.AddressCard, buttons,
                contact.Blocked ? "Blocked - unblock to read them" : "Open their profile")
            && !contact.Blocked)
        {
            plugin.ProfileView.Open(contact.CharacterKey);
        }

        ImGui.SameLine(0f, gap);

        if (EchoButton.BareIcon("##block", plugin.Fonts.Icon,
                contact.Blocked ? FontAwesomeIcon.EyeSlash : FontAwesomeIcon.Eye, buttons,
                contact.Blocked
                    ? "Blocked. Click to see them again."
                    : "Block - hides their profile, statuses and roleplay name",
                selected: contact.Blocked, colourOverride: contact.Blocked ? Theme.Bad : null))
        {
            Book.SetBlocked(contact.CharacterKey, contact.Name, !contact.Blocked);
        }

        ImGui.SameLine(0f, gap);

        if (EchoButton.BareIcon("##forget", plugin.Fonts.Icon, FontAwesomeIcon.Times, buttons, "Forget"))
        {
            forgetting = contact.CharacterKey;
            forgetRequested = true;
        }

        ImGui.SetCursorScreenPos(new Vector2(rowOrigin.X, MathF.Max(
            ImGui.GetItemRectMax().Y, rowOrigin.Y + (ImGui.GetTextLineHeightWithSpacing() * 2f)) + (6f * scale)));

        DrawNoteRow(contact, width, scale);
    }

    /// The note, and which group they are filed under.
    private void DrawNoteRow(Contact contact, float width, float scale)
    {
        var groupWidth = MathF.Min(150f * scale, width * 0.32f);
        var noteWidth = MathF.Max(80f * scale, width - groupWidth - (10f * scale));

        var note = contact.Note;

        ImGui.SetNextItemWidth(noteWidth);

        if (ImGui.InputTextWithHint("##note", "Your Note", ref note, 240))
            Book.Note(contact.CharacterKey, note);

    }

    private const string ForgetPopup = "##confirmforget";

    private void DrawForgetConfirmation(float scale)
    {
        if (!ImGui.BeginPopup(ForgetPopup))
            return;

        var contact = forgetting is null ? null : Book.ByKey(forgetting);

        if (contact is null)
        {
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            return;
        }

        ImGui.TextColored(Theme.Bad, "Forget Contact");
        ImGui.TextUnformatted(contact.Name);

        ImGui.TextColored(Theme.TextDim,
            contact.Note.Length > 0 ? "Your note about them goes too." : "They return if you read them again.");

        if (contact.Blocked)
            ImGui.TextColored(Theme.Warning, "They are blocked. Forgetting them unblocks them.");

        ImGui.Dummy(new Vector2(0f, 6f * scale));

        if (EchoButton.Draw("##confirmforgetyes", "Forget", new Vector2(92f * scale, 26f * scale),
                accentOverride: Theme.Bad))
        {
            Book.Forget(contact.CharacterKey);
            forgetting = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine(0f, 8f * scale);

        if (EchoButton.Draw("##confirmforgetno", "Cancel", new Vector2(92f * scale, 26f * scale)))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }
}
