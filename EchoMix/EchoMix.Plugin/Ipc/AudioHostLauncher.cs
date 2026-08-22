using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace EchoMix.Plugin.Ipc;

/// Finds and launches the companion EchoMix.AudioHost process if nothing's already listening on its pipe.
public static class AudioHostLauncher
{
    private const int MaxLaunchAttempts = 4;
    private static readonly TimeSpan RelaunchBackoff = TimeSpan.FromMilliseconds(500);

    public static async Task ConnectOrLaunchAsync(
        AudioHostClient client, string pluginAssemblyDirectory, string pluginConfigDirectory, IPluginLog log)
    {
        if (await client.TryConnectAsync(TimeSpan.FromMilliseconds(300)))
            return;

        var exePath = ResolveAudioHostPath(pluginAssemblyDirectory, log);
        if (exePath == null)
            return;

        for (var launchAttempt = 1; launchAttempt <= MaxLaunchAttempts; launchAttempt++)
        {
            Process? process;
            try
            {
                log.Debug($"[EchoMix] Launching AudioHost (attempt {launchAttempt}/{MaxLaunchAttempts}): {exePath}");
                process = Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"\"{pluginConfigDirectory}\" {Environment.ProcessId}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
            }
            catch (Exception ex)
            {
                log.Error(ex, "[EchoMix] Failed to launch AudioHost.exe");
                return;
            }

            var sw = Stopwatch.StartNew();
            var connected = false;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                if (await client.TryConnectAsync(TimeSpan.FromMilliseconds(500)))
                {
                    connected = true;
                    client.HostProcess = process;
                    break;
                }

                if (process != null && process.HasExited)
                    break;

                await Task.Delay(250);
            }

            if (connected)
                return;

            var stillRunning = process != null && !process.HasExited;
            log.Warning(
                $"[EchoMix] AudioHost's pipe never came up within {sw.ElapsedMilliseconds}ms of launching " +
                $"(attempt {launchAttempt}/{MaxLaunchAttempts}){(stillRunning ? " and it's still running - likely stuck behind a Wine crash dialog" : "")}.");

            if (stillRunning)
            {
                try
                {
                    process!.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    log.Debug($"[EchoMix] Couldn't kill the stuck AudioHost process (probably already gone): {ex.Message}");
                }
            }

            if (launchAttempt < MaxLaunchAttempts)
            {
                log.Warning("[EchoMix] Retrying AudioHost launch.");
                await Task.Delay(RelaunchBackoff);
            }
        }

        log.Warning("[EchoMix] Gave up trying to connect to AudioHost after launch.");
    }

    private static string? ResolveAudioHostPath(string pluginAssemblyDirectory, IPluginLog log)
    {
        var path = Path.Combine(pluginAssemblyDirectory, "AudioHost", "EchoMix.AudioHost.exe");
        if (File.Exists(path))
            return path;

        log.Error($"[EchoMix] AudioHost.exe not found at expected path: {path}");
        return null;
    }
}
