using Newtonsoft.Json.Linq;

namespace EchoMix.Shared;

/// Wraps every message crossing the pipe.
public sealed class IpcEnvelope
{
    public string Type { get; set; } = string.Empty;
    public JObject Payload { get; set; } = new();

    public static IpcEnvelope For<T>(string type, T payload) =>
        new() { Type = type, Payload = JObject.FromObject(payload!) };

    public T ReadPayload<T>() => Payload.ToObject<T>()!;
}
