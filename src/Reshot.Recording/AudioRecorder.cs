using System.Diagnostics;
using NAudio.CoreAudioApi;
using Reshot.Core.Diagnostics;

namespace Reshot.Recording;

/// <summary>
/// Standalone audio recorder: captures system and/or microphone audio via
/// <see cref="AudioCaptureMixer"/> and encodes it to an M4A on a background thread.
/// Independent of screen capture — the audio-recording tool.
/// </summary>
public sealed class AudioRecorder : IDisposable
{
    private const int MaxChunkFrames = 4800; // ~100 ms at 48 kHz
    private const int Bitrate = 192_000;

    private readonly AudioCaptureMixer _mixer;
    private readonly M4aAudioEncoder _encoder;
    private readonly Thread _thread;
    private volatile bool _stop;
    private bool _disposed;

    public string Path { get; }

    public AudioRecorder(string path, AudioSources sources)
    {
        Path = path;
        _mixer = new AudioCaptureMixer(sources);
        _encoder = new M4aAudioEncoder(path, AudioCaptureMixer.SampleRate, AudioCaptureMixer.Channels, Bitrate);
        _thread = new Thread(Loop) { IsBackground = true, Name = "reshot-audiorec" };
        _thread.Start();
    }

    /// <summary>True when at least one audio source is actually capturing.</summary>
    public bool HasSource => _mixer.HasAnySource;

    private void Loop()
    {
        var pcm = new byte[MaxChunkFrames * AudioCaptureMixer.Channels * 2];
        try
        {
            _mixer.Flush();
            var clock = Stopwatch.StartNew();
            long written = 0;
            while (!_stop)
            {
                var target = (long)(clock.Elapsed.TotalSeconds * AudioCaptureMixer.SampleRate);
                var need = target - written;
                if (need >= 480)
                {
                    var chunk = (int)Math.Min(need, MaxChunkFrames);
                    var bytes = _mixer.ReadPcm16(pcm, chunk);
                    _encoder.WriteAudio(pcm, bytes);
                    written += chunk;
                }
                Thread.Sleep(8);
            }
        }
        catch (Exception ex)
        {
            Log.Error("AudioRecorder: loop failed", ex);
        }
    }

    public void Stop()
    {
        _stop = true;
        _thread.Join(TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
        _encoder.Dispose(); // finalizes the M4A
        _mixer.Dispose();
    }

    /// <summary>Active microphone (capture) devices as (id, friendly name) for settings UI.</summary>
    public static IReadOnlyList<(string Id, string Name)> ListMicrophones()
    {
        var list = new List<(string, string)>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                list.Add((device.ID, device.FriendlyName));
        }
        catch (Exception ex)
        {
            Log.Warn($"Audio: could not enumerate microphones: {ex.Message}");
        }
        return list;
    }
}
