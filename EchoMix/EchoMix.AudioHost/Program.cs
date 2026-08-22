using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EchoMix.AudioHost.Audio;
using EchoMix.AudioHost.Audio.ExternalInput;
using EchoMix.AudioHost.Broadcast;
using EchoMix.AudioHost.Playlists;
using EchoMix.Shared;
using NAudio.Wave;

namespace EchoMix.AudioHost;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--listen-test")
        {
            await RunListenTestAsync(args);
            return;
        }

        if (args.Length > 0 && args[0] == "--spotify-test")
        {
            await RunSpotifyTestAsync();
            return;
        }

        if (args.Length > 0 && args[0] == "--input-test")
        {
            RunInputTest(args);
            return;
        }

        if (args.Length > 0 && args[0] == "--bpm-test")
        {
            RunBpmTest(args);
            return;
        }

        if (args.Length > 0 && args[0] == "--bugreport-test")
        {
            await RunBugReportTestAsync();
            return;
        }

        if (args.Length > 0 && args[0] == "--seed-shows")
        {
            await RunSeedShowsAsync();
            return;
        }

        if (args.Length > 0 && args[0] == "--query-shows")
        {
            await RunQueryShowsAsync();
            return;
        }

        var libraryRoot = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
            ? args[0]
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "XIVLauncher", "pluginConfigs", "EchoMix.Plugin");

        TryEnableFileLogging(libraryRoot);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Console.Error.WriteLine($"[EchoMix.AudioHost] FATAL - unhandled exception (terminating={e.IsTerminating}): {e.ExceptionObject}");
        };

        Console.WriteLine("[EchoMix.AudioHost] Starting...");

        int? ownerProcessId = args.Length > 1 && int.TryParse(args[1], out var parsedOwnerProcessId) ? parsedOwnerProcessId : null;
        var pipeName = ownerProcessId.HasValue ? PipeNaming.ForProcess(ownerProcessId.Value) : "EchoMixAudioHost_dev";

        var mixer = new MixerEngine();
        mixer.Start();

        var playlists = new PlaylistManager(libraryRoot);
        var soundPads = new SoundPadManager(libraryRoot);
        var deckQueues = new DeckQueueManager();

        using var shutdownCts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            shutdownCts.Cancel();
        };

        var server = new IpcServer(pipeName, mixer, playlists, soundPads, deckQueues, libraryRoot, ownerProcessId);
        await server.RunAsync(shutdownCts.Token);

        mixer.Dispose();
        Console.WriteLine("[EchoMix.AudioHost] Stopped.");
    }

    private const long MaxLogFileBytes = 2 * 1024 * 1024;

    /// Best-effort - if the directory can't be created or the file can't be opened (odd permissions, disk
    /// full), this just leaves Console as-is rather than crashing AudioHost over a logging nicety.
    private static void TryEnableFileLogging(string libraryRoot)
    {
        try
        {
            Directory.CreateDirectory(libraryRoot);
            var logPath = Path.Combine(libraryRoot, "audiohost.log");
            TrimLogIfTooLarge(logPath);

            var fileStream = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            var fileWriter = new StreamWriter(fileStream, Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(new DualTextWriter(Console.Out, fileWriter));
            Console.SetError(new DualTextWriter(Console.Error, fileWriter));

            fileWriter.WriteLine();
            fileWriter.WriteLine($"===== EchoMix.AudioHost session started {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EchoMix.AudioHost] Couldn't set up file logging: {ex.Message}");
        }
    }

    /// Keeps only the most recent MaxLogFileBytes worth of content, run before this session's own writer
    /// opens the file - a report needs recent history, not a multi-week archive, and letting an appended log
    /// grow forever would eventually make every bug report's LogContent (and the Discord upload it rides in
    /// on) unreasonably large.
    private static void TrimLogIfTooLarge(string logPath)
    {
        if (!File.Exists(logPath) || new FileInfo(logPath).Length <= MaxLogFileBytes)
            return;

        string tail;
        using (var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            stream.Seek(-MaxLogFileBytes, SeekOrigin.End);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            tail = reader.ReadToEnd();
        }

        var firstNewline = tail.IndexOf('\n');
        if (firstNewline >= 0 && firstNewline < tail.Length - 1)
            tail = tail[(firstNewline + 1)..];

        File.WriteAllText(logPath, tail, Encoding.UTF8);
    }

    /// Mirrors everything written to it into two underlying writers - lets every existing Console.WriteLine
    /// call site keep working unchanged while also landing in a plain text file on disk.
    private sealed class DualTextWriter(TextWriter first, TextWriter second) : TextWriter
    {
        private readonly object gate = new();

        public override Encoding Encoding => first.Encoding;

        public override void Write(char value)
        {
            lock (gate)
            {
                first.Write(value);
                second.Write(value);
            }
        }

        public override void Write(string? value)
        {
            lock (gate)
            {
                first.Write(value);
                second.Write(value);
            }
        }

        public override void WriteLine(string? value)
        {
            var stamped = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {value}";
            lock (gate)
            {
                first.WriteLine(stamped);
                second.WriteLine(stamped);
            }
        }
    }

    private static async Task RunListenTestAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: EchoMix.AudioHost.exe --listen-test <roomCode> [password]");
            return;
        }

        var roomCode = args[1];
        var password = args.Length > 2 ? args[2] : string.Empty;

        Console.WriteLine($"[ListenTest] Connecting to relay {RelayConfig.DefaultHost}:{RelayConfig.DefaultPort}, room {roomCode}...");

        var client = new BroadcastListenClient();
        var connected = await client.ConnectAsync(roomCode, password, "ListenTest");
        if (!connected)
        {
            Console.WriteLine($"[ListenTest] Failed to join: {client.LastError}");
            return;
        }

        Console.WriteLine($"[ListenTest] Joined! DJ: \"{client.HostDjName}\", proximity mode: {client.IsProximityAudio}");
        Console.WriteLine("[ListenTest] Playing through the default output device. Press Ctrl+C to stop.");

        using var shutdownCts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            shutdownCts.Cancel();
        };

        try
        {
            while (client.IsConnected)
            {
                Console.Write($"\r[ListenTest] Connected - A: {client.NowPlayingTitleA ?? "(none)"} | B: {client.NowPlayingTitleB ?? "(none)"}          ");
                await Task.Delay(500, shutdownCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }

        Console.WriteLine();
        if (!client.IsConnected && client.LastError != null)
            Console.WriteLine($"[ListenTest] Disconnected: {client.LastError}");

        await client.DisposeAsync();
        Console.WriteLine("[ListenTest] Stopped.");
    }

    /// One fake DJ's worth of RegisterHost fields - CharacterName must exactly match a profile already seeded
    /// in dj-profiles.json (see the dev fixture batch) for RelayServer.FindLiveRoomFor to actually link the
    /// two, which is the whole point: making a specific DJ List card light up as live rather than just any
    /// anonymous room existing.
    private sealed record SeedShow(
        string DjName, string CharacterName, string RoomCode, bool IsProximityAudio,
        string? ShowName, string? VenueName, string? VenueDataCenter, string? VenueWorld,
        string? VenueHousingArea, string? VenueWard, string? VenuePlot);

    private static readonly SeedShow[] SeedShowFixtures =
    {
        new("Nova Pulse", "Nova Stardust", "SEED1", true,
            "Nova's Neon Rave", "The Ruby Bazaar", "Aether", "Cactuar", "The Lavender Beds", "5", "12"),
        new("DJ Brimstone", "Brimstone Ashgrave", "SEED2", false,
            "Brimstone's Wall of Sound", null, null, null, null, null, null),
        new("Glitch Oracle", "Oracle Glitchfeather", "SEED3", true,
            "Fractal Frequencies", "The Fractal Den", "Crystal", "Balmung", "Mist", "10", "3"),
        new("Bassline Bandit", "Bandit Coldiron", "SEED4", false,
            "Bandit's Bass Bunker", null, null, null, null, null, null),
    };

    private static async Task RunSeedShowsAsync()
    {
        Console.WriteLine($"[SeedShows] Registering {SeedShowFixtures.Length} fake live shows against {RelayConfig.DefaultHost}:{RelayConfig.DefaultPort}...");

        var connections = new List<BroadcastHostConnection>();
        foreach (var show in SeedShowFixtures)
        {
            var connection = new BroadcastHostConnection();
            var ok = await connection.StartAsync(
                roomCode: show.RoomCode, isCoHostJoin: false, password: "seed", hostPassword: "seedhost",
                djName: show.DjName, characterName: show.CharacterName,
                isProximityAudio: show.IsProximityAudio, proximityRange: 30f,
                isPubliclyListed: true, showName: show.ShowName,
                venueName: show.VenueName, venueDataCenter: show.VenueDataCenter, venueWorld: show.VenueWorld,
                venueHousingArea: show.VenueHousingArea, venueWard: show.VenueWard, venuePlot: show.VenuePlot);

            if (ok)
            {
                Console.WriteLine($"[SeedShows] \"{show.DjName}\" is live - room {show.RoomCode}.");
                connections.Add(connection);
            }
            else
            {
                Console.WriteLine($"[SeedShows] \"{show.DjName}\" failed to register.");
                await connection.DisposeAsync();
            }
        }

        if (connections.Count == 0)
        {
            Console.WriteLine("[SeedShows] Nothing registered - exiting.");
            return;
        }

        Console.WriteLine($"[SeedShows] {connections.Count} show(s) live. Press Ctrl+C to end them.");

        using var shutdownCts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            shutdownCts.Cancel();
        };

        try
        {
            await Task.Delay(Timeout.Infinite, shutdownCts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        Console.WriteLine("[SeedShows] Ending shows...");
        foreach (var connection in connections)
            await connection.DisposeAsync();
        Console.WriteLine("[SeedShows] Stopped.");
    }

    private static async Task RunBugReportTestAsync()
    {
        Console.WriteLine("[BugReportTest] Submitting a test report through the relay...");

        var report = new SubmitBugReportMessage
        {
            PluginVersion = "test-harness",
            DjName = "Test DJ",
            CharacterName = "Test Character",
            Description = "This is a --bugreport-test smoke test, not a real report.",
            StatusSnapshot = "Broadcasting: false\nListening: false\nSpotify Mode: false",
            LogContent = "[BugReportTest] Sample log content for the test attachment.",
        };

        var (success, error) = await BugReportClient.SubmitAsync(report);
        Console.WriteLine(success
            ? "[BugReportTest] Success - check the Discord channel for the test report."
            : $"[BugReportTest] Failed: {error}");
    }

    private static async Task RunQueryShowsAsync()
    {
        Console.WriteLine("[QueryShows] Requesting the public shows snapshot from the relay...");
        var (success, error, snapshot) = await PublicShowsClient.RequestAsync();
        if (!success || snapshot == null)
        {
            Console.WriteLine($"[QueryShows] Failed: {error}");
            return;
        }

        Console.WriteLine($"[QueryShows] {snapshot.Shows.Count} show(s) listed:");
        foreach (var show in snapshot.Shows)
        {
            Console.WriteLine(
                $"  RoomCode={show.RoomCode} ShowName={show.ShowName} DjName={show.DjName} " +
                $"IsVenueShow={show.IsVenueShow} HostCharacterName='{show.HostCharacterName}' " +
                $"ProximityRange={show.ProximityRange} HasPassword={show.HasPassword}");
        }
    }

    private static async Task RunSpotifyTestAsync()
    {
        Console.WriteLine("[SpotifyTest] Polling Windows' System Media Transport Controls for Spotify every second. Press Ctrl+C to stop.");

        using var shutdownCts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            shutdownCts.Cancel();
        };

        var reader = new Spotify.SpotifyNowPlayingReader();
        try
        {
            while (true)
            {
                var result = await reader.PollAsync();
                if (result == null)
                    Console.WriteLine("[SpotifyTest] No Spotify session found (Spotify closed, or nothing loaded).");
                else
                    Console.WriteLine($"[SpotifyTest] \"{result.Title}\" - {result.Artist} | {result.ProgressMs / 1000.0:F1}s / {result.DurationMs / 1000.0:F1}s | playing={result.IsPlaying}");

                await Task.Delay(1000, shutdownCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }

        Console.WriteLine("[SpotifyTest] Stopped.");
    }

    private static void RunInputTest(string[] args)
    {
        var devices = AudioInputDevices.List();
        if (devices.Count == 0)
        {
            Console.WriteLine("[InputTest] No Windows recording devices found.");
            return;
        }

        if (args.Length < 2 || !int.TryParse(args[1], out var index) || index < 0 || index >= devices.Count)
        {
            Console.WriteLine("[InputTest] Available recording devices:");
            for (var i = 0; i < devices.Count; i++)
                Console.WriteLine($"  [{i}] {devices[i].Name}");
            Console.WriteLine("Usage: EchoMix.AudioHost.exe --input-test <index>");
            return;
        }

        var device = AudioInputDevices.FindById(devices[index].Id);
        if (device == null)
        {
            Console.WriteLine("[InputTest] That device disappeared before capture could start.");
            return;
        }

        Console.WriteLine($"[InputTest] Device ID: {device.ID}");
        using var capture = new ExternalInputCapture(device);
        Console.WriteLine($"[InputTest] Capturing \"{capture.DeviceName}\" ({capture.WaveFormat}). Showing peak level every 500ms. Press Ctrl+C to stop.");
        capture.Start();

        var sampleProvider = capture.WaveProvider.ToSampleProvider();
        var buffer = new float[capture.WaveFormat.SampleRate];        using var shutdownCts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            shutdownCts.Cancel();
        };

        try
        {
            while (!shutdownCts.IsCancellationRequested)
            {
                var read = sampleProvider.Read(buffer, 0, buffer.Length);
                var peak = 0f;
                for (var i = 0; i < read; i++)
                    peak = Math.Max(peak, Math.Abs(buffer[i]));

                Console.Write($"\r[InputTest] running={capture.IsRunning} peak={peak:F3} {new string('#', (int)(peak * 40))}                    ");
                Thread.Sleep(500);
            }
        }
        catch (OperationCanceledException)
        {
        }

        Console.WriteLine();
        Console.WriteLine("[InputTest] Stopped.");
    }

    /// Usage: EchoMix.AudioHost.exe --bpm-test &lt;filepath&gt; [&lt;expectedBpm&gt;] Prints the detected BPM
    /// and beatgrid anchor; if an expected BPM is given, also prints the delta so a batch of known-BPM
    /// reference tracks can be checked by eye before relying on this in the UI.
    private static void RunBpmTest(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: EchoMix.AudioHost.exe --bpm-test <filepath> [<expectedBpm>]");
            return;
        }

        var filePath = args[1];
        Console.WriteLine($"[BpmTest] Analyzing {filePath}...");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = BpmAnalyzer.Analyze(filePath);
        sw.Stop();

        if (result.Bpm is not float bpm)
        {
            Console.WriteLine($"[BpmTest] No confident BPM detected ({sw.ElapsedMilliseconds}ms).");
            return;
        }

        var offsetText = result.BeatGridOffsetSeconds is float offset ? $"{offset:0.000}s" : "unknown";
        Console.WriteLine($"[BpmTest] Detected {bpm:0.0} BPM, beatgrid anchor {offsetText} ({sw.ElapsedMilliseconds}ms).");
        if (args.Length > 2 && float.TryParse(args[2], out var expected))
            Console.WriteLine($"[BpmTest] Expected {expected:0.0} BPM - delta {bpm - expected:+0.0;-0.0}.");
    }
}
