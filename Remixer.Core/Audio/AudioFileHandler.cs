using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.MediaFoundation;
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
        SaveAudioFileWithProgress(audioProvider, outputPath, sampleRate, channels, bitsPerSample, null, null);
    }
    
    public static void SaveAudioFileWithProgress(IWaveProvider audioProvider, string outputPath, int sampleRate, int channels, int bitsPerSample, 
        TimeSpan? totalDuration, Action<int, string>? progressCallback)
    {
        var extension = Path.GetExtension(outputPath).ToLowerInvariant();
        var format = audioProvider.WaveFormat;

        // Initialize MediaFoundation if needed for compressed formats
        if (extension != ".wav")
        {
            try
            {
                MediaFoundationApi.Startup();
            }
            catch (Exception ex)
            {
                Logger.Warning($"MediaFoundation initialization failed: {ex.Message}. Falling back to WAV format.");
                // Change extension to .wav as fallback
                var wavPath = Path.ChangeExtension(outputPath, ".wav");
                Logger.Info($"Exporting as WAV instead: {wavPath}");
                SaveAudioFileWithProgress(audioProvider, wavPath, sampleRate, channels, bitsPerSample, totalDuration, progressCallback);
                return;
            }
        }

        IDisposable? writer = null;
        try
        {
            if (extension == ".wav")
            {
                // WAV format - uncompressed
                writer = new WaveFileWriter(outputPath, format);
                WriteAudioData(audioProvider, writer, format, totalDuration, progressCallback);
            }
            else
            {
                // Compressed formats using MediaFoundation
                var mediaType = GetMediaTypeForFormat(extension, sampleRate, channels);
                if (mediaType == null)
                {
                    throw new NotSupportedException($"Format '{extension}' is not supported. Supported formats: .wav, .mp3, .m4a, .wma");
                }

                // Use MediaFoundationEncoder to encode
                // Create a temporary WAV file first, then encode it
                var tempWavPath = Path.ChangeExtension(outputPath, ".tmp.wav");
                try
                {
                    // Write to temporary WAV first
                    using (var tempWriter = new WaveFileWriter(tempWavPath, format))
                    {
                        WriteAudioData(audioProvider, tempWriter, format, totalDuration, 
                            (percent, msg) => progressCallback?.Invoke(percent / 2, $"Preparing audio... {percent}%"));
                    }
                    
                    // Now encode the WAV to target format
                    progressCallback?.Invoke(50, "Encoding audio...");
                    using (var reader = new WaveFileReader(tempWavPath))
                    {
                        using (var encoder = new MediaFoundationEncoder(mediaType))
                        {
                            encoder.Encode(outputPath, reader);
                        }
                    }
                    
                    // Clean up temp file
                    File.Delete(tempWavPath);
                    progressCallback?.Invoke(100, "Encoding complete!");
                }
                catch
                {
                    // Clean up temp file on error
                    if (File.Exists(tempWavPath))
                    {
                        try { File.Delete(tempWavPath); } catch { }
                    }
                    throw;
                }
            }
        }
        finally
        {
            writer?.Dispose();
        }
    }
    
    private static void WriteAudioData(IWaveProvider audioProvider, IDisposable writer, WaveFormat format, 
        TimeSpan? totalDuration, Action<int, string>? progressCallback)
    {
        byte[] buffer = new byte[8192];
        int bytesRead;
        long totalBytesWritten = 0;
        long? estimatedTotalBytes = null;
        
        // Estimate total bytes if we have duration
        if (totalDuration.HasValue && format.AverageBytesPerSecond > 0)
        {
            estimatedTotalBytes = (long)(totalDuration.Value.TotalSeconds * format.AverageBytesPerSecond);
        }

        while ((bytesRead = audioProvider.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (writer is WaveFileWriter waveWriter)
            {
                waveWriter.Write(buffer, 0, bytesRead);
            }
            totalBytesWritten += bytesRead;
            
            // Report progress if callback provided
            if (progressCallback != null && estimatedTotalBytes.HasValue && estimatedTotalBytes.Value > 0)
            {
                int percent = (int)((totalBytesWritten * 100) / estimatedTotalBytes.Value);
                progressCallback(percent, $"Writing audio... {percent}%");
            }
        }
        
        if (writer is WaveFileWriter waveWriter2)
        {
            waveWriter2.Flush();
        }
    }
    
    private static MediaType? GetMediaTypeForFormat(string extension, int sampleRate, int channels)
    {
        try
        {
            return extension.ToLowerInvariant() switch
            {
                ".mp3" => MediaFoundationEncoder.SelectMediaType(
                    AudioSubtypes.MFAudioFormat_MP3,
                    new WaveFormat(sampleRate, channels),
                    sampleRate * channels * 2 * 8), // Bitrate: sampleRate * channels * bitsPerSample
                ".m4a" or ".aac" => MediaFoundationEncoder.SelectMediaType(
                    AudioSubtypes.MFAudioFormat_AAC,
                    new WaveFormat(sampleRate, channels),
                    sampleRate * channels * 2 * 8),
                ".wma" => MediaFoundationEncoder.SelectMediaType(
                    AudioSubtypes.MFAudioFormat_WMAudioV9,
                    new WaveFormat(sampleRate, channels),
                    sampleRate * channels * 2 * 8),
                _ => null
            };
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to get media type for {extension}: {ex.Message}");
            return null;
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

