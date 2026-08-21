using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Newtonsoft.Json;

namespace EchoNav.Nav;

/// Records where the character walks, so the mesh can be told about crossings it missed.
public sealed class TrailRecorder
{
    /// How far the character has to move before another point is kept.
    private const float SampleDistance = 2f;

    private readonly string directory;
    private readonly List<Node> nodes = [];
    private Vector3 last;
    private bool hasLast;

    public TrailRecorder(string configDirectory) => directory = Path.Combine(configDirectory, "waypoints");

    public bool IsRecording { get; private set; }

    /// Points added since recording started.
    public int Recorded { get; private set; }

    public string Detail { get; private set; } = string.Empty;

    /// Starts, loading whatever has already been gathered for this zone so the new walk extends the trail
    /// rather than replacing it.
    public void Start(uint territory)
    {
        nodes.Clear();
        Recorded = 0;
        hasLast = false;

        try
        {
            var file = PathFor(territory);
            if (File.Exists(file))
            {
                var existing = JsonConvert.DeserializeObject<Trail>(File.ReadAllText(file));
                if (existing?.Nodes != null)
                    nodes.AddRange(existing.Nodes);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[EchoNav] Could not load the existing trail; starting from nothing");
        }

        IsRecording = true;
        Detail = $"recording, {nodes.Count} points already on file";
    }

    public void Stop(uint territory)
    {
        IsRecording = false;
        Save(territory);
    }

    /// Framework thread.
    public void Tick(Vector3 playerPosition)
    {
        if (!IsRecording)
            return;

        if (hasLast && Vector3.Distance(playerPosition, last) < SampleDistance)
            return;

        var index = nodes.Count;
        nodes.Add(new Node { Position = playerPosition, Links = [] });

        if (hasLast && index > 0)
        {
            nodes[index].Links.Add(index - 1);
            nodes[index - 1].Links.Add(index);
        }

        last = playerPosition;
        hasLast = true;
        Recorded++;
        Detail = $"recording, {Recorded} new points ({nodes.Count} total)";
    }

    private void Save(uint territory)
    {
        try
        {
            var file = PathFor(territory);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonConvert.SerializeObject(new Trail { Nodes = nodes }));
            Detail = $"saved {nodes.Count} points to {file}";
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[EchoNav] Could not save the trail");
            Detail = $"could not save: {ex.Message}";
        }
    }

    private string PathFor(uint territory) => Path.Combine(directory, $"{territory}.json");

    private sealed class Trail
    {
        public List<Node> Nodes { get; set; } = [];
    }

    private sealed class Node
    {
        public Vector3 Position { get; set; }
        public List<int> Links { get; set; } = [];
    }
}
