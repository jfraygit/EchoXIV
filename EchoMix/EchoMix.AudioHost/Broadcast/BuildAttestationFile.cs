using System;
using System.IO;
using Newtonsoft.Json;

namespace EchoMix.AudioHost.Broadcast;

/// Reads attestation.json from next to this process's own exe, if it's there - see
/// EchoMix.Shared.BuildAttestation's own doc comment for what the file actually proves and why it only exists
/// on a build produced by tools/release/package.ps1.
public static class BuildAttestationFile
{
    private static readonly Lazy<(string? Version, string? Signature)> Loaded = new(Load);

    public static string? Version => Loaded.Value.Version;
    public static string? Signature => Loaded.Value.Signature;

    private static (string?, string?) Load()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "attestation.json");
            if (!File.Exists(path))
                return (null, null);

            var parsed = JsonConvert.DeserializeObject<Dto>(File.ReadAllText(path));
            return (parsed?.Version, parsed?.Signature);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return (null, null);
        }
    }

    private sealed class Dto
    {
        public string? Version { get; set; }
        public string? Signature { get; set; }
    }
}
