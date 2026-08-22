using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using EchoRoleplay.Game;
using EchoRoleplay.Shared;

namespace EchoRoleplay.UI;

/// The row of status icons that hangs under a character, drawn out in the world.
public sealed class StatusRowOverlay
{
    /// One icon in a drawn row, resolved from a profile.
    private readonly record struct RowEntry(uint IconId, string Label, string Detail);

    /// Scratch list for the row being built, reused across every character and every frame.
    private readonly List<RowEntry> entries = [];

    /// Icon box, gap between icons, and how far under the character's feet the row sits.
    private const float DropDesign = 6f;

    /// Gap between icons, as a fraction of the box.
    private const float IconGapFraction = 0.18f;

    /// How much of its box the icon is inset by, as a fraction.
    private const float IconInsetFraction = 0.06f;

    /// How small the row is allowed to get as the camera pulls back.
    private const float MinimumRowScale = 0.55f;

    /// Within this many world units the row is drawn at full size.
    private const float FullSizeDistance = 10f;

    /// How much of the distance range is at full opacity before the fade starts.
    private const float FadeStartFraction = 0.75f;

    /// The ease rate at either end of the smoothing dial.
    private const float SmoothingSpeedMin = 8f;
    private const float SmoothingSpeedMax = 60f;

    /// The rate at the far end of the extended range.
    private const float SmoothingSpeedFloor = 2f;

    /// Beyond this much movement in one frame, the row is teleported rather than eased.
    private const float SmoothingSnapDistance = 160f;

    /// How far a row must move in one frame before the easing steps out of the way entirely, in design
    /// pixels.
    private const float MotionFullFollow = 14f;

    /// The top of the smoothing dial.
    public const float MaximumSmoothing = 2f;

    /// Below this much target movement in a frame, in design pixels, the row is treated as stationary and
    /// held exactly where it is.
    private const float StillThreshold = 0.9f;

    private readonly IGameGui gameGui;
    private readonly IObjectTable objectTable;
    private readonly LineOfSight lineOfSight;
    private readonly ProfileDirectory directory;
    private readonly IconCatalogue icons;
    private readonly GameUiRegions uiRegions = new();

    /// How many rows the last frame withheld because they would have landed on the game's own interface.
    public int LastBehindUi { get; private set; }

    /// How many pieces of game interface were found last frame.
    public int UiRegionCount => uiRegions.Count;

    /// Nodes the last interface scan walked.
    public int UiNodesVisited => uiRegions.LastNodesVisited;

    /// Per-addon outcomes from the last interface scan.
    public IReadOnlyList<string> UiScanReport => uiRegions.Report;

    /// Whether the scan records why each addon was accepted or rejected.
    public bool ExplainUiScan
    {
        get => uiRegions.Explain;
        set => uiRegions.Explain = value;
    }

    /// Last drawn screen position per character, for the smoothing.
    private readonly record struct SmoothState(Vector2 Current, Vector2 LastTarget);

    private readonly Dictionary<ulong, SmoothState> smoothed = [];
    private readonly List<ulong> seen = [];

    /// Diagnostics for the Development panel: how many rows the last frame actually drew, as opposed to how
    /// many players were nearby.
    public int LastDrawnRows { get; private set; }
    public int LastNearbyPlayers { get; private set; }

    /// How many rows the line-of-sight check removed last frame.
    public int LastOccluded { get; private set; }

    public StatusRowOverlay(
        IGameGui gameGui, IObjectTable objectTable, LineOfSight lineOfSight,
        ProfileDirectory directory, IconCatalogue icons)
    {
        this.gameGui = gameGui;
        this.objectTable = objectTable;
        this.lineOfSight = lineOfSight;
        this.directory = directory;
        this.icons = icons;
    }

    public void Draw(
        float yOffsetDesign, bool markAnchor, float smoothing, float maxDistance, float iconBoxDesign,
        string localCharacterKey, bool avoidGameUi, bool snapToPixels)
    {
        LastDrawnRows = 0;
        LastNearbyPlayers = 0;
        LastOccluded = 0;
        LastBehindUi = 0;

        lineOfSight.BeginFrame();
        seen.Clear();

        if (gameGui.GameUiHidden)
            return;

        var viewport = ImGuiHelpers.MainViewport;

        if (avoidGameUi)
            uiRegions.Refresh(viewport.Size);

        var drawList = ImGui.GetBackgroundDrawList();
        var mouse = ImGui.GetMousePos();

        (string Label, string Detail, Vector2 At)? hovered = null;

        var eye = lineOfSight.CameraPosition;

        foreach (var candidate in objectTable.PlayerObjects)
        {
            if (candidate is not IPlayerCharacter obj)
                continue;

            LastNearbyPlayers++;

            var profile = directory.Lookup(ProfileDirectory.KeyFor(obj), localCharacterKey);
            if (profile is null)
                continue;

            BuildRow(profile);
            if (entries.Count == 0)
                continue;

            var feetWorld = RenderedPosition(obj);

            var distance = eye is { } camera ? Vector3.Distance(camera, feetWorld) : 0f;
            if (distance > maxDistance)
                continue;

            if (!lineOfSight.IsVisible(obj.GameObjectId, feetWorld))
            {
                LastOccluded++;

                if (markAnchor
                    && gameGui.WorldToScreen(feetWorld, out var hiddenAt, out var hiddenInView)
                    && hiddenInView)
                    DrawAnchorMark(drawList, hiddenAt, AnchorOccluded);

                continue;
            }

            if (!gameGui.WorldToScreen(feetWorld, out var feet, out var inView) || !inView)
                continue;

            seen.Add(obj.GameObjectId);

            if (smoothing > 0.001f)
                feet = Smooth(obj.GameObjectId, feet, smoothing);

            if (snapToPixels)
                feet = new Vector2(MathF.Round(feet.X), MathF.Round(feet.Y));

            var found = DrawRow(
                drawList, feet, FadeFor(distance, maxDistance), SizeFor(distance),
                yOffsetDesign, iconBoxDesign, mouse, avoidGameUi, out var hiddenByUi);

            if (found is not null)
                hovered = found;

            if (markAnchor)
                DrawAnchorMark(drawList, feet, hiddenByUi ? AnchorBehindUi : AnchorDrawn);
        }

        if (hovered is { } tip)
            DrawTooltip(drawList, tip.Label, tip.Detail, tip.At);

        if (markAnchor && avoidGameUi)
            DrawUiRegions(drawList, viewport.Pos);

        PruneSmoothing();
    }

    /// How faded a row is at this distance.
    private static float FadeFor(float distance, float maxDistance)
    {
        var fadeStart = maxDistance * FadeStartFraction;
        if (distance <= fadeStart)
            return 1f;

        var span = MathF.Max(0.01f, maxDistance - fadeStart);
        return Math.Clamp(1f - ((distance - fadeStart) / span), 0f, 1f);
    }

    /// The raw distance falloff, before any legibility floor.
    private static float SizeFor(float distance)
    {
        if (distance <= FullSizeDistance)
            return 1f;

        return Math.Clamp(FullSizeDistance / distance, 0.02f, 1f);
    }

    /// Eases a row toward where the projection says it should be.
    private Vector2 Smooth(ulong gameObjectId, Vector2 target, float strength)
    {
        if (!smoothed.TryGetValue(gameObjectId, out var state))
        {
            smoothed[gameObjectId] = new SmoothState(target, target);
            return target;
        }

        var current = state.Current;

        if (Vector2.Distance(current, target) > SmoothingSnapDistance * UiHelpers.Scale)
        {
            smoothed[gameObjectId] = new SmoothState(target, target);
            return target;
        }

        var targetTravel = Vector2.Distance(state.LastTarget, target);

        if (targetTravel < StillThreshold * UiHelpers.Scale
            && Vector2.Distance(current, target) < StillThreshold * 2f * UiHelpers.Scale)
        {
            smoothed[gameObjectId] = new SmoothState(current, target);
            return current;
        }

        var motion = Math.Clamp(targetTravel / (MotionFullFollow * UiHelpers.Scale), 0f, 1f);
        var applied = Math.Clamp(strength, 0f, MaximumSmoothing) * (1f - motion);

        var speed = applied <= 1f
            ? SmoothingSpeedMax - (applied * (SmoothingSpeedMax - SmoothingSpeedMin))
            : SmoothingSpeedMin - ((applied - 1f) * (SmoothingSpeedMin - SmoothingSpeedFloor));

        var deltaTime = ImGui.GetIO().DeltaTime;
        var eased = new Vector2(
            UiHelpers.Lerp(current.X, target.X, speed, deltaTime),
            UiHelpers.Lerp(current.Y, target.Y, speed, deltaTime));

        smoothed[gameObjectId] = new SmoothState(eased, target);
        return eased;
    }

    /// Forgets characters who are no longer being drawn, so the map does not grow for the whole session as
    /// people come and go across a hub.
    private void PruneSmoothing()
    {
        if (smoothed.Count <= seen.Count)
            return;

        foreach (var id in smoothed.Keys.ToList())
        {
            if (!seen.Contains(id))
                smoothed.Remove(id);
        }
    }

    /// Where the renderer is actually drawing this character.
    private static unsafe Vector3 RenderedPosition(Dalamud.Game.ClientState.Objects.Types.IGameObject obj)
    {
        var native = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)obj.Address;
        if (native == null)
            return obj.Position;

        var draw = native->DrawObject;
        if (draw == null)
            return obj.Position;

        var position = draw->Object.Position;
        return new Vector3(position.X, position.Y, position.Z);
    }

    /// A cross exactly on the projected point, drawn under no offset at all.
    private static void DrawAnchorMark(ImDrawListPtr drawList, Vector2 at, Vector4 colour)
    {
        var scale = UiHelpers.Scale;
        var arm = 5f * scale;
        var packed = ImGui.GetColorU32(colour);

        drawList.AddLine(new Vector2(at.X - arm, at.Y), new Vector2(at.X + arm, at.Y), packed, 1.5f * scale);
        drawList.AddLine(new Vector2(at.X, at.Y - arm), new Vector2(at.X, at.Y + arm), packed, 1.5f * scale);
    }

    /// Marker colours, so a withheld row says why it was withheld.
    private static readonly Vector4 AnchorDrawn = new(0.30f, 1f, 0.40f, 0.95f);
    private static readonly Vector4 AnchorBehindUi = new(1f, 0.85f, 0.20f, 0.95f);
    private static readonly Vector4 AnchorOccluded = new(1f, 0.30f, 0.30f, 0.95f);

    /// Outlines every rectangle the interface scan claimed, labelled with the addon that claimed it.
    private void DrawUiRegions(ImDrawListPtr drawList, Vector2 viewportPos)
    {
        var colour = ImGui.GetColorU32(new Vector4(1f, 0.85f, 0.20f, 0.55f));

        foreach (var region in uiRegions.Regions)
        {
            drawList.AddRect(region.Min, region.Max, colour);
            drawList.AddText(region.Min + new Vector2(2f, 2f), colour, region.Name);
        }

        var summary = uiRegions.Count == 0
            ? $"UI SCAN FOUND NOTHING ({uiRegions.LastNodesVisited} nodes walked)"
            : $"UI regions: {uiRegions.Count} ({uiRegions.LastNodesVisited} nodes)";

        drawList.AddText(
            viewportPos + new Vector2(8f, 8f),
            ImGui.GetColorU32(uiRegions.Count == 0 ? new Vector4(1f, 0.35f, 0.35f, 1f) : new Vector4(1f, 0.85f, 0.20f, 1f)),
            summary);
    }

    /// Turns a profile into the icons to draw, into the reused scratch list.
    private void BuildRow(RoleplayProfile profile)
    {
        entries.Clear();

        foreach (var status in profile.Statuses)
        {
            if (entries.Count >= ProfileLimits.Statuses)
                break;

            if (status.HasExpired(DateTime.UtcNow))
                continue;

            entries.Add(new RowEntry(
                status.IconId,
                string.IsNullOrWhiteSpace(status.Label) ? "(Unnamed)" : status.Label,
                status.Detail));
        }
    }

    /// One character's row, centred just below their feet.
    private (string Label, string Detail, Vector2 At)? DrawRow(
        ImDrawListPtr drawList, Vector2 feet, float alpha, float rowScale,
        float yOffsetDesign, float iconBoxDesign, Vector2 mouse, bool avoidGameUi, out bool hiddenByUi)
    {
        hiddenByUi = false;

        var sizeScale = UiHelpers.Scale * MathF.Max(rowScale, MinimumRowScale);
        var dropScale = UiHelpers.Scale * rowScale;

        var box = iconBoxDesign * sizeScale;
        var gap = box * IconGapFraction;
        var drop = (DropDesign + yOffsetDesign) * dropScale;

        var count = entries.Count;
        var rowWidth = (box * count) + (gap * (count - 1));

        var origin = new Vector2(feet.X - (rowWidth / 2f), feet.Y + drop);

        if (avoidGameUi && uiRegions.Overlaps(origin, origin + new Vector2(rowWidth, box)))
        {
            LastBehindUi++;
            hiddenByUi = true;
            return null;
        }

        LastDrawnRows++;

        (string, string, Vector2)? hovered = null;

        for (var i = 0; i < count; i++)
        {
            var (iconId, label, detail) = entries[i];

            var min = new Vector2(origin.X + (i * (box + gap)), origin.Y);
            var max = min + new Vector2(box, box);

            var over = mouse.X >= min.X && mouse.X <= max.X && mouse.Y >= min.Y && mouse.Y <= max.Y;

            drawList.AddRectFilled(
                min, max,
                ImGui.GetColorU32(new Vector4(0f, 0f, 0f, (over ? 0.72f : 0.55f) * alpha)),
                4f * sizeScale);

            drawList.AddRect(
                min, max,
                ImGui.GetColorU32(new Vector4(
                    Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, (over ? 0.95f : 0.45f) * alpha)),
                4f * sizeScale, ImDrawFlags.None, 1f * sizeScale);

            var inset = box * IconInsetFraction;

            icons.Draw(
                drawList, iconId, min + new Vector2(inset, inset), box - (inset * 2f),
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, (over ? 1f : 0.92f) * alpha)));

            if (over)
                hovered = (label, detail, new Vector2(min.X, max.Y));
        }

        return hovered;
    }

    /// The hover tooltip, drawn by hand.
    private void DrawTooltip(ImDrawListPtr drawList, string label, string detail, Vector2 below)
    {
        var scale = UiHelpers.Scale;
        var padding = new Vector2(8f, 6f) * scale;
        var gap = 4f * scale;

        var hasDetail = !string.IsNullOrWhiteSpace(detail);

        var labelSize = ImGui.CalcTextSize(label);
        var detailSize = hasDetail ? ImGui.CalcTextSize(detail) : Vector2.Zero;

        var size = new Vector2(
            MathF.Max(labelSize.X, detailSize.X),
            labelSize.Y + (hasDetail ? gap + detailSize.Y : 0f)) + (padding * 2f);

        var min = new Vector2(below.X, below.Y + (4f * scale));

        var viewport = ImGuiHelpers.MainViewport;
        var limit = viewport.Pos + viewport.Size;
        min.X = MathF.Min(min.X, limit.X - size.X - (4f * scale));
        min.X = MathF.Max(min.X, viewport.Pos.X + (4f * scale));
        min.Y = MathF.Min(min.Y, limit.Y - size.Y - (4f * scale));

        var max = min + size;

        drawList.AddRectFilled(
            min + new Vector2(0f, 2f * scale), max + new Vector2(0f, 2f * scale),
            ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.45f)), 6f * scale);

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(Theme.Background), 6f * scale);
        drawList.AddRect(
            min, max, ImGui.GetColorU32(new Vector4(Theme.Accent.X, Theme.Accent.Y, Theme.Accent.Z, 0.8f)),
            6f * scale, ImDrawFlags.None, 1.2f * scale);

        drawList.AddText(min + padding, ImGui.GetColorU32(Theme.Accent), label);

        if (hasDetail)
        {
            drawList.AddText(
                min + padding + new Vector2(0f, labelSize.Y + gap), ImGui.GetColorU32(Theme.TextDim), detail);
        }
    }
}
