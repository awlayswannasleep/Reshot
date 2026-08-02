using System.Globalization;
using System.Text;

namespace Reshot.Recording;

/// <summary>
/// Builds ffmpeg command lines without touching process or filesystem state.
/// Keeping the grammar here makes the recording paths deterministic and keeps
/// quoting changes reviewable in one place.
/// </summary>
public static class FfmpegArgs
{
    public static string Video(
        int width,
        int height,
        int fps,
        int bitrate,
        string encoder,
        string outputPath)
    {
        return $"-hide_banner -loglevel error -y -f rawvideo -pixel_format bgra -video_size {I(width)}x{I(height)} -framerate {I(fps)} -i - -an -c:v {encoder} -b:v {I(bitrate)} -pix_fmt yuv420p -movflags +faststart \"{outputPath}\"";
    }

    public static string Audio(int sampleRate, int channels, int bitrate, string outputPath)
    {
        return $"-hide_banner -loglevel error -y -f s16le -ar {I(sampleRate)} -ac {I(channels)} -i - -c:a aac -b:a {I(bitrate)} -movflags +faststart \"{outputPath}\"";
    }

    /// <summary>
    /// Builds a no-output live capability trial. The dimensions match the real
    /// recording because hardware availability alone does not imply size support.
    /// </summary>
    public static string H264Probe(int width, int height, string encoder)
    {
        return $"-hide_banner -loglevel error -f lavfi -i color=black:s={I(width)}x{I(height)}:r=10:d=0.2 -an -c:v {encoder} -pix_fmt yuv420p -f null -";
    }

    public static string Mux(
        string videoOnlyMp4,
        IReadOnlyList<string> pcmPaths,
        string outputPath,
        int sampleRate,
        int channels,
        int audioBitrate)
    {
        var command = new StringBuilder(
            $"-hide_banner -loglevel error -y -i \"{videoOnlyMp4}\"");

        if (pcmPaths.Count == 0)
        {
            command.Append($" -c copy -movflags +faststart \"{outputPath}\"");
            return command.ToString();
        }

        foreach (var pcmPath in pcmPaths)
        {
            command.Append($" -f s16le -ar {I(sampleRate)} -ac {I(channels)} -i \"{pcmPath}\"");
        }

        if (pcmPaths.Count == 1)
        {
            command.Append($" -map 0:v:0 -map 1:a:0 -c:v copy -c:a aac -b:a {I(audioBitrate)} -shortest -movflags +faststart \"{outputPath}\"");
            return command.ToString();
        }

        command.Append(" -filter_complex \"");
        for (var index = 1; index <= pcmPaths.Count; index++)
            command.Append($"[{I(index)}:a]");
        command.Append($"amix=inputs={I(pcmPaths.Count)}:duration=longest:normalize=0[aout]\"");
        command.Append($" -map 0:v:0 -map \"[aout]\" -c:v copy -c:a aac -b:a {I(audioBitrate)} -movflags +faststart \"{outputPath}\"");
        return command.ToString();
    }

    private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);
}
