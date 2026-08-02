using Reshot.Core.Diagnostics;

namespace Reshot.Recording;

/// <summary>
/// Combines an already-encoded video-only MP4 with the selected PCM sources.
/// ffmpeg copies H.264 unchanged and encodes only AAC; amix uses normalize=0 so
/// multiple tracks retain the saturating-sum loudness of the former mixer.
/// </summary>
public static class Mp4Muxer
{
    private const int SampleRate = AudioCaptureMixer.SampleRate;
    private const int Channels = AudioCaptureMixer.Channels;
    private const int AudioBitrate = 160_000;

    /// <summary>
    /// Writes <paramref name="outputPath"/> from the video in <paramref name="videoOnlyMp4"/>
    /// plus the selected raw 48 kHz stereo 16-bit sources. An empty list copies a
    /// silent video, and false preserves VideoRecorder's silent-video fallback.
    /// </summary>
    public static bool Mux(string videoOnlyMp4, IReadOnlyList<string> pcmPaths, string outputPath)
    {
        var tracks = pcmPaths.Where(File.Exists).ToList();

        try
        {
            var ok = Ffmpeg.Run(
                FfmpegArgs.Mux(videoOnlyMp4, tracks, outputPath, SampleRate, Channels, AudioBitrate),
                out var stderrTail);
            if (!ok || !HasContent(outputPath))
            {
                var detail = ok ? "ffmpeg produced no output" : stderrTail;
                Log.Error($"Muxer: ffmpeg failed for '{outputPath}': {detail}");
                DeleteIncompleteOutput(videoOnlyMp4, outputPath);
                return false;
            }

            Log.Info($"Muxer: {tracks.Count} audio source(s) → {outputPath}");
            return true;
        }
        catch (FileNotFoundException)
        {
            // Missing ffmpeg is a deployment error and must remain a loud failure.
            throw;
        }
        catch (Exception ex)
        {
            Log.Error($"Muxer: failed to mux '{outputPath}'", ex);
            DeleteIncompleteOutput(videoOnlyMp4, outputPath);
            return false;
        }
    }

    private static bool HasContent(string path)
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

    private static void DeleteIncompleteOutput(string inputPath, string outputPath)
    {
        try
        {
            if (!string.Equals(Path.GetFullPath(inputPath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase)
                && File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Muxer: could not delete incomplete output '{outputPath}': {ex.Message}");
        }
    }
}
