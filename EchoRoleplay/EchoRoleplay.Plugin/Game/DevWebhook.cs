using System;
using System.IO;

namespace EchoRoleplay.Game;

/// A Discord webhook for development builds, read from a file on this machine.
public static class DevWebhook
{
    /// Where the URL is kept, beside the plugin's own configuration.
    public const string FileName = "dev-webhook.txt";

#if DEBUG
    private static string? cached;
    private static bool looked;
#endif

    /// The development webhook, or empty when there is none - which is the normal case for everybody outside
    /// development.
    public static string Url(string configDirectory)
    {
#if DEBUG
        if (looked)
            return cached ?? string.Empty;

        looked = true;

        try
        {
            var path = Path.Combine(configDirectory, FileName);

            if (File.Exists(path))
            {
                var text = File.ReadAllText(path).Trim();

                if (text.StartsWith("https://discord.com/api/webhooks/", StringComparison.OrdinalIgnoreCase))
                    cached = text;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[EchoRoleplay] Could not read the development webhook");
        }

        return cached ?? string.Empty;
#else
        _ = configDirectory;
        return string.Empty;
#endif
    }
}
