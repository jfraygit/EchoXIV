using System.Collections.Generic;
using NAudio.CoreAudioApi;

namespace EchoMix.AudioHost.Audio.ExternalInput;

/// Lists Windows recording (input) devices for External Input Mode's device picker - a physical hardware
/// mixer/audio interface plugged into the DJ's PC, or a virtual audio cable's recording endpoint mirroring
/// another app's output, shows up here the same way a microphone would.
public static class AudioInputDevices
{
    public sealed record Entry(string Id, string Name);

    public static List<Entry> List()
    {
        var result = new List<Entry>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                result.Add(new Entry(device.ID, device.FriendlyName));
                device.Dispose();
            }
        }
        catch
        {
        }

        return result;
    }

    /// Looks the device back up by its stable ID right before capture starts, rather than caching an MMDevice
    /// from an earlier List() call - devices can be unplugged/replugged between when the DJ picked one and
    /// when they actually toggle the mode on.
    public static MMDevice? FindById(string deviceId)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                if (device.ID == deviceId)
                    return device;
                device.Dispose();
            }
        }
        catch
        {
        }

        return null;
    }
}
