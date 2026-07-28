using System.Runtime.InteropServices;
using Reshot.Core.Diagnostics;
using Vortice.MediaFoundation;

namespace Reshot.Recording;

/// <summary>
/// Audio-only AAC → M4A encoder (Media Foundation SinkWriter), for the standalone
/// audio-recording tool. Fed 16-bit interleaved PCM; timestamps run off a sample
/// clock. Mirrors the audio path of <see cref="Mp4VideoEncoder"/>.
/// </summary>
public sealed class M4aAudioEncoder : IDisposable
{
    private readonly IMFSinkWriter _writer;
    private readonly int _streamIndex;
    private readonly int _sampleRate;
    private readonly int _bytesPerFrame;
    private long _framesWritten;
    private bool _disposed;

    public M4aAudioEncoder(string path, int sampleRate, int channels, int bitrate)
    {
        _sampleRate = sampleRate;
        _bytesPerFrame = channels * 2;

        MediaFactory.MFStartup(false);
        _writer = MediaFactory.MFCreateSinkWriterFromURL(path, null, null);

        using (var aacOut = MediaFactory.MFCreateMediaType())
        {
            aacOut.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
            aacOut.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Aac);
            aacOut.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
            aacOut.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)sampleRate);
            aacOut.Set(MediaTypeAttributeKeys.AudioNumChannels, (uint)channels);
            aacOut.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, (uint)(bitrate / 8));
            _streamIndex = _writer.AddStream(aacOut);
        }

        using (var pcmIn = MediaFactory.MFCreateMediaType())
        {
            pcmIn.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
            pcmIn.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
            pcmIn.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
            pcmIn.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)sampleRate);
            pcmIn.Set(MediaTypeAttributeKeys.AudioNumChannels, (uint)channels);
            pcmIn.Set(MediaTypeAttributeKeys.AudioBlockAlignment, (uint)_bytesPerFrame);
            pcmIn.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, (uint)(sampleRate * _bytesPerFrame));
            _writer.SetInputMediaType(_streamIndex, pcmIn, null);
        }

        _writer.BeginWriting();
        Log.Info($"Audio encoder: AAC {sampleRate}Hz/{channels}ch → {path}");
    }

    public void WriteAudio(byte[] pcm, int byteCount)
    {
        if (byteCount <= 0)
            return;

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

        var frames = byteCount / _bytesPerFrame;
        var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime = _framesWritten * 10_000_000L / _sampleRate;
        sample.SampleDuration = (long)frames * 10_000_000L / _sampleRate;
        _writer.WriteSample(_streamIndex, sample);
        _framesWritten += frames;

        sample.Dispose();
        buffer.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            _writer.Finalize();
            _writer.Dispose();
        }
        finally
        {
            MediaFactory.MFShutdown();
        }
        Log.Info($"Audio encoder: finalized {_framesWritten} frames.");
    }
}
