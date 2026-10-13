using System.Runtime.Versioning;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Tropicast.Station.Audio.Windows;

[SupportedOSPlatform("windows")]
internal sealed class NAudioWasapiBackend : IWasapiBackend
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly MMDeviceNotificationClient _notifications;
    private volatile bool _disposed;

    internal NAudioWasapiBackend()
    {
        // Raised on a COM thread (no synchronization context): Notify only signals, the subscriber schedules the refresh.
        _notifications = _enumerator.CreateNotificationClient(useSynchronizationContext: false);
        _notifications.DeviceAdded += (_, _) => Notify();
        _notifications.DeviceRemoved += (_, _) => Notify();
        _notifications.DeviceStateChanged += (_, _) => Notify();
        _notifications.DefaultDeviceChanged += (_, _) => Notify();
        _notifications.PropertyValueChanged += (_, _) => Notify();
    }

    public event EventHandler? DevicesChanged;

    public IReadOnlyList<AudioDevice> GetDevices()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var devices = new List<AudioDevice>();
        foreach (var flow in new[] { DataFlow.Capture, DataFlow.Render })
        {
            string? defaultId = null;
            if (_enumerator.HasDefaultAudioEndpoint(flow, Role.Multimedia))
            {
                using var defaultDevice = _enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
                defaultId = defaultDevice.ID;
            }

            foreach (var endpoint in _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            {
                using (endpoint)
                using (var client = endpoint.CreateAudioClient())
                {
                    var format = WasapiFormats.Negotiate(client.MixFormat);
                    devices.Add(new(endpoint.ID, endpoint.FriendlyName,
                        flow == DataFlow.Capture ? AudioDeviceKind.Input : AudioDeviceKind.Loopback,
                        endpoint.ID == defaultId, format));
                }
            }
        }

        return devices;
    }

    public bool IsMicrophoneDenied()
    {
        const string consent = @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";
        // Registry is only a preflight hint; WASAPI's actual access-denied HRESULT remains authoritative.
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var microphone = root.OpenSubKey(consent);
            using var desktop = root.OpenSubKey(consent + @"\NonPackaged");
            if (string.Equals(microphone?.GetValue("Value") as string, "Deny", StringComparison.OrdinalIgnoreCase)
                || string.Equals(desktop?.GetValue("Value") as string, "Deny", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public IWasapiSource Open(string deviceId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var device = _enumerator.GetDevice(deviceId);
        WasapiRecorder? recorder = null;
        try
        {
            if (device.State != DeviceState.Active)
            {
                throw new IOException("The selected Windows audio endpoint is no longer active.");
            }

            AudioFormat format;
            using (var client = device.CreateAudioClient())
            {
                format = WasapiFormats.Negotiate(client.MixFormat);
            }
            // Shared mode at the mix rate and channel count, 100 ms buffers and polling, as with NAudio 2's WasapiCapture;
            // the engine converts PCM24/32 to the requested float32 (or keeps PCM16).
            var builder = new WasapiRecorderBuilder()
                .WithDevice(device)
                .WithSharedMode()
                .WithPollingSync()
                .WithBufferLength(100)
                .WithFormat(format.Encoding == PcmEncoding.Signed16
                    ? new WaveFormat(format.SampleRate, 16, format.Channels)
                    : WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, format.Channels));
            if (device.DataFlow == DataFlow.Render)
            {
                builder.WithLoopbackCapture();
            }
            recorder = builder.Build();
            return new NAudioSource(device, recorder, format);
        }
        catch
        {
            recorder?.Dispose();
            device.Dispose();
            throw;
        }
    }

    private void Notify()
    {
        if (!_disposed)
        {
            // The subscriber only schedules refresh; never enumerate, stop or unregister inside a COM callback.
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _notifications.Dispose();
            _enumerator.Dispose();
        }
    }

    private sealed class NAudioSource : IWasapiSource
    {
        private readonly MMDevice _device;
        private readonly WasapiRecorder _recorder;
        // The recorder's span is only valid during its callback: copy it into a reused buffer for the session.
        private byte[] _buffer = [];
        private bool _disposed;

        internal NAudioSource(MMDevice device, WasapiRecorder recorder, AudioFormat format)
        {
            _device = device;
            _recorder = recorder;
            Format = format;
            recorder.DataAvailable += OnData;
            recorder.RecordingStopped += OnStopped;
        }

        public AudioFormat Format { get; }
        public event EventHandler<WasapiDataEventArgs>? DataAvailable;
        public event EventHandler<WasapiStoppedEventArgs>? Stopped;
        public void Start() => _recorder.StartRecording();
        public void Stop() => _recorder.StopRecording();

        private void OnData(ReadOnlySpan<byte> data, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
        {
            var count = WasapiPackets.Copy(data, flags.HasFlag(AudioClientBufferFlags.Silent), ref _buffer);
            DataAvailable?.Invoke(this, new(_buffer, count));
        }

        private void OnStopped(object? sender, StoppedEventArgs e) => Stopped?.Invoke(this, new(e.Exception));

        public void Dispose()
        {
            if (!_disposed)
            {
                _recorder.DataAvailable -= OnData;
                _recorder.RecordingStopped -= OnStopped;
                try
                {
                    _recorder.Dispose();
                }
                finally
                {
                    _device.Dispose();
                    _disposed = true;
                }
            }
        }
    }
}

internal static class WasapiPackets
{
    /// <summary>
    /// Copies a capture packet, valid only during the recorder's callback, into a reused buffer (grown as needed).
    /// A packet flagged silent becomes zeros: WASAPI may leave anything in its data.
    /// </summary>
    /// <returns>The number of bytes copied.</returns>
    internal static int Copy(ReadOnlySpan<byte> data, bool silent, ref byte[] buffer)
    {
        if (buffer.Length < data.Length)
        {
            buffer = new byte[data.Length];
        }
        var target = buffer.AsSpan(0, data.Length);
        if (silent)
        {
            target.Clear();
        }
        else
        {
            data.CopyTo(target);
        }
        return data.Length;
    }
}

internal static class WasapiFormats
{
    /// <summary>
    /// Preserve float32/PCM16 mix formats. Shared WASAPI auto-converts PCM24/32 to float32;
    /// native sample rate/channel count remain unchanged. Resampling/downmix is the shared pipeline's job.
    /// </summary>
    internal static AudioFormat Negotiate(WaveFormat mix)
    {
        ArgumentNullException.ThrowIfNull(mix);
        if (mix is WaveFormatExtensible extensible)
        {
            mix = extensible.ToStandardWaveFormat();
        }

        if (mix.Encoding == WaveFormatEncoding.Pcm && mix.BitsPerSample is 16 or 24 or 32)
        {
            return new(mix.SampleRate, mix.Channels, mix.BitsPerSample == 16 ? PcmEncoding.Signed16 : PcmEncoding.Float32);
        }

        if (mix.Encoding == WaveFormatEncoding.IeeeFloat && mix.BitsPerSample == 32)
        {
            return new(mix.SampleRate, mix.Channels);
        }

        throw new IOException("The Windows audio endpoint has an unsupported mix format. Configure PCM16/24/32 or float32 in its Sound properties.");
    }
}
