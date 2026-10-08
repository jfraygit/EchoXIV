using System;

namespace EchoMix.AudioHost.Audio;

/// Logging shim for the two per-app audio routing helpers, which were ported from the AmbitionFM mod (see
/// AudioEndpointRouter's own doc comment for the technique and its caveats).
internal static class Log
{
    public static void Info(string message) => Console.WriteLine($"[EchoMix.AudioHost] {message}");

    public static void Warn(string message) => Console.WriteLine($"[EchoMix.AudioHost] WARN: {message}");

    public static void Error(string message, Exception? ex = null) =>
        Console.WriteLine($"[EchoMix.AudioHost] ERROR: {message}{(ex == null ? string.Empty : $" - {ex}")}");
}
