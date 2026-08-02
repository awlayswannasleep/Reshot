using System.Globalization;
using Reshot.Recording;
using Xunit;

namespace Reshot.Recording.Tests;

public class FfmpegArgsTests
{
    [Fact]
    public void Video_builds_documented_command()
    {
        var args = FfmpegArgs.Video(1920, 1080, 60, 8_000_000, "libx264", @"C:\Videos\recording.mp4");

        Assert.Equal(
            "-hide_banner -loglevel error -y -f rawvideo -pixel_format bgra -video_size 1920x1080 -framerate 60 -i - -an -c:v libx264 -b:v 8000000 -pix_fmt yuv420p -movflags +faststart \"C:\\Videos\\recording.mp4\"",
            args);
    }

    [Fact]
    public void Audio_builds_documented_command()
    {
        var args = FfmpegArgs.Audio(48_000, 2, 192_000, @"C:\Audio\track.m4a");

        Assert.Equal(
            "-hide_banner -loglevel error -y -f s16le -ar 48000 -ac 2 -i - -c:a aac -b:a 192000 -movflags +faststart \"C:\\Audio\\track.m4a\"",
            args);
    }

    [Fact]
    public void H264_probe_builds_documented_command()
    {
        var args = FfmpegArgs.H264Probe(1920, 1080, "h264_nvenc");

        Assert.Equal(
            "-hide_banner -loglevel error -f lavfi -i color=black:s=1920x1080:r=10:d=0.2 -an -c:v h264_nvenc -pix_fmt yuv420p -f null -",
            args);
    }

    [Fact]
    public void H264_probe_supports_minimum_64_by_64_frame()
    {
        var args = FfmpegArgs.H264Probe(64, 64, "h264_nvenc");

        Assert.Equal(
            "-hide_banner -loglevel error -f lavfi -i color=black:s=64x64:r=10:d=0.2 -an -c:v h264_nvenc -pix_fmt yuv420p -f null -",
            args);
    }

    [Fact]
    public void H264_probe_numeric_arguments_are_invariant_under_comma_decimal_culture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

        try
        {
            var args = FfmpegArgs.H264Probe(1_920, 1_080, "h264_qsv");

            Assert.Contains("color=black:s=1920x1080:r=10:d=0.2", args, StringComparison.Ordinal);
            Assert.DoesNotContain("1.920", args, StringComparison.Ordinal);
            Assert.DoesNotContain("1.080", args, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Mux_with_no_tracks_copies_video()
    {
        var args = FfmpegArgs.Mux(@"C:\Temp\video-only.mp4", [], @"C:\Videos\final.mp4", 48_000, 2, 192_000);

        Assert.Equal(
            "-hide_banner -loglevel error -y -i \"C:\\Temp\\video-only.mp4\" -c copy -movflags +faststart \"C:\\Videos\\final.mp4\"",
            args);
    }

    [Fact]
    public void Mux_with_one_track_maps_audio_and_uses_shortest()
    {
        var args = FfmpegArgs.Mux(
            @"C:\Temp\video-only.mp4",
            [@"C:\Temp\system.pcm"],
            @"C:\Videos\final.mp4",
            48_000,
            2,
            192_000);

        Assert.Equal(
            "-hide_banner -loglevel error -y -i \"C:\\Temp\\video-only.mp4\" -f s16le -ar 48000 -ac 2 -i \"C:\\Temp\\system.pcm\" -map 0:v:0 -map 1:a:0 -c:v copy -c:a aac -b:a 192000 -shortest -movflags +faststart \"C:\\Videos\\final.mp4\"",
            args);
    }

    [Fact]
    public void Mux_with_two_tracks_mixes_audio()
    {
        var args = FfmpegArgs.Mux(
            @"C:\Temp\video-only.mp4",
            [@"C:\Temp\system.pcm", @"C:\Temp\microphone.pcm"],
            @"C:\Videos\final.mp4",
            48_000,
            2,
            192_000);

        Assert.Equal(
            "-hide_banner -loglevel error -y -i \"C:\\Temp\\video-only.mp4\" -f s16le -ar 48000 -ac 2 -i \"C:\\Temp\\system.pcm\" -f s16le -ar 48000 -ac 2 -i \"C:\\Temp\\microphone.pcm\" -filter_complex \"[1:a][2:a]amix=inputs=2:duration=longest:normalize=0[aout]\" -map 0:v:0 -map \"[aout]\" -c:v copy -c:a aac -b:a 192000 -movflags +faststart \"C:\\Videos\\final.mp4\"",
            args);
    }

    [Fact]
    public void Mux_with_three_tracks_generalizes_audio_mix()
    {
        var args = FfmpegArgs.Mux(
            @"C:\Temp\video-only.mp4",
            [@"C:\Temp\system.pcm", @"C:\Temp\microphone.pcm", @"C:\Temp\application.pcm"],
            @"C:\Videos\final.mp4",
            44_100,
            1,
            128_000);

        Assert.Equal(
            "-hide_banner -loglevel error -y -i \"C:\\Temp\\video-only.mp4\" -f s16le -ar 44100 -ac 1 -i \"C:\\Temp\\system.pcm\" -f s16le -ar 44100 -ac 1 -i \"C:\\Temp\\microphone.pcm\" -f s16le -ar 44100 -ac 1 -i \"C:\\Temp\\application.pcm\" -filter_complex \"[1:a][2:a][3:a]amix=inputs=3:duration=longest:normalize=0[aout]\" -map 0:v:0 -map \"[aout]\" -c:v copy -c:a aac -b:a 128000 -movflags +faststart \"C:\\Videos\\final.mp4\"",
            args);
    }

    [Fact]
    public void Paths_with_spaces_remain_quoted()
    {
        var args = FfmpegArgs.Video(1280, 720, 30, 4_000_000, "h264_nvenc", @"C:\My Videos\recording one.mp4");

        Assert.EndsWith("\"C:\\My Videos\\recording one.mp4\"", args, StringComparison.Ordinal);
        Assert.DoesNotContain("-movflags +faststart C:\\My Videos", args, StringComparison.Ordinal);
    }

    [Fact]
    public void Numeric_arguments_are_invariant_under_comma_decimal_culture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

        try
        {
            var args = FfmpegArgs.Mux(
                @"C:\Temp\video-only.mp4",
                [@"C:\Temp\system.pcm", @"C:\Temp\microphone.pcm"],
                @"C:\Videos\final.mp4",
                44_100,
                2,
                128_000);

            Assert.Contains("-ar 44100 -ac 2", args, StringComparison.Ordinal);
            Assert.Contains("amix=inputs=2", args, StringComparison.Ordinal);
            Assert.Contains("-b:a 128000", args, StringComparison.Ordinal);
            Assert.DoesNotContain("44.100", args, StringComparison.Ordinal);
            Assert.DoesNotContain("128.000", args, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
