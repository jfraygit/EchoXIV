using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EchoRoleplay.Shared;
using Newtonsoft.Json;

namespace EchoRoleplay.Game;

/// Profiles already downloaded, kept on disk between sessions.
public sealed class ProfileCache
{
    private readonly string directory;

    /// Entries older than this go on startup.
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    /// A ceiling in case somebody spends a month in a crowded venue.
    private const int MaximumEntries = 2000;

    public ProfileCache(string configDirectory)
    {
        directory = Path.Combine(configDirectory, "cache");

        try
        {
            Directory.CreateDirectory(directory);
            Prune();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[EchoRoleplay] The profile cache could not be opened");
        }
    }

    /// The cached profile at exactly this version, or null.
    public ProfileEnvelope? Read(string id, string version)
    {
        var path = PathFor(id, version);

        if (path is null || !File.Exists(path))
            return null;

        try
        {
            return JsonConvert.DeserializeObject<ProfileEnvelope>(File.ReadAllText(path));
        }
        catch (Exception)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception)
            {
            }

            return null;
        }
    }

    public void Write(ProfileEnvelope envelope)
    {
        var path = PathFor(envelope.Id, envelope.Version);

        if (path is null)
            return;

        try
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(envelope));
        }
        catch (Exception)
        {
        }
    }

    /// Where an entry lives, or null if either half of the key is not the shape it must be.
    private string? PathFor(string id, string version)
    {
        if (!Safe(id) || !Safe(version))
            return null;

        return Path.Combine(directory, $"{id}.{version}.json");
    }

    private static bool Safe(string value) =>
        !string.IsNullOrEmpty(value)
        && value.Length <= 64
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private void Prune()
    {
        var files = new DirectoryInfo(directory).GetFiles("*.json");

        if (files.Length == 0)
            return;

        var cutoff = DateTime.UtcNow - Lifetime;
        var survivors = new List<FileInfo>(files.Length);

        foreach (var file in files)
        {
            if (file.LastWriteTimeUtc < cutoff)
                Delete(file);
            else
                survivors.Add(file);
        }

        foreach (var file in survivors.OrderBy(f => f.LastWriteTimeUtc).Take(survivors.Count - MaximumEntries))
            Delete(file);
    }

    private static void Delete(FileInfo file)
    {
        try
        {
            file.Delete();
        }
        catch (Exception)
        {
        }
    }
}
