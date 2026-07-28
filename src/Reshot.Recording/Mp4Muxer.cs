using System.Runtime.InteropServices;
using Reshot.Core.Diagnostics;
using Vortice.MediaFoundation;

namespace Reshot.Recording;

/// <summary>
/// Combines an already-encoded video-only MP4 with a freshly mixed audio track into the
/// final MP4. The video is <b>passed through</b> (its compressed samples are copied as-is,
/// no re-encode, no quality loss); only the AAC track is encoded here.
///
/// This is what makes the post-recording track picker honest: during capture each audio
/// source is written to its own raw PCM file, so which ones end up in the file is decided
/// after the user chooses, not while recording.
/// </summary>
public static class Mp4Muxer
{
    private const int SampleRate = AudioCaptureMixer.SampleRate;
    private const int Channels = AudioCaptureMixer.Channels;
    private const int BytesPerFrame = Channels * 2;

    /// <summary>
    /// Writes <paramref name="outputPath"/> from the video in <paramref name="videoOnlyMp4"/>
    /// plus the sum of <paramref name="pcmPaths"/> (raw 48 kHz stereo 16-bit). An empty list
    /// produces a silent video. Returns false (and logs) if muxing fails.
    /// </summary>
    public static bool Mux(string videoOnlyMp4, IReadOnlyList<string> pcmPaths, string outputPath)
    {
        var tracks = pcmPaths.Where(File.Exists).ToList();
        MediaFactory.MFStartup(false);
        IMFSourceReader? reader = null;
        IMFSinkWriter? writer = null;
        var pcmStreams = new List<FileStream>();

        try
        {
            reader = MediaFactory.MFCreateSourceReaderFromURL(videoOnlyMp4, null);
            reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
            reader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);

            // The source's own compressed type, used for both the output stream and the
            // input type — that combination is what tells the SinkWriter "don't re-encode".
            using var videoType = reader.GetNativeMediaType(SourceReaderIndex.FirstVideoStream, 0);

            var attributes = MediaFactory.MFCreateAttributes(1);
            attributes.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, 1u);
            writer = MediaFactory.MFCreateSinkWriterFromURL(outputPath, null, attributes);

            var videoIndex = writer.AddStream(videoType);
            writer.SetInputMediaType(videoIndex, videoType, null);

            var audioIndex = -1;
            if (tracks.Count > 0)
            {
                using (var aacOut = MediaFactory.MFCreateMediaType())
                {
                    aacOut.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
                    aacOut.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Aac);
                    aacOut.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
                    aacOut.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)SampleRate);
                    aacOut.Set(MediaTypeAttributeKeys.AudioNumChannels, (uint)Channels);
                    aacOut.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, 160_000u / 8);
                    audioIndex = writer.AddStream(aacOut);
                }
                using (var pcmIn = MediaFactory.MFCreateMediaType())
                {
                    pcmIn.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
                    pcmIn.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
                    pcmIn.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
                    pcmIn.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)SampleRate);
                    pcmIn.Set(MediaTypeAttributeKeys.AudioNumChannels, (uint)Channels);
                    pcmIn.Set(MediaTypeAttributeKeys.AudioBlockAlignment, (uint)BytesPerFrame);
                    pcmIn.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, (uint)(SampleRate * BytesPerFrame));
                    writer.SetInputMediaType(audioIndex, pcmIn, null);
                }
                foreach (var path in tracks)
                    pcmStreams.Add(File.OpenRead(path));
            }

            writer.BeginWriting();

            // Interleave by timestamp: a SinkWriter buffers whichever stream runs ahead, so
            // dumping all video first and all audio last would hold the whole video in RAM.
            const int chunkFrames = 4800; // ~100 ms
            var mixBuffer = new byte[chunkFrames * BytesPerFrame];
            var srcBuffer = new byte[chunkFrames * BytesPerFrame];
            long audioFrames = 0;
            var audioDone = audioIndex < 0;
            var videoDone = false;
            long videoTime = 0;

            while (!videoDone || !audioDone)
            {
                if (!videoDone)
                {
                    var sample = reader.ReadSample(
                        SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None,
                        out _, out var flags, out var timestamp);

                    if (flags.HasFlag(SourceReaderFlag.EndOfStream))
                    {
                        videoDone = true;
                    }
                    else if (sample is not null)
                    {
                        videoTime = timestamp;
                        writer.WriteSample(videoIndex, sample);
                        sample.Dispose();
                    }
                }

                // Keep audio just behind the video clock (or drain it once video ends).
                while (!audioDone)
                {
                    var audioTime = audioFrames * 10_000_000L / SampleRate;
                    if (!videoDone && audioTime > videoTime)
                        break;

                    var read = MixChunk(pcmStreams, srcBuffer, mixBuffer);
                    if (read <= 0)
                    {
                        audioDone = true;
                        break;
                    }
                    WriteAudio(writer, audioIndex, mixBuffer, read, ref audioFrames);
                }
            }

            writer.Finalize();
            Log.Info($"Muxer: {tracks.Count} audio source(s) → {outputPath}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Muxer: failed to mux '{outputPath}'", ex);
            return false;
        }
        finally
        {
            foreach (var s in pcmStreams)
                s.Dispose();
            writer?.Dispose();
            reader?.Dispose();
            MediaFactory.MFShutdown();
        }
    }

    /// <summary>Sums one chunk from every PCM track (saturating). Returns bytes produced.</summary>
    private static int MixChunk(List<FileStream> streams, byte[] scratch, byte[] mix)
    {
        var produced = 0;
        Array.Clear(mix);

        foreach (var stream in streams)
        {
            var read = stream.Read(scratch, 0, scratch.Length);
            if (read <= 0)
                continue;
            read -= read % 2; // whole 16-bit samples only
            produced = Math.Max(produced, read);

            for (var i = 0; i < read; i += 2)
            {
                var a = (short)(mix[i] | (mix[i + 1] << 8));
                var b = (short)(scratch[i] | (scratch[i + 1] << 8));
                var sum = Math.Clamp(a + b, short.MinValue, short.MaxValue);
                mix[i] = (byte)(sum & 0xFF);
                mix[i + 1] = (byte)((sum >> 8) & 0xFF);
            }
        }
        return produced - produced % BytesPerFrame;
    }

    private static void WriteAudio(IMFSinkWriter writer, int streamIndex, byte[] pcm, int byteCount, ref long framesWritten)
    {
        var buffer = MediaFactory.MFCreateMemoryBuffer(byteCount);
        buffer.Lock(out var dest, out _, out _);
        try
        {
            Marshal.Copy(pcm, 0, dest, byteCount);
        }
        finally
        {
            buffer.Unlock();
        }
        buffer.CurrentLength = byteCount;

        var frames = byteCount / BytesPerFrame;
        var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime = framesWritten * 10_000_000L / SampleRate;
        sample.SampleDuration = (long)frames * 10_000_000L / SampleRate;
        writer.WriteSample(streamIndex, sample);
        framesWritten += frames;

        sample.Dispose();
        buffer.Dispose();
    }
}
