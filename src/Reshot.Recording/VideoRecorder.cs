using System.Diagnostics;
using Reshot.Capture;
using Reshot.Core.Diagnostics;

namespace Reshot.Recording;

/// <summary>
/// Records a screen rectangle to an MP4 (ARCHITECTURE §9). A
/// <see cref="WgcMonitorStream"/> supplies live frames; a video thread pulls the crop at
/// the target fps into a <b>video-only</b> temp MP4, while an audio thread writes each
/// source (system, microphone) to its <b>own</b> raw PCM file.
///
/// Nothing is mixed during capture — <see cref="Finish"/> decides which tracks make it
/// into the final file, which is what lets the user pick tracks after recording. The
/// audio clock starts when the first video frame arrives (and the buffers are flushed
/// then) so the tracks line up.
/// </summary>
public sealed class VideoRecorder : IDisposable
{
    private const int AudioMaxChunkFrames = 4800; // ~100 ms at 48 kHz

    private readonly WgcMonitorStream _stream;
    private readonly Mp4VideoEncoder _encoder;
    private readonly AudioCaptureMixer? _systemAudio;
    private readonly AudioCaptureMixer? _micAudio;
    private FileStream? _systemPcm;
    private FileStream? _micPcm;
    private readonly string _tempVideoPath;
    private readonly string? _systemPcmPath;
    private readonly string? _micPcmPath;
    private readonly Thread _videoThread;
    private readonly Thread? _audioThread;
    private readonly ManualResetEventSlim _started = new(false);
    private readonly int _cropLeft, _cropTop, _cropWidth, _cropHeight, _fps;
    private readonly byte[]? _mask;
    private readonly int _maskStride;
    private volatile bool _stop;
    private bool _stopped;
    private bool _disposed;

    /// <summary>Final output path (only written by <see cref="Finish"/>).</summary>
    public string Path { get; }
    public int Width => _cropWidth;
    public int Height => _cropHeight;

    /// <summary>True if a system-audio track was actually captured.</summary>
    public bool HasSystemTrack => _systemAudio is not null;

    /// <summary>True if a microphone track was actually captured.</summary>
    public bool HasMicTrack => _micAudio is not null;

    public VideoRecorder(
        IScreenCaptureService capture,
        int rectLeft, int rectTop, int rectWidth, int rectHeight,
        int fps, int bitrate, string path,
        AudioSources sources,
        byte[]? shapeMask = null, int maskStride = 0)
    {
        Path = path;
        _fps = fps;
        _mask = shapeMask;
        _maskStride = maskStride;
        _stream = capture.StartMonitorStream(rectLeft, rectTop);

        // Recording crop relative to the captured monitor, clamped to it, even dims.
        var cl = Math.Clamp(rectLeft - _stream.MonitorLeft, 0, Math.Max(0, _stream.Width - 2));
        var ct = Math.Clamp(rectTop - _stream.MonitorTop, 0, Math.Max(0, _stream.Height - 2));
        _cropLeft = cl;
        _cropTop = ct;
        _cropWidth = Math.Max(2, Math.Min(rectWidth, _stream.Width - cl) & ~1);
        _cropHeight = Math.Max(2, Math.Min(rectHeight, _stream.Height - ct) & ~1);

        // Each source is captured on its own, so tracks can be kept or dropped later.
        // Audio is best-effort: a source that won't open just isn't offered at save time.
        if (sources.SystemFull || sources.IncludePids.Length > 0)
        {
            _systemAudio = TryOpen(new AudioSources
            {
                SystemFull = sources.SystemFull,
                IncludePids = sources.IncludePids,
            });
        }
        if (sources.Mic)
            _micAudio = TryOpen(new AudioSources { Mic = true, MicDevice = sources.MicDevice });

        var stamp = Guid.NewGuid().ToString("N")[..8];
        var tempDir = System.IO.Path.GetTempPath();
        _tempVideoPath = System.IO.Path.Combine(tempDir, $"reshot_{stamp}_video.mp4");
        if (_systemAudio is not null)
        {
            _systemPcmPath = System.IO.Path.Combine(tempDir, $"reshot_{stamp}_sys.pcm");
            _systemPcm = File.Create(_systemPcmPath);
        }
        if (_micAudio is not null)
        {
            _micPcmPath = System.IO.Path.Combine(tempDir, $"reshot_{stamp}_mic.pcm");
            _micPcm = File.Create(_micPcmPath);
        }

        // Video-only: the audio track is added by the muxer once the tracks are chosen.
        _encoder = new Mp4VideoEncoder(_tempVideoPath, _cropWidth, _cropHeight, fps, bitrate, null);

        _videoThread = new Thread(VideoLoop) { IsBackground = true, Name = "reshot-video" };
        _videoThread.Start();
        if (_systemAudio is not null || _micAudio is not null)
        {
            _audioThread = new Thread(AudioLoop) { IsBackground = true, Name = "reshot-audio" };
            _audioThread.Start();
        }
    }

    private static AudioCaptureMixer? TryOpen(AudioSources sources)
    {
        var mixer = new AudioCaptureMixer(sources);
        if (mixer.HasAnySource)
            return mixer;
        mixer.Dispose();
        return null;
    }

    private void VideoLoop()
    {
        var buffer = new byte[_cropWidth * _cropHeight * 4];
        try
        {
            // Wait for the first captured frame, then release the audio clock.
            var spin = Stopwatch.StartNew();
            while (!_stop && !_stream.CopyRegion(_cropLeft, _cropTop, _cropWidth, _cropHeight, buffer))
            {
                if (spin.Elapsed > TimeSpan.FromSeconds(3))
                {
                    Log.Warn("Recorder: no frames arrived within 3s.");
                    _started.Set();
                    return;
                }
                Thread.Sleep(4);
            }
            _started.Set();
            if (_stop)
                return;

            var clock = Stopwatch.StartNew();
            var interval = 1000.0 / _fps;
            long frame = 0;
            while (!_stop)
            {
                if (_stream.CopyRegion(_cropLeft, _cropTop, _cropWidth, _cropHeight, buffer))
                {
                    if (_mask is not null)
                        ApplyMask(buffer);
                    _encoder.WriteFrame(buffer);
                }
                frame++;
                var delayMs = frame * interval - clock.Elapsed.TotalMilliseconds;
                if (delayMs > 1)
                    Thread.Sleep((int)delayMs);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Recorder: video loop failed", ex);
        }
    }

    /// <summary>
    /// Blacks out pixels outside the selected shape(s). MP4/H.264 has no alpha, so
    /// "empty space" becomes black rather than transparent.
    /// </summary>
    private void ApplyMask(byte[] bgra)
    {
        for (var y = 0; y < _cropHeight; y++)
        {
            var maskRow = y * _maskStride;
            var pixRow = y * _cropWidth * 4;
            for (var x = 0; x < _cropWidth; x++)
            {
                if (_mask![maskRow + x] >= 128)
                    continue;
                var i = pixRow + x * 4;
                bgra[i] = 0;
                bgra[i + 1] = 0;
                bgra[i + 2] = 0;
                bgra[i + 3] = 255;
            }
        }
    }

    private void AudioLoop()
    {
        var pcm = new byte[AudioMaxChunkFrames * AudioCaptureMixer.Channels * 2];
        try
        {
            _started.Wait();
            if (_stop)
                return;

            // Drop audio buffered while the first frame was awaited.
            _systemAudio?.Flush();
            _micAudio?.Flush();

            var clock = Stopwatch.StartNew();
            long written = 0;
            while (!_stop)
            {
                var target = (long)(clock.Elapsed.TotalSeconds * AudioCaptureMixer.SampleRate);
                var need = target - written;
                if (need >= 480)
                {
                    // Both tracks are pulled the same number of frames, so they stay aligned.
                    var chunk = (int)Math.Min(need, AudioMaxChunkFrames);
                    if (_systemAudio is not null)
                        _systemPcm!.Write(pcm, 0, _systemAudio.ReadPcm16(pcm, chunk));
                    if (_micAudio is not null)
                        _micPcm!.Write(pcm, 0, _micAudio.ReadPcm16(pcm, chunk));
                    written += chunk;
                }
                Thread.Sleep(8);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Recorder: audio loop failed", ex);
        }
    }

    /// <summary>Stops capture and finalizes the temp video + PCM files (no output yet).</summary>
    public void Stop()
    {
        if (_stopped)
            return;
        _stopped = true;

        _stop = true;
        _started.Set();
        _videoThread.Join(TimeSpan.FromSeconds(5));
        _audioThread?.Join(TimeSpan.FromSeconds(5));

        _encoder.Dispose(); // finalizes the temp MP4
        _systemAudio?.Dispose();
        _micAudio?.Dispose();
        _systemPcm?.Dispose();
        _micPcm?.Dispose();
        _systemPcm = null;
        _micPcm = null;
        _stream.Dispose();
    }

    /// <summary>
    /// Writes the final MP4 with the chosen audio tracks (video is copied through, never
    /// re-encoded) and deletes the temp files. Call after <see cref="Stop"/>.
    /// </summary>
    public bool Finish(bool keepSystem, bool keepMic)
    {
        Stop();

        var tracks = new List<string>();
        if (keepSystem && _systemPcmPath is not null)
            tracks.Add(_systemPcmPath);
        if (keepMic && _micPcmPath is not null)
            tracks.Add(_micPcmPath);

        var ok = Mp4Muxer.Mux(_tempVideoPath, tracks, Path);
        if (!ok)
        {
            // Muxing failed — keep at least the silent video rather than losing the take.
            try { File.Copy(_tempVideoPath, Path, overwrite: true); ok = File.Exists(Path); }
            catch (Exception ex) { Log.Error("Recorder: fallback copy failed", ex); }
        }
        CleanupTemps();
        return ok;
    }

    private void CleanupTemps()
    {
        foreach (var path in new[] { _tempVideoPath, _systemPcmPath, _micPcmPath })
        {
            if (path is null)
                continue;
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { Log.Warn($"Recorder: could not delete temp '{path}': {ex.Message}"); }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
        _started.Dispose();
    }
}
