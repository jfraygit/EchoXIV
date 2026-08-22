using System;
using System.Collections.Generic;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace EchoRoleplay.Game;

/// Where the game's own interface is on screen, so nothing gets drawn over it.
public sealed class GameUiRegions
{
    /// How much of the screen an addon may cover before it is treated as a container rather than as a piece
    /// of interface.
    private const float ContainerAreaFraction = 0.6f;

    /// Below this the root node is faded far enough that nothing is really on screen.
    private const byte AlphaFloor = 8;

    /// Bits of an addon's VisibilityFlags that actually keep it off the screen.
    private const ushort HidesAddonMask = 0x7FFF;

    /// Addons that live in the world rather than on the HUD, and are therefore not something to hide behind.
    private static readonly string[] WorldSpaceAddons =
    [
        "NamePlate",
        "_MiniTalk",
    ];

    /// A collected rectangle and the addon it came from.
    public readonly record struct Region(Vector2 Min, Vector2 Max, string Name);

    private readonly List<Region> regions = [];

    /// How many regions were collected last frame, for the diagnostics panel.
    public int Count => regions.Count;

    public IReadOnlyList<Region> Regions => regions;

    /// How many frames an interface scan is kept before being redone.
    private const int RefreshIntervalFrames = 3;

    private int frame;

    /// Nodes visited on the last scan.
    public int LastNodesVisited { get; private set; }

    /// One line per addon examined, saying what happened to it.
    public IReadOnlyList<string> Report => report;

    private readonly List<string> report = [];

    /// Whether to build Report.
    public bool Explain { get; set; }

    /// Rebuilds the list.
    public unsafe void Refresh(Vector2 screenSize)
    {
        if (frame++ % RefreshIntervalFrames != 0)
            return;

        regions.Clear();
        report.Clear();
        LastNodesVisited = 0;

        var manager = RaptureAtkUnitManager.Instance();
        if (manager == null)
            return;

        var screenArea = MathF.Max(1f, screenSize.X * screenSize.Y);
        var units = manager->AllLoadedUnitsList;

        for (var i = 0; i < units.Count; i++)
        {
            var unit = units.Entries[i].Value;
            if (unit == null)
                continue;

            var name = unit->NameString;

            if (Array.IndexOf(WorldSpaceAddons, name) >= 0)
            {
                Note(name, "world-space layer");
                continue;
            }

            if (!unit->IsVisible)
            {
                Note(name, "not visible");
                continue;
            }

            if ((unit->VisibilityFlags & HidesAddonMask) != 0)
            {
                Note(name, $"visibility flags {unit->VisibilityFlags}");
                continue;
            }

            if (unit->Alpha <= AlphaFloor)
            {
                Note(name, $"addon alpha {unit->Alpha}");
                continue;
            }

            var root = unit->RootNode;
            if (root == null)
            {
                Note(name, "no root node");
                continue;
            }

            if ((root->NodeFlags & NodeFlags.Visible) == 0)
            {
                Note(name, "root hidden");
                continue;
            }

            var visited = 0;
            var min = new Vector2(float.MaxValue);
            var max = new Vector2(float.MinValue);

            Union(root, 0, ref visited, ref min, ref max);
            LastNodesVisited += visited;

            if (min.X > max.X || min.Y > max.Y)
            {
                Note(name, $"empty union ({visited} nodes)");
                continue;
            }

            var declaredSize = new Vector2(root->Width, root->Height) * unit->Scale;

            if (declaredSize.X > 1f && declaredSize.Y > 1f)
            {
                var declaredMin = new Vector2(unit->X, unit->Y);

                min = Vector2.Max(min, declaredMin);
                max = Vector2.Min(max, declaredMin + declaredSize);

                if (min.X >= max.X || min.Y >= max.Y)
                {
                    Note(name, "union outside declared box");
                    continue;
                }
            }

            var size = max - min;
            if (size.X <= 1f || size.Y <= 1f)
            {
                Note(name, "degenerate box");
                continue;
            }

            if (size.X * size.Y >= screenArea * ContainerAreaFraction)
            {
                Note(name, $"container {size.X:0}x{size.Y:0}");
                continue;
            }

            Note(name, $"claimed {size.X:0}x{size.Y:0}");
            regions.Add(new Region(min, max, unit->NameString));
        }
    }

    /// Records what happened to one addon, when anybody is listening.
    private void Note(string name, string outcome)
    {
        if (Explain && name.Length > 0)
            report.Add($"{name}: {outcome}");
    }

    /// How deep the node walk goes, and how many nodes it may visit per addon.
    private const int MaximumDepth = 10;
    private const int MaximumNodes = 400;

    /// Grows a bounding box over every node that actually paints.
    private static unsafe void Union(
        AtkResNode* first, int depth, ref int visited, ref Vector2 min, ref Vector2 max)
    {
        if (depth > MaximumDepth)
            return;

        for (var node = first; node != null; node = node->PrevSiblingNode)
        {
            if (visited++ > MaximumNodes)
                return;

            Measure(node, depth, ref visited, ref min, ref max);
        }
    }

    private static unsafe void Measure(
        AtkResNode* node, int depth, ref int visited, ref Vector2 min, ref Vector2 max)
    {

        if ((node->NodeFlags & NodeFlags.Visible) == 0)
            return;

        var type = node->Type;

        if (type is NodeType.Image or NodeType.Text or NodeType.NineGrid or NodeType.Counter)
        {
            var width = node->Width * node->ScaleX;
            var height = node->Height * node->ScaleY;

            if (width > 0f && height > 0f)
            {
                min = Vector2.Min(min, new Vector2(node->ScreenX, node->ScreenY));
                max = Vector2.Max(max, new Vector2(node->ScreenX + width, node->ScreenY + height));
            }
        }

        var component = node->GetComponent();
        if (component != null)
            Union(component->UldManager.RootNode, depth + 1, ref visited, ref min, ref max);

        Union(node->ChildNode, depth + 1, ref visited, ref min, ref max);
    }

    /// Whether a rectangle overlaps any part of the game's interface.
    public bool Overlaps(Vector2 min, Vector2 max)
    {
        foreach (var region in regions)
        {
            if (min.X < region.Max.X && max.X > region.Min.X
                && min.Y < region.Max.Y && max.Y > region.Min.Y)
                return true;
        }

        return false;
    }
}
