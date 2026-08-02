using System.Diagnostics;
using Reshot.Core.Diagnostics;

namespace Reshot.Recording;

/// <summary>
/// Streams 16-bit interleaved PCM to ffmpeg for the standalone M4A recorder.
/// Keeping the pipe open for the recording lifetime avoids temporary audio data
/// and lets closing stdin serve as the single, deterministic end-of-stream signal.
/// </summary>
public sealed class M4aAudioEncoder : IDisposable
{
    private const int FinalizeTimeoutMs = 30_000;

    private readonly string _path;
    private readonly Process _process;
    private readonly Stream _input;
    private readonly int _bytesPerFrame;
    private long _framesWritten;
    private bool _disposed;

    public M4aAudioEncoder(string path, int sampleRate, int channels, int bitrate)
    {
        _path = path;
        _bytesPerFrame = checked(channels * 2);
        _process = Ffmpeg.Start(FfmpegArgs.Audio(sampleRate, channels, bitrate, path), redirectStdin: true);
        _input = _process.StandardInput.BaseStream;
        Log.Info($"Audio encoder: ffmpeg/AAC {sampleRate}Hz/{channels}ch → {path}");
    }

    public void WriteAudio(byte[] pcm, int byteCount)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (byteCount <= 0)
            return;
        if ((uint)byteCount > (uint)pcm.Length)
            throw new ArgumentOutOfRangeException(nameof(byteCount));

        _input.Write(pcm, 0, byteCount);
        _framesWritten += byteCount / _bytesPerFrame;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        Exception? closeError = null;
        try
        {
            _input.Close();
        }
        catch (Exception ex)
        {
            closeError = ex;
            Log.Error("Audio encoder: failed to close ffmpeg input", ex);
        }

        var ok = Ffmpeg.WaitForSuccessfulExit(_process, FinalizeTimeoutMs, out var exitCode);
        _process.Dispose();
        var hasContent = HasContent(_path);
        if (!ok || closeError is not null || !hasContent)
        {
            var reason = closeError is not null
                ? "input pipe failed"
                : exitCode is null
                    ? "timed out"
                    : exitCode != 0
                        ? $"exited with code {exitCode}"
                        : "produced no output";
            Log.Error($"Audio encoder: ffmpeg {reason} after {_framesWritten} frames; discarding incomplete output '{_path}'.");
            DeleteIncompleteOutput();
            return;
        }

        Log.Info($"Audio encoder: finalized {_framesWritten} frames.");
    }

    private bool HasContent(string path)
    {
        try
        {
            return File.Exists(path) && new FileInfo(path).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private void DeleteIncompleteOutput()
    {
        try
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
        catch (Exception ex)
        {
            Log.Warn($"Audio encoder: could not delete incomplete output '{_path}': {ex.Message}");
        }
    }
}
