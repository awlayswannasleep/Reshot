using System.Runtime.InteropServices;
using Reshot.Core.Diagnostics;
using Vortice.MediaFoundation;

namespace Reshot.Recording;

/// <summary>
/// Hardware H.264 → MP4 encoder built on the Media Foundation SinkWriter
/// (ARCHITECTURE §9). Frames are pushed as top-down BGRA (RGB32); the SinkWriter's
/// encoder MFT converts to NV12 and does the H.264 encode, using the GPU when the
/// driver exposes a hardware transform. One instance = one output file.
/// </summary>
public sealed class Mp4VideoEncoder : IDisposable
{
    private const int Progressive = 2; // MFVideoInterlace_Progressive

    private readonly IMFSinkWriter _writer;
    private readonly int _streamIndex;
    private readonly int _width;
    private readonly int _height;
    private readonly int _rowBytes;
    private readonly long _frameDuration100Ns;
    private readonly byte[] _nv12;
    private long _frameIndex;
    private bool _disposed;

    private readonly object _writeLock = new();
    private readonly int _audioStreamIndex = -1;
    private readonly int _audioSampleRate;
    private readonly int _audioBytesPerFrame; // channels * 2 (16-bit)
    private long _audioFramesWritten;

    /// <summary>Optional PCM audio track config (16-bit); null = video only.</summary>
    public sealed record AudioConfig(int SampleRate, int Channels, int Bitrate);

    public bool HasAudio => _audioStreamIndex >= 0;

    public Mp4VideoEncoder(string path, int width, int height, int fps, int bitrate, AudioConfig? audio = null)
    {
        // Encoders want even dimensions; round down.
        _width = width & ~1;
        _height = height & ~1;
        _rowBytes = _width * 4;
        _frameDuration100Ns = 10_000_000L / fps;

        MediaFactory.MFStartup(false); // false = full startup (not lite)

        var attributes = MediaFactory.MFCreateAttributes(1);
        // Prefer the GPU H.264 encoder (NVENC/AMF/QSV) when the driver exposes one.
        attributes.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, 1u);
        _writer = MediaFactory.MFCreateSinkWriterFromURL(path, null, attributes);

        using (var outType = MediaFactory.MFCreateMediaType())
        {
            outType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            outType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264);
            outType.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)bitrate);
            outType.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)Progressive);
            outType.Set(MediaTypeAttributeKeys.FrameSize, Pack(_width, _height));
            outType.Set(MediaTypeAttributeKeys.FrameRate, Pack(fps, 1));
            outType.Set(MediaTypeAttributeKeys.PixelAspectRatio, Pack(1, 1));
            _streamIndex = _writer.AddStream(outType);
        }

        using (var inType = MediaFactory.MFCreateMediaType())
        {
            // NV12 fed straight to the H.264 encoder — no auto color-converter MFT.
            inType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            inType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.NV12);
            inType.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)Progressive);
            inType.Set(MediaTypeAttributeKeys.FrameSize, Pack(_width, _height));
            inType.Set(MediaTypeAttributeKeys.FrameRate, Pack(fps, 1));
            inType.Set(MediaTypeAttributeKeys.PixelAspectRatio, Pack(1, 1));
            _writer.SetInputMediaType(_streamIndex, inType, null);
        }

        _nv12 = new byte[_width * _height * 3 / 2];

        if (audio is not null)
        {
            _audioSampleRate = audio.SampleRate;
            _audioBytesPerFrame = audio.Channels * 2;

            using (var aacOut = MediaFactory.MFCreateMediaType())
            {
                aacOut.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
                aacOut.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Aac);
                aacOut.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
                aacOut.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)audio.SampleRate);
                aacOut.Set(MediaTypeAttributeKeys.AudioNumChannels, (uint)audio.Channels);
                aacOut.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, (uint)(audio.Bitrate / 8));
                _audioStreamIndex = _writer.AddStream(aacOut);
            }

            using (var pcmIn = MediaFactory.MFCreateMediaType())
            {
                pcmIn.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
                pcmIn.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
                pcmIn.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
                pcmIn.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)audio.SampleRate);
                pcmIn.Set(MediaTypeAttributeKeys.AudioNumChannels, (uint)audio.Channels);
                pcmIn.Set(MediaTypeAttributeKeys.AudioBlockAlignment, (uint)_audioBytesPerFrame);
                pcmIn.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, (uint)(audio.SampleRate * _audioBytesPerFrame));
                _writer.SetInputMediaType(_audioStreamIndex, pcmIn, null);
            }
        }

        _writer.BeginWriting();
        Log.Info($"Encoder: MP4/H.264 {_width}x{_height} @ {fps}fps, {bitrate / 1000}kbps"
                 + (audio is not null ? $" + AAC {audio.SampleRate}Hz/{audio.Channels}ch" : "") + $" → {path}");
    }

    /// <summary>
    /// Writes one frame. <paramref name="bgra"/> is top-down BGRA of the encoder
    /// size (its stride must equal the encoder width*4). Rows are copied bottom-up
    /// because MF RGB32 is bottom-up by default.
    /// </summary>
    public void WriteFrame(ReadOnlySpan<byte> bgra)
    {
        BgraToNv12(bgra, _nv12);
        var length = _nv12.Length;
        var buffer = MediaFactory.MFCreateMemoryBuffer(length);
        buffer.Lock(out var dest, out _, out _);
        try
        {
            Marshal.Copy(_nv12, 0, dest, length);
        }
        finally
        {
            buffer.Unlock();
        }
        buffer.CurrentLength = length;

        var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime = _frameIndex * _frameDuration100Ns;
        sample.SampleDuration = _frameDuration100Ns;
        lock (_writeLock)
            _writer.WriteSample(_streamIndex, sample);
        _frameIndex++;

        sample.Dispose();
        buffer.Dispose();
    }

    /// <summary>
    /// Writes 16-bit interleaved PCM to the audio track. <paramref name="byteCount"/>
    /// must be a whole number of frames. Timestamps run off a dedicated audio clock
    /// (samples written / sample rate). No-op if the encoder has no audio track.
    /// </summary>
    public void WriteAudio(byte[] pcm, int byteCount)
    {
        if (_audioStreamIndex < 0 || byteCount <= 0)
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

        var frames = byteCount / _audioBytesPerFrame;
        var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime = _audioFramesWritten * 10_000_000L / _audioSampleRate;
        sample.SampleDuration = (long)frames * 10_000_000L / _audioSampleRate;
        lock (_writeLock)
            _writer.WriteSample(_audioStreamIndex, sample);
        _audioFramesWritten += frames;

        sample.Dispose();
        buffer.Dispose();
    }

    /// <summary>Converts top-down BGRA (stride width*4) to NV12 (BT.601, integer math).</summary>
    private void BgraToNv12(ReadOnlySpan<byte> bgra, byte[] nv12)
    {
        int w = _width, h = _height, uvStart = w * h;
        for (var y = 0; y < h; y++)
        {
            var row = y * _rowBytes;
            var yRow = y * w;
            for (var x = 0; x < w; x++)
            {
                var i = row + x * 4;
                int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
                nv12[yRow + x] = Clamp(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16);
            }
        }
        for (var y = 0; y < h; y += 2)
        {
            for (var x = 0; x < w; x += 2)
            {
                var i = y * _rowBytes + x * 4;
                int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
                var uv = uvStart + (y / 2) * w + (x / 2) * 2;
                nv12[uv] = Clamp(((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128);
                nv12[uv + 1] = Clamp(((112 * r - 94 * g - 18 * b + 128) >> 8) + 128);
            }
        }
    }

    private static byte Clamp(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);

    /// <summary>Packs two 32-bit values into the UINT64 layout MF uses for size/rate attributes.</summary>
    private static ulong Pack(int high, int low) => ((ulong)(uint)high << 32) | (uint)low;

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
        Log.Info($"Encoder: finalized {_frameIndex} frames.");
    }
}
