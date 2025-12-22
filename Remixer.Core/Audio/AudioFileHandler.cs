using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Remixer.Core.Logging;
using System;
using System.IO;

namespace Remixer.Core.Audio;

public class AudioFileHandler
{
    public static WaveStream LoadAudioFile(string filePath)
    {
        Logger.Debug($"Loading audio file: {filePath}");
        
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            Logger.Error($"Audio file not found: {filePath}");
            throw new FileNotFoundException("Audio file not found", filePath);
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        Logger.Debug($"File extension: {extension}");
        
        WaveStream reader = extension switch
        {
            ".wav" => new WaveFileReader(filePath),
            ".mp3" => new MediaFoundationReader(filePath), // Use MediaFoundation for better MP3 support
            ".flac" => new MediaFoundationReader(filePath),
            ".ogg" => new MediaFoundationReader(filePath),
            ".m4a" => new MediaFoundationReader(filePath),
            ".aac" => new MediaFoundationReader(filePath),
            _ => throw new NotSupportedException($"Audio format '{extension}' is not supported")
        };
        
        Logger.Debug($"Initial format: {reader.WaveFormat}");

        // Convert to IEEE Float format (32-bit, stereo, 44.1kHz) for consistent processing
        // This ensures compatibility with WaveToSampleProvider which works best with float
        var targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        
        // Check if we need conversion
        // We need conversion if:
        // 1. The encoding is not IEEE Float
        // 2. The sample rate or channels don't match our target
        bool needsConversion = reader.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat ||
                              reader.WaveFormat.SampleRate != targetFormat.SampleRate ||
                              reader.WaveFormat.Channels != targetFormat.Channels;
        
        if (!needsConversion)
        {
            Logger.Debug("No format conversion needed");
            return reader;
        }
        
        Logger.Debug($"Converting to target format: {targetFormat}");
        
        try
        {
            // Resample and convert to integer PCM
            var resampler = new MediaFoundationResampler(reader, targetFormat)
            {
                ResamplerQuality = 60 // High quality resampling
            };
            
            Logger.Debug($"Resampler output format: {resampler.WaveFormat}");
            Logger.Debug("Format conversion completed");
            
            // Wrap the resampler in a WaveStream wrapper
            return new ResamplerDmoStream(resampler, reader);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to convert audio format from {reader.WaveFormat} to {targetFormat}", ex);
            // Dispose the reader if conversion fails
            reader.Dispose();
            throw;
        }
    }
    
    // Helper class to wrap IWaveProvider as WaveStream
    private class ResamplerDmoStream : WaveStream
    {
        private readonly IWaveProvider _provider;
        private readonly WaveStream _sourceStream;
        private long _position;
        private long? _cachedLength;

        public ResamplerDmoStream(IWaveProvider provider, WaveStream sourceStream)
        {
            _provider = provider;
            _sourceStream = sourceStream;
        }

        public override WaveFormat WaveFormat => _provider.WaveFormat;

        public override long Length
        {
            get
            {
                // Calculate length based on the source stream's duration and the new format
                // Resampling doesn't change duration, only sample rate/channels/format
                if (!_cachedLength.HasValue)
                {
                    var sourceDuration = _sourceStream.TotalTime;
                    // Calculate length based on duration and the new format's bytes per second
                    _cachedLength = (long)(sourceDuration.TotalSeconds * WaveFormat.AverageBytesPerSecond);
                }
                return _cachedLength.Value;
            }
        }

        public override long Position
        {
            get => _position;
            set
            {
                // Convert position from resampled format to source format
                // Position is in bytes, so we need to convert based on bytes per second ratio
                if (WaveFormat.AverageBytesPerSecond > 0 && _sourceStream.WaveFormat.AverageBytesPerSecond > 0)
                {
                    // Calculate the time position in seconds
                    double timeInSeconds = (double)value / WaveFormat.AverageBytesPerSecond;
                    // Convert to source stream position
                    long sourcePosition = (long)(timeInSeconds * _sourceStream.WaveFormat.AverageBytesPerSecond);
                    _sourceStream.Position = sourcePosition;
                }
                else
                {
                    // Fallback: try to set directly (may not work correctly)
                    _sourceStream.Position = value;
                }
                _position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesRead = _provider.Read(buffer, offset, count);
            _position += bytesRead;
            return bytesRead;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_provider is IDisposable disposableProvider)
                {
                    disposableProvider.Dispose();
                }
                _sourceStream?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    public static void SaveAudioFile(IWaveProvider audioProvider, string outputPath, int sampleRate = 44100, int channels = 2, int bitsPerSample = 16)
    {
        var extension = Path.GetExtension(outputPath).ToLowerInvariant();
        var format = audioProvider.WaveFormat;

        using var writer = extension switch
        {
            ".wav" => new WaveFileWriter(outputPath, format),
            _ => throw new NotSupportedException($"Output format '{extension}' is not supported. Use WAV format.")
        };

        byte[] buffer = new byte[8192];
        int bytesRead;

        while ((bytesRead = audioProvider.Read(buffer, 0, buffer.Length)) > 0)
        {
            writer.Write(buffer, 0, bytesRead);
        }
    }

    public static AudioFileInfo GetAudioInfo(string filePath)
    {
        using var reader = LoadAudioFile(filePath);
        return new AudioFileInfo
        {
            SampleRate = reader.WaveFormat.SampleRate,
            Channels = reader.WaveFormat.Channels,
            BitsPerSample = reader.WaveFormat.BitsPerSample,
            Duration = reader.TotalTime,
            FilePath = filePath
        };
    }
}

public class AudioFileInfo
{
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public int BitsPerSample { get; set; }
    public TimeSpan Duration { get; set; }
    public string FilePath { get; set; } = string.Empty;
}

