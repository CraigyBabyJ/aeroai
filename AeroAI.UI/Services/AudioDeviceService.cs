using System.Collections.Generic;
using NAudio.CoreAudioApi;

namespace AeroAI.UI.Services;

public sealed record AudioDeviceOption(string Id, string Name);

public sealed class AudioDeviceService
{
    public IReadOnlyList<AudioDeviceOption> GetInputDevices()
    {
        var list = new List<AudioDeviceOption>();
        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            list.Add(new AudioDeviceOption(device.ID, device.FriendlyName));
        }
        return list;
    }

    public IReadOnlyList<AudioDeviceOption> GetOutputDevices()
    {
        var list = new List<AudioDeviceOption>();
        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            list.Add(new AudioDeviceOption(device.ID, device.FriendlyName));
        }
        return list;
    }
}
