using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;

namespace EchoNav.Nav;

public sealed class KnownAetheryte
{
    public uint DataId { get; set; }

    /// The object's own name, which for field operations is the same generic "Aetheryte Shard" on every one
    /// of them - useless for telling them apart.
    public string Name { get; set; } = string.Empty;

    /// The name the teleport window uses, learned by standing here and reading it off the window's "Current
    /// Location" line.
    public string AethernetName { get; set; } = string.Empty;

    public Vector3 Position { get; set; }

    public string DisplayName => string.IsNullOrEmpty(AethernetName) ? Name : AethernetName;
}

/// Remembers where a zone's aetheryte shards are.
public sealed class AetheryteRegistry(IObjectTable objectTable, IClientState clientState, string configDirectory)
{
    /// Two sightings closer than this are the same shard.
    private const float SamePlaceDistance = 5f;

    private readonly Dictionary<uint, List<KnownAetheryte>> byTerritory = [];
    private uint loadedTerritory;
    private bool dirty;

    public IReadOnlyList<KnownAetheryte> Current =>
        byTerritory.TryGetValue(loadedTerritory, out var list) ? list : [];

    /// What's around the player right now, kind and name, nearest first.
    public IReadOnlyList<string> NearbyDiagnostic { get; private set; } = [];

    /// The six aetheryte objects in North Horn, by data id.
    private static readonly HashSet<uint> NorthHornAetherytes =
        [2015429, 2015430, 2015431, 2015432, 2015433, 2015434];

    /// Whether an object looks like a shard.
    public static bool LooksLikeAetheryte(Dalamud.Game.ClientState.Objects.Types.IGameObject obj)
    {
        if (obj.ObjectKind == ObjectKind.Aetheryte)
            return true;

        if (obj.ObjectKind != ObjectKind.EventObj)
            return false;

        if (NorthHornAetherytes.Contains(obj.BaseId))
            return true;

        var name = obj.Name.TextValue;
        return name.Contains("aetheryte", StringComparison.OrdinalIgnoreCase)
            || name.Contains("aethernet", StringComparison.OrdinalIgnoreCase);
    }

    /// North Horn's destinations in the order the teleport window lists them, by data id.
    private static readonly uint[] NorthHornMenuOrder =
        [2015429, 2015434, 2015430, 2015431, 2015432, 2015433];

    private static int MenuRowOf(uint dataId) => Array.IndexOf(NorthHornMenuOrder, dataId);

    /// Names every shard at once from an open teleport window.
    public void LearnAllFromMenu(IReadOnlyList<string> destinations, string currentLocation, Vector3 playerPosition)
    {
        if (destinations.Count < NorthHornMenuOrder.Length || string.IsNullOrWhiteSpace(currentLocation))
            return;

        var here = Nearest(playerPosition);
        if (here == null || Vector3.Distance(here.Position, playerPosition) > 20f)
            return;

        var anchor = MenuRowOf(here.DataId);
        if (anchor < 0 || !string.Equals(destinations[anchor], currentLocation, StringComparison.OrdinalIgnoreCase))
            return;

        var changed = false;

        foreach (var shard in Current)
        {
            var row = MenuRowOf(shard.DataId);
            if (row < 0 || shard.AethernetName == destinations[row])
                continue;

            shard.AethernetName = destinations[row];
            changed = true;
        }

        if (!changed)
            return;

        dirty = true;
        SaveNow();
    }

    public void Tick()
    {
        var territory = clientState.TerritoryType;
        if (territory != loadedTerritory)
        {
            SaveNow();
            loadedTerritory = territory;
            if (!byTerritory.ContainsKey(territory))
                byTerritory[territory] = Load(configDirectory, territory);
        }

        if (!byTerritory.TryGetValue(territory, out var known))
            byTerritory[territory] = known = [];

        RefreshDiagnostic();

        foreach (var obj in objectTable)
        {
            if (!LooksLikeAetheryte(obj))
                continue;

            var position = obj.Position;
            if (known.Any(a => Vector3.Distance(a.Position, position) <= SamePlaceDistance))
                continue;

            known.Add(new KnownAetheryte
            {
                DataId = obj.BaseId,
                Name = obj.Name.TextValue,
                Position = position,
            });

            dirty = true;
        }
    }

    private void RefreshDiagnostic()
    {
        var player = objectTable.LocalPlayer;
        if (player == null)
            return;

        var entries = new List<(float Distance, string Text)>();
        foreach (var obj in objectTable)
        {
            if (obj.ObjectKind is ObjectKind.Pc or ObjectKind.BattleNpc or ObjectKind.Companion)
                continue;

            var distance = Vector3.Distance(obj.Position, player.Position);
            if (distance > 60f)
                continue;

            var name = obj.Name.TextValue;
            entries.Add((distance,
                $"{obj.ObjectKind} \"{(string.IsNullOrEmpty(name) ? "-" : name)}\" id={obj.BaseId} {distance:F0}y"));
        }

        entries.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        NearbyDiagnostic = entries.Take(20).Select(e => e.Text).ToList();
    }

    /// Records the teleport-window name for whichever shard the player is standing at.
    public void LearnName(Vector3 playerPosition, string aethernetName)
    {
        if (string.IsNullOrWhiteSpace(aethernetName))
            return;

        var shard = Nearest(playerPosition);
        if (shard == null || Vector3.Distance(shard.Position, playerPosition) > 20f)
            return;

        if (shard.AethernetName == aethernetName)
            return;

        shard.AethernetName = aethernetName;
        dirty = true;
        SaveNow();
    }

    /// The zone's main aetheryte - where Occult Return drops you - or null if it can't be told apart from the
    /// shards.
    private const uint NorthHornBaseCamp = 2015429;

    public KnownAetheryte? MainAetheryte
    {
        get
        {
            foreach (var shard in Current)
            {
                if (shard.DataId == NorthHornBaseCamp)
                    return shard;
            }

            var byName = new Dictionary<string, List<KnownAetheryte>>();
            foreach (var shard in Current)
            {
                if (!byName.TryGetValue(shard.Name, out var group))
                    byName[shard.Name] = group = [];

                group.Add(shard);
            }

            if (byName.Count < 2)
                return null;

            KnownAetheryte? only = null;
            foreach (var group in byName.Values)
            {
                if (group.Count != 1)
                    continue;

                if (only != null)
                    return null;

                only = group[0];
            }

            return only;
        }
    }

    /// The shard closest to a point, or null if none are known.
    public KnownAetheryte? Nearest(Vector3 to)
    {
        KnownAetheryte? best = null;
        var bestDistance = float.MaxValue;

        foreach (var shard in Current)
        {
            var distance = Vector3.Distance(shard.Position, to);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = shard;
            }
        }

        return best;
    }

    private static string PathFor(string directory, uint territory) =>
        Path.Combine(directory, "aetherytes", $"{territory}.json");

    private static List<KnownAetheryte> Load(string directory, uint territory)
    {
        try
        {
            var file = PathFor(directory, territory);
            if (File.Exists(file))
                return JsonConvert.DeserializeObject<List<KnownAetheryte>>(File.ReadAllText(file)) ?? [];
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, $"[EchoNav] Could not load aetherytes for territory {territory}");
        }

        return [];
    }

    public void SaveNow()
    {
        if (!dirty || !byTerritory.TryGetValue(loadedTerritory, out var known))
            return;

        try
        {
            var file = PathFor(configDirectory, loadedTerritory);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonConvert.SerializeObject(known));
            dirty = false;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, $"[EchoNav] Could not save aetherytes for territory {loadedTerritory}");
        }
    }
}
