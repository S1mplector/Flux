using System;
using System.Collections.Generic;
using Equalizer.Application.Abstractions;
using NAudio.CoreAudioApi;

namespace Equalizer.Infrastructure.Audio;

public sealed class AudioDeviceProvider : IAudioDeviceProvider
{
    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices()
    {
        var result = new List<AudioDeviceInfo>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var defaultId = defaultDevice?.ID;

            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                try
                {
                    var id = device.ID;
                    var name = device.FriendlyName;
                    var isDefault = string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase);
                    result.Add(new AudioDeviceInfo(id, name, isDefault));
                }
                catch
                {
                    // Skip devices that can't be queried
                }
            }
        }
        catch
        {
            // Return empty list if enumeration fails
        }
        return result;
    }
}
