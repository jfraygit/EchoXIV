using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using EchoRoleplay.Game;
using EchoRoleplay.Shared;

namespace EchoRoleplay.UI;

/// Point at somebody and read who they are.
public sealed class ProfileTooltip
{
    private readonly Plugin plugin;

    /// How long the pointer has to rest on somebody, in seconds.
    private const float DwellSeconds = 0.32f;

    /// How fast the card fades in and out.
    private const float FadeSpeed = 14f;

    /// How wide the card is allowed to get, in design units.
    private const float MaximumWidth = 336f;

    /// How narrow it is allowed to get.
    private const float MinimumWidth = 246f;

    /// Gap between the cursor and the card's corner.
    private const float CursorGap = 20f;

    /// How far the card lifts as it fades in, in design units.
    private const float RiseDistance = 7f;

    private ulong hovered;
    private float dwell;
    private float alpha;

    /// Last frame's size, for keeping the card on screen.
    private Vector2 lastSize;

    public ProfileTooltip(Plugin plugin) => this.plugin = plugin;

    /// Whether the card is on screen.
    public bool Showing => alpha > 0.01f;

    public void Draw()
    {
        var deltaTime = ImGui.GetIO().DeltaTime;
        var profile = Resolve(out var characterKey);

        if (profile is null)
        {
            dwell = 0f;
            hovered = 0;
            alpha = MathF.Max(0f, alpha - (FadeSpeed * deltaTime));
        }
        else
        {
            dwell += deltaTime;
            alpha = dwell >= DwellSeconds
                ? MathF.Min(1f, alpha + (FadeSpeed * deltaTime))
                : MathF.Max(0f, alpha - (FadeSpeed * deltaTime));
        }

        if (alpha <= 0.01f || profile is null)
            return;

        DrawCard(profile, characterKey, alpha);
    }

    /// The profile of whoever the cursor is on, or null.
    private RoleplayProfile? Resolve(out string characterKey)
    {
        characterKey = string.Empty;

        if (!plugin.Configuration.ShowHoverTooltip)
            return null;

        if (plugin.HiddenInDuty)
            return null;

        if (ImGui.GetIO().WantCaptureMouse)
            return null;

        var target = Plugin.Targets.MouseOverTarget ?? Plugin.Targets.MouseOverNameplateTarget;

        if (target is not IPlayerCharacter player)
            return null;

        if (player.GameObjectId != hovered)
        {
            hovered = player.GameObjectId;
            dwell = 0f;
        }

        characterKey = ProfileDirectory.KeyFor(player);

        plugin.Directory.Request(characterKey);

        var profile = plugin.Directory.Lookup(characterKey, plugin.LocalCharacterKey);

        return profile is not null && profile.HasAnything ? profile : null;
    }

    private void DrawCard(RoleplayProfile profile, string characterKey, float opacity)
    {
        var scale = UiHelpers.Scale;
        var viewport = ImGuiHelpers.MainViewport;
        var mouse = ImGui.GetMousePos();
        var colour = NameColour(profile);

        var gap = CursorGap * scale;
        var position = new Vector2(mouse.X + gap, mouse.Y + gap);

        if (lastSize.X > 0f)
        {
            var right = viewport.Pos.X + viewport.Size.X;
            var bottom = viewport.Pos.Y + viewport.Size.Y;

            if (position.X + lastSize.X > right)
                position.X = mouse.X - gap - lastSize.X;

            if (position.Y + lastSize.Y > bottom)
                position.Y = MathF.Max(viewport.Pos.Y, mouse.Y - gap - lastSize.Y);
        }

        position.Y += (1f - opacity) * RiseDistance * scale;

        ImGui.SetNextWindowPos(position, ImGuiCond.Always);
        ImGui.SetNextWindowSizeConstraints(new Vector2(0f, 0f), new Vector2(MaximumWidth * scale, float.MaxValue));

        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoBringToFrontOnFocus
            | ImGuiWindowFlags.NoNav
            | ImGuiWindowFlags.NoInputs
            | ImGuiWindowFlags.AlwaysAutoResize;

        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, opacity);
        var colours = Theme.Push();

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        if (ImGui.Begin("##echorptooltip", flags))
        {
            DrawContents(profile, characterKey, colour, scale);
            lastSize = ImGui.GetWindowSize();
            DrawFrame(colour, scale, opacity);
        }

        ImGui.End();

        ImGui.PopStyleVar();
        Theme.Pop(colours);
        ImGui.PopStyleVar();
    }

    /// The frame, and a shadow to lift the card off whatever it is standing on.
    private static void DrawFrame(Vector4 colour, float scale, float opacity)
    {
        var drawList = ImGui.GetWindowDrawList();
        var inset = 1.5f * scale;

        var min = ImGui.GetWindowPos() + new Vector2(inset, inset);
        var max = ImGui.GetWindowPos() + ImGui.GetWindowSize() - new Vector2(inset, inset);
        var rounding = ImGui.GetStyle().WindowRounding;

        drawList.PushClipRectFullScreen();

        for (var i = 3; i >= 1; i--)
        {
            var spread = new Vector2(i * 1.5f * scale, i * 1.5f * scale);
            drawList.AddRect(
                min - spread, max + spread,
                ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.16f / i * opacity)),
                rounding + (i * 1.5f * scale), ImDrawFlags.None, 2f * scale);
        }

        drawList.AddRect(
            min, max,
            ImGui.GetColorU32(new Vector4(colour.X, colour.Y, colour.Z, 0.8f * opacity)),
            rounding, ImDrawFlags.None, 1.5f * scale);

        drawList.PopClipRect();
    }

    /// The card: an identity band, the glance under it, and their statuses in a recessed footer.
    private void DrawContents(RoleplayProfile profile, string characterKey, Vector4 colour, float scale)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();

        var padX = 15f * scale;
        var padY = 14f * scale;

        var medallion = 52f * scale;
        var medallionGap = 13f * scale;
        var nameSize = 19f * scale;
        var lineHeight = ImGui.GetTextLineHeight();


        var name = !string.IsNullOrWhiteSpace(profile.Name)
            ? profile.Name
            : characterKey.Split('@')[0];

        var facts = Join(profile.Title, profile.HouseName, profile.Pronouns);
        var glance = Join(profile.Race, profile.Age, profile.Occupation);

        var (statusLabel, statusColour) = StatusPresentation(profile.RpStatus);
        var verified = plugin.Directory.IsVerified(characterKey, plugin.LocalCharacterKey);

        var statuses = plugin.Configuration.Statuses == StatusPlacement.InTooltip
            ? profile.Statuses
            : [];

        var iconBox = lineHeight * 1.3f;
        var iconGap = 10f * scale;


        float nameWidth;

        using (plugin.Fonts.Header.PushSafe())
        {
            nameWidth = ImGui.CalcTextSize(name).X * (nameSize / ImGui.GetFontSize());
        }

        var markWidth = verified ? (12f * scale) + (6f * scale) : 0f;

        var identityWidth = MathF.Max(
            nameWidth,
            MathF.Max(ImGui.CalcTextSize(characterKey).X + markWidth, ImGui.CalcTextSize(facts).X));

        var wanted = (padX * 2f) + medallion + medallionGap + identityWidth;

        if (glance.Length > 0)
            wanted = MathF.Max(wanted, (padX * 2f) + ImGui.CalcTextSize(glance).X);

        foreach (var status in statuses)
        {
            var words = MathF.Max(
                ImGui.CalcTextSize(StatusLabel(status)).X,
                status.Detail is { Length: > 0 } detail ? ImGui.CalcTextSize(detail).X : 0f);

            wanted = MathF.Max(wanted, (padX * 2f) + iconBox + iconGap + words);
        }

        var width = Math.Clamp(wanted, MinimumWidth * scale, MaximumWidth * scale);

        var textX = origin.X + padX + medallion + medallionGap;
        var textWidth = origin.X + width - padX - textX;
        var bodyWidth = width - (padX * 2f);
        var statusWidth = bodyWidth - iconBox - iconGap;


        var identityHeight = nameSize + (5f * scale) + lineHeight;

        if (facts.Length > 0)
            identityHeight += (3f * scale) + lineHeight;

        var headerBottom = origin.Y + padY + MathF.Max(medallion, identityHeight) + (padY * 0.8f);

        var bodyY = headerBottom + (10f * scale);
        var bodyBottom = headerBottom;

        if (statusLabel.Length > 0)
            bodyBottom = bodyY + ProfileFields.StatePillHeight;

        var glanceY = bodyBottom > headerBottom ? bodyBottom + (9f * scale) : bodyY;

        if (glance.Length > 0)
            bodyBottom = glanceY + lineHeight;

        var bodyEnd = bodyBottom + (bodyBottom > headerBottom ? padY : 0f);

        var rowHeights = new float[statuses.Count];
        var statusesTop = bodyEnd;
        var height = bodyEnd;

        if (statuses.Count > 0)
        {
            var rowGap = 9f * scale;
            var measured = statusesTop + (12f * scale);

            for (var i = 0; i < statuses.Count; i++)
            {
                var detail = statuses[i].Detail;
                var words = lineHeight;

                if (!string.IsNullOrWhiteSpace(detail))
                    words += (2f * scale) + ImGui.CalcTextSize(detail, false, statusWidth).Y;

                rowHeights[i] = MathF.Max(iconBox, words);
                measured += rowHeights[i] + rowGap;
            }

            height = measured - rowGap + (13f * scale);
        }


        var rounding = ImGui.GetStyle().WindowRounding;
        var right = origin.X + width;

        var washTop = ImGui.GetColorU32(new Vector4(colour.X, colour.Y, colour.Z, 0.15f));
        var washBottom = ImGui.GetColorU32(new Vector4(colour.X, colour.Y, colour.Z, 0f));

        drawList.AddRectFilled(
            origin, new Vector2(right, origin.Y + rounding), washTop, rounding, ImDrawFlags.RoundCornersTop);
        drawList.AddRectFilledMultiColor(
            new Vector2(origin.X, origin.Y + rounding), new Vector2(right, headerBottom),
            washTop, washTop, washBottom, washBottom);

        drawList.AddLine(
            new Vector2(origin.X + rounding, origin.Y + (1f * scale)),
            new Vector2(right - rounding, origin.Y + (1f * scale)),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.06f)), 1f * scale);

        if (statusLabel.Length > 0 || glance.Length > 0)
        {
            var solid = ImGui.GetColorU32(new Vector4(Theme.Border.X, Theme.Border.Y, Theme.Border.Z, 0.6f));
            var clear = ImGui.GetColorU32(new Vector4(Theme.Border.X, Theme.Border.Y, Theme.Border.Z, 0f));

            var ruleY = headerBottom;
            var middle = origin.X + (width / 2f);
            var thickness = 1f * scale;

            drawList.AddRectFilledMultiColor(
                new Vector2(origin.X + padX, ruleY), new Vector2(middle, ruleY + thickness),
                clear, solid, solid, clear);
            drawList.AddRectFilledMultiColor(
                new Vector2(middle, ruleY), new Vector2(right - padX, ruleY + thickness),
                solid, clear, clear, solid);
        }

        if (statuses.Count > 0)
        {
            drawList.AddRectFilled(
                new Vector2(origin.X, statusesTop), new Vector2(right, origin.Y + height),
                ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.22f)), rounding, ImDrawFlags.RoundCornersBottom);

            drawList.AddLine(
                new Vector2(origin.X, statusesTop), new Vector2(right, statusesTop),
                ImGui.GetColorU32(new Vector4(Theme.Border.X, Theme.Border.Y, Theme.Border.Z, 0.55f)), 1f * scale);
        }


        DrawMedallion(
            profile, characterKey,
            new Vector2(origin.X + padX, origin.Y + padY + ((MathF.Max(medallion, identityHeight) - medallion) / 2f)),
            medallion, colour, scale);

        var y = origin.Y + padY + MathF.Max(0f, (medallion - identityHeight) / 2f);

        using (plugin.Fonts.Header.PushSafe())
        {
            drawList.AddText(
                ImGui.GetFont(), nameSize, new Vector2(textX, y), ImGui.GetColorU32(colour),
                UiHelpers.Truncate(name, textWidth * (ImGui.GetFontSize() / nameSize)), 0f);
        }

        y += nameSize + (5f * scale);

        drawList.AddText(
            new Vector2(textX, y),
            ImGui.GetColorU32(verified ? Theme.Accent : Theme.TextDisabled),
            UiHelpers.Truncate(characterKey, textWidth - markWidth));

        if (verified)
        {
            var markX = textX + MathF.Min(ImGui.CalcTextSize(characterKey).X, textWidth - markWidth) + (9f * scale);

            var markY = y + UiHelpers.TextInkCentre();

            using (plugin.Fonts.Icon.PushSafe())
            {
                UiHelpers.DrawIconSized(
                    drawList, FontAwesomeIcon.CheckCircle, new Vector2(markX, markY), 12f * scale,
                    ImGui.GetColorU32(Theme.Accent));
            }
        }

        y += lineHeight + (3f * scale);

        if (facts.Length > 0)
        {
            drawList.AddText(
                new Vector2(textX, y), ImGui.GetColorU32(Theme.TextDim), UiHelpers.Truncate(facts, textWidth));
        }


        if (statusLabel.Length > 0)
        {
            ImGui.SetCursorScreenPos(new Vector2(origin.X + padX, bodyY));
            ProfileFields.StatePill(statusLabel, statusColour);
        }

        if (glance.Length > 0)
        {
            drawList.AddText(
                new Vector2(origin.X + padX, glanceY), ImGui.GetColorU32(Theme.Text),
                UiHelpers.Truncate(glance, bodyWidth));
        }


        var rowY = statusesTop + (12f * scale);

        for (var i = 0; i < statuses.Count; i++)
        {
            var status = statuses[i];

            plugin.Icons.Draw(
                drawList, status.IconId,
                new Vector2(origin.X + padX, rowY + ((lineHeight - iconBox) / 2f)), iconBox,
                ImGui.GetColorU32(Vector4.One));

            var wordsX = origin.X + padX + iconBox + iconGap;

            drawList.AddText(
                new Vector2(wordsX, rowY), ImGui.GetColorU32(Theme.Text), StatusLabel(status));

            if (!string.IsNullOrWhiteSpace(status.Detail))
            {
                drawList.AddText(
                    ImGui.GetFont(), ImGui.GetFontSize(),
                    new Vector2(wordsX, rowY + lineHeight + (2f * scale)),
                    ImGui.GetColorU32(Theme.TextDim), status.Detail, statusWidth);
            }

            rowY += rowHeights[i] + (9f * scale);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height - origin.Y));
    }

    /// The portrait, or the letter standing in for one.
    private void DrawMedallion(
        RoleplayProfile profile, string characterKey, Vector2 origin, float size, Vector4 colour, float scale)
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

        var portrait = ProfileFields.Portrait(plugin, profile, characterKey);

        if (portrait is not null)
        {
            var inset = 4f * scale;

            drawList.AddImageRounded(
                portrait.Handle,
                origin + new Vector2(inset, inset),
                origin + new Vector2(size - inset, size - inset),
                Vector2.Zero,
                Vector2.One,
                ImGui.GetColorU32(Vector4.One),
                radius - inset);

            return;
        }

        var source = !string.IsNullOrWhiteSpace(profile.Name) ? profile.Name : characterKey;
        var trimmed = source.TrimStart();

        if (trimmed.Length == 0)
            return;

        using (plugin.Fonts.Header.PushSafe())
        {
            var monogram = char.ToUpperInvariant(trimmed[0]).ToString();
            var glyphSize = 23f * scale;
            var measured = ImGui.CalcTextSize(monogram) * (glyphSize / ImGui.GetFontSize());

            drawList.AddText(
                ImGui.GetFont(), glyphSize, centre - (measured / 2f), ImGui.GetColorU32(colour), monogram, 0f);
        }
    }

    /// The colour this person reads in - theirs where they picked one, the plugin's otherwise.
    private static Vector4 NameColour(RoleplayProfile profile) =>
        profile.NameColour is { Length: >= 3 } c ? new Vector4(c[0], c[1], c[2], 1f) : Theme.Accent;

    /// Facts on one line, middot-separated, with the empty ones simply absent - a short profile should read
    /// as short rather than as full of gaps.
    private static string Join(params string?[] parts)
    {
        var kept = new List<string>(parts.Length);

        foreach (var part in parts)
        {
            if (!string.IsNullOrWhiteSpace(part))
                kept.Add(part);
        }

        return kept.Count == 0 ? string.Empty : string.Join("  ·  ", kept);
    }

    private static string StatusLabel(RoleplayStatus status) =>
        string.IsNullOrWhiteSpace(status.Label) ? "(Unnamed)" : status.Label;

    /// How a roleplay status reads.
    private static (string Label, Vector4 Colour) StatusPresentation(RpStatus status) => status switch
    {
        RpStatus.InCharacter => ("In Character", Theme.Good),
        RpStatus.LookingForRp => ("Looking For RP", Theme.Accent),
        RpStatus.InAScene => ("In A Scene", Theme.Warning),
        RpStatus.DoNotDisturb => ("Do Not Disturb", Theme.Bad),
        RpStatus.OutOfCharacter => ("Out Of Character", Theme.TextDim),
        _ => (string.Empty, Theme.TextDim),
    };
}
