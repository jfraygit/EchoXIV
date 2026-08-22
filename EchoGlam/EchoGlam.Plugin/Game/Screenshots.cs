using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace EchoGlam.Game;

/// Takes a screenshot, and finds the ones already taken.
public sealed class Screenshots
{
    /// Whether the plugin's windows should stay out of the picture.
    private volatile bool hiding;

    public bool Hiding => hiding;

    /// Called once per frame by the window.
    public bool ShouldDraw() => !hiding;

    /// Where FFXIV writes screenshots.
    public static string Directory
    {
        get
        {
            if (resolved is not null)
                return resolved;

            foreach (var candidate in Candidates())
            {
                if (!System.IO.Directory.Exists(candidate))
                    continue;

                resolved = candidate;
                return resolved;
            }

            return Candidates().First();
        }
    }

    private static string? resolved;

    private static IEnumerable<string> Candidates()
    {
        const string tail = @"My Games\FINAL FANTASY XIV - A Realm Reborn\screenshots";

        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), tail);

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        yield return Path.Combine(profile, "Documents", tail);
        yield return Path.Combine(profile, "OneDrive", "Documents", tail);
    }

    public static bool DirectoryExists => System.IO.Directory.Exists(Directory);

    /// The most recent screenshots, newest first.
    public static List<string> Recent(int count)
    {
        try
        {
            if (!DirectoryExists)
                return [];

            return
            [
                .. new DirectoryInfo(Directory)
                    .EnumerateFiles()
                    .Where(f => f.Extension is ".png" or ".jpg" or ".jpeg" or ".PNG" or ".JPG" or ".JPEG")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Take(count)
                    .Select(f => f.FullName),
            ];
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[EchoGlam] Could not read the screenshots folder: {ex.Message}");
            return [];
        }
    }

    private static DateTime NewestWriteTime()
    {
        try
        {
            if (!DirectoryExists)
                return DateTime.MinValue;

            return new DirectoryInfo(Directory)
                .EnumerateFiles()
                .Select(f => f.LastWriteTimeUtc)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();
        }
        catch (Exception)
        {
            return DateTime.MinValue;
        }
    }

    /// Hides the plugin, captures the game window, and returns a file holding the picture.
    public async Task<(string? Path, string? Problem)> CaptureAsync()
    {
        hiding = true;

        var uiWasVisible = await Plugin.Framework.RunOnTick(() => SetGameUi(false)).ConfigureAwait(false);

        try
        {
            await Task.Delay(450).ConfigureAwait(false);

            byte[]? png = null;
            string? problem = null;

            await Plugin.Framework.RunOnTick(() => png = WindowCapture.Capture(out problem)).ConfigureAwait(false);

            if (png is null)
            {
                Plugin.Log.Warning($"[EchoGlam] Screenshot: {problem}");
                return (null, problem ?? "The screenshot couldn't be taken.");
            }

            var path = Path.Combine(
                Path.GetTempPath(), $"echoglam-capture-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png");

            await File.WriteAllBytesAsync(path, png).ConfigureAwait(false);

            Plugin.Log.Information($"[EchoGlam] Screenshot: captured {png.Length / 1024}KB to {path}.");
            return (path, null);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[EchoGlam] Screenshot capture failed");
            return (null, $"The screenshot couldn't be taken: {ex.Message}");
        }
        finally
        {
            hiding = false;

            if (uiWasVisible)
                await Plugin.Framework.RunOnTick(() => SetGameUi(true)).ConfigureAwait(false);
        }
    }

    /// Turns the game's interface on or off, returning whether it had been on.
    private static unsafe bool SetGameUi(bool visible)
    {
        try
        {
            var module = FFXIVClientStructs.FFXIV.Client.UI.RaptureAtkModule.Instance();
            if (module == null)
                return false;

            var was = module->IsUiVisible;
            module->IsUiVisible = visible;
            return was;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"[EchoGlam] Couldn't toggle the game UI: {ex.Message}");
            return false;
        }
    }
}
