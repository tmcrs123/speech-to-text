using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace SpeechToText;

internal readonly record struct AudioInputDevice(string Id, string FriendlyName);

internal static class AudioDevices
{
    public const string SystemDefaultId = "";

    // Re-queries the OS each call so freshly-plugged headsets show up.
    public static IReadOnlyList<AudioInputDevice> EnumerateInputs()
    {
        var list = new List<AudioInputDevice>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                list.Add(new AudioInputDevice(device.ID, device.FriendlyName));
                device.Dispose();
            }
        }
        catch (Exception)
        {
            // Fall back to WaveIn enumeration if the CoreAudio path fails.
            for (int i = 0; i < WaveInEvent.DeviceCount; i++)
            {
                var caps = WaveInEvent.GetCapabilities(i);
                list.Add(new AudioInputDevice(i.ToString(), caps.ProductName));
            }
        }
        return list;
    }

    // Maps an MMDevice id (or numeric WaveIn index, or "" for default) to a
    // WaveInEvent DeviceNumber. -1 means "system default".
    public static int ResolveWaveInDeviceNumber(string? deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) return -1;

        if (int.TryParse(deviceId, out var direct) && direct >= 0 && direct < WaveInEvent.DeviceCount)
            return direct;

        string? friendlyName = null;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(deviceId);
            friendlyName = device.FriendlyName;
        }
        catch (Exception)
        {
            return -1;
        }

        if (friendlyName == null) return -1;

        // WaveIn truncates product names to 32 chars; compare on a prefix.
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var caps = WaveInEvent.GetCapabilities(i);
            if (friendlyName.StartsWith(caps.ProductName, StringComparison.OrdinalIgnoreCase) ||
                caps.ProductName.StartsWith(friendlyName, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }
}
