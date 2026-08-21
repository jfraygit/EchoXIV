using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace EchoNav.Nav;

/// Reads the game's aethernet window.
public sealed unsafe class AethernetMenu
{
    /// Addon name for the aethernet window, confirmed live.
    private const string AddonName = "TelepotTown";

    /// Where the addon reports how many destinations it's showing.
    private const int DestinationCountIndex = 3;

    /// Prefix before the current location in an English client.
    private const string CurrentLocationMarker = "Current Location:";

    public string OpenAddonName { get; private set; } = string.Empty;

    public bool IsOpen => !string.IsNullOrEmpty(OpenAddonName);

    /// Name of the shard the player is standing at, read straight off the window.
    public string CurrentLocation { get; private set; } = string.Empty;

    /// Destination names, best effort, for display only.
    public IReadOnlyList<string> Destinations { get; private set; } = [];

    public IReadOnlyList<string> NodeDump { get; private set; } = [];
    public IReadOnlyList<string> ValueDump { get; private set; } = [];

    /// NOTHING IN THIS CLASS CALLS A METHOD ON A COMPONENT.
    private const int ListComponentNodeType = 1022;

    /// Rows as the list reports them, index and label.
    public IReadOnlyList<(int Row, string Label)> ListRows { get; private set; } = [];

    /// What the last read or selection did, for diagnosis without guesswork.
    public string LastSelectResult { get; private set; } = string.Empty;

    /// Row labels read out of the node tree, in the order they're laid out.
    public IReadOnlyList<string> ReadRowLabels()
    {
        var labels = new List<string>();

        try
        {
            var handle = Plugin.GameGui.GetAddonByName(AddonName);
            if (handle.Address == IntPtr.Zero)
            {
                LastSelectResult = "window isn't open";
                return labels;
            }

            var addon = (AtkUnitBase*)handle.Address;
            for (var i = 0; i < addon->UldManager.NodeListCount; i++)
            {
                var node = addon->UldManager.NodeList[i];
                if (node == null || (int)node->Type != ListComponentNodeType)
                    continue;

                var component = ((AtkComponentNode*)node)->Component;
                if (component == null)
                    continue;

                CollectText(&component->UldManager, labels);
            }

            LastSelectResult = $"read {labels.Count} labels from the node tree";
        }
        catch (Exception ex)
        {
            LastSelectResult = $"failed: {ex.Message}";
            Plugin.Log.Warning(ex, "[EchoNav] Could not read the aethernet rows");
        }

        return labels;
    }

    /// Gathers text from a node tree, descending into component nodes.
    private static void CollectText(AtkUldManager* uld, List<string> into, int depth = 0)
    {
        if (uld == null || uld->NodeList == null || depth > 4)
            return;

        for (var i = 0; i < uld->NodeListCount; i++)
        {
            var node = uld->NodeList[i];
            if (node == null)
                continue;

            if (node->Type == NodeType.Text)
            {
                var text = ((AtkTextNode*)node)->NodeText.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                    into.Add(text);
            }
            else if ((int)node->Type > 1000)
            {
                var child = ((AtkComponentNode*)node)->Component;
                if (child != null)
                    CollectText(&child->UldManager, into, depth + 1);
            }
        }
    }

    /// Sends a callback to the addon, the way a completed UI interaction does.
    private const int WorkingCallbackShape = 2;

    /// Gap between the two sends.
    private const long ConfirmDelayMs = 250;

    private int pendingIndex = -1;
    private long confirmAtTick;

    /// True between the highlight and the confirm.
    public bool IsSelecting => pendingIndex >= 0;

    /// Teleports to a row: highlight now, confirm shortly after.
    public bool Select(int rowIndex)
    {
        if (!TryCallback(WorkingCallbackShape, rowIndex))
            return false;

        pendingIndex = rowIndex;
        confirmAtTick = Environment.TickCount64 + ConfirmDelayMs;
        LastSelectResult = $"highlighted row {rowIndex}, confirming shortly";
        return true;
    }

    /// Sends the confirming second callback once the delay is up.
    public void Tick()
    {
        if (pendingIndex < 0 || Environment.TickCount64 < confirmAtTick)
            return;

        var index = pendingIndex;
        pendingIndex = -1;

        if (TryCallback(WorkingCallbackShape, index))
            LastSelectResult = $"confirmed row {index}";
    }

    public void CancelSelection() => pendingIndex = -1;

    public bool TryCallback(int shape, int index)
    {
        try
        {
            var handle = Plugin.GameGui.GetAddonByName(AddonName);
            if (handle.Address == IntPtr.Zero)
            {
                LastSelectResult = "window isn't open";
                return false;
            }

            var addon = (AtkUnitBase*)handle.Address;
            var values = stackalloc AtkValue[2];

            switch (shape)
            {
                case 0:
                    values[0].Type = AtkValueType.Int;
                    values[0].Int = index;
                    addon->FireCallback(1, values);
                    break;
                case 1:
                    values[0].Type = AtkValueType.Int;
                    values[0].Int = 0;
                    values[1].Type = AtkValueType.Int;
                    values[1].Int = index;
                    addon->FireCallback(2, values);
                    break;
                case 2:
                    values[0].Type = AtkValueType.Int;
                    values[0].Int = 11;
                    values[1].Type = AtkValueType.Int;
                    values[1].Int = index;
                    addon->FireCallback(2, values);
                    break;
                default:
                    values[0].Type = AtkValueType.Int;
                    values[0].Int = index;
                    addon->FireCallback(1, values, true);
                    break;
            }

            LastSelectResult = $"sent callback shape {shape} with index {index}";
            return true;
        }
        catch (Exception ex)
        {
            LastSelectResult = $"failed: {ex.Message}";
            Plugin.Log.Warning(ex, "[EchoNav] Aethernet callback failed");
            return false;
        }
    }

    public void Refresh()
    {
        OpenAddonName = string.Empty;
        CurrentLocation = string.Empty;
        Destinations = [];
        NodeDump = [];
        ValueDump = [];

        try
        {
            var handle = Plugin.GameGui.GetAddonByName(AddonName);
            if (handle.Address == IntPtr.Zero)
                return;

            var addon = (AtkUnitBase*)handle.Address;
            if (!addon->IsVisible)
                return;

            OpenAddonName = AddonName;
            NodeDump = DumpText(addon);
            ValueDump = DumpValues(addon);
            CurrentLocation = ReadCurrentLocation(NodeDump);
            Destinations = ReadDestinations(addon);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[EchoNav] Could not read the aethernet window");
        }
    }

    /// The name of the shard being stood at, read off the window.
    private static string ReadCurrentLocation(IReadOnlyList<string> nodeDump)
    {
        foreach (var line in nodeDump)
        {
            var marker = line.IndexOf(CurrentLocationMarker, StringComparison.OrdinalIgnoreCase);
            if (marker < 0)
                continue;

            var named = Clean(line[(marker + CurrentLocationMarker.Length)..]);
            if (named.Length > 0)
                return named;
        }

        var last = string.Empty;

        foreach (var line in nodeDump)
        {
            var text = Quoted(line);
            if (text.Length == 0)
                continue;

            var colon = text.IndexOf(':');
            if (colon < 0 || text.IndexOf(':', colon + 1) >= 0)
                continue;

            var candidate = text[(colon + 1)..].Trim();
            if (candidate.Length > 0)
                last = candidate;
        }

        return last;
    }

    /// The text a dumped node line carries, which the dump wraps in quotes after the node index.
    private static string Quoted(string line)
    {
        var open = line.IndexOf('"');
        if (open < 0)
            return string.Empty;

        var close = line.LastIndexOf('"');
        return close > open ? line[(open + 1)..close] : string.Empty;
    }

    /// Trims a dumped value down to the place name: the text up to the closing quote.
    private static string Clean(string value)
    {
        var trimmed = value.Trim();

        var quote = trimmed.IndexOf('"');
        if (quote >= 0)
            trimmed = trimmed[..quote];

        return trimmed.Trim();
    }

    /// Destination names, in the order the window lists them.
    private static List<string> ReadDestinations(AtkUnitBase* addon)
    {
        var names = new List<string>();
        if (addon->AtkValues == null)
            return names;

        var strings = new List<string>();
        for (var i = 0; i < addon->AtkValuesCount; i++)
        {
            var value = addon->AtkValues[i];
            if (value.Type is not (AtkValueType.String or AtkValueType.ManagedString or AtkValueType.ConstString))
                continue;

            if (value.String.Value == null)
                continue;

            var text = Dalamud.Memory.MemoryHelper.ReadStringNullTerminated((nint)value.String.Value);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            if (text.Contains(':'))
                continue;

            strings.Add(text);
        }

        if (strings.Count > 1)
            names.AddRange(strings.GetRange(1, strings.Count - 1));

        return names;
    }

    private static List<string> DumpValues(AtkUnitBase* addon)
    {
        var lines = new List<string>();
        if (addon->AtkValues == null)
            return lines;

        for (var i = 0; i < addon->AtkValuesCount; i++)
        {
            var value = addon->AtkValues[i];
            var text = value.Type switch
            {
                AtkValueType.String or AtkValueType.ManagedString or AtkValueType.ConstString =>
                    value.String.Value == null
                        ? string.Empty
                        : Dalamud.Memory.MemoryHelper.ReadStringNullTerminated((nint)value.String.Value),
                AtkValueType.Int => value.Int.ToString(),
                AtkValueType.UInt => value.UInt.ToString(),
                AtkValueType.Bool => value.Byte != 0 ? "true" : "false",
                _ => string.Empty,
            };

            if (!string.IsNullOrWhiteSpace(text))
                lines.Add($"[{i}] {value.Type} {text}");
        }

        return lines;
    }

    /// Text of the addon's text nodes.
    private static List<string> DumpText(AtkUnitBase* addon)
    {
        var lines = new List<string>();
        if (addon->UldManager.NodeList == null)
            return lines;

        for (var i = 0; i < addon->UldManager.NodeListCount; i++)
        {
            var node = addon->UldManager.NodeList[i];
            if (node == null || node->Type != NodeType.Text)
                continue;

            var text = ((AtkTextNode*)node)->NodeText.ToString();
            if (!string.IsNullOrWhiteSpace(text))
                lines.Add($"[{i}] text \"{text}\"");
        }

        return lines;
    }
}
