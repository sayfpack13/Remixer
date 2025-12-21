using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;

namespace Remixer.Core.Audio.Effects;

public class TempoAdjuster : IEffect
{
    private double _tempo = 1.0;
    
    public bool IsEnabled { get; set; } = true;

    public double Tempo
    {
        get => _tempo;
        set
        {
            if (value <= 0 || value > 4.0)
                throw new ArgumentOutOfRangeException(nameof(value), "Tempo must be between 0 and 4.0");
            _tempo = value;
        }
    }

    public ISampleProvider Apply(ISampleProvider input)
    {
        if (!IsEnabled || Math.Abs(_tempo - 1.0) < 0.01)
            return input;

        // For tempo adjustment without pitch change, we need time-stretching
        // NAudio doesn't have built-in time-stretching
        // This is a simplified approach that changes playback speed (affects both tempo and pitch)
        // For production-quality tempo adjustment without pitch change, consider using SoundTouch library
        
        // Use a simple variable speed provider approach
        // Note: This changes both tempo and pitch proportionally
        return new VariableSpeedProvider(input, _tempo);
    }
}

// Variable speed provider - changes playback speed (tempo and pitch together)
internal class VariableSpeedProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly double _speed;

    public VariableSpeedProvider(ISampleProvider source, double speed)
    {
        _source = source;
        _speed = speed;
        WaveFormat = source.WaveFormat;
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        if (Math.Abs(_speed - 1.0) < 0.001)
            return _source.Read(buffer, offset, count);

        // count is the total number of float values to read (samples * channels)
        int samplesRequested = count / WaveFormat.Channels;
        
        // Calculate how many source samples we need
        int sourceSamplesNeeded = (int)(samplesRequested / _speed) + 2; // +2 for interpolation safety
        int sourceValuesNeeded = sourceSamplesNeeded * WaveFormat.Channels;
        
        float[] sourceBuffer = new float[sourceValuesNeeded];
        int sourceRead = _source.Read(sourceBuffer, 0, sourceValuesNeeded);
        
        if (sourceRead == 0)
            return 0;

        int sourceSamplesAvailable = sourceRead / WaveFormat.Channels;
        int outputSamples = Math.Min(samplesRequested, (int)(sourceSamplesAvailable * _speed));

        // Simple linear interpolation for speed adjustment
        int channels = WaveFormat.Channels;
        int actualOutputSamples = 0;
        
        for (int i = 0; i < outputSamples; i++)
        {
            double sourceIndex = i / _speed;
            int sourceIdx = (int)sourceIndex;
            double fraction = sourceIndex - sourceIdx;
            
            // Ensure we don't go out of bounds on source
            if (sourceIdx >= sourceSamplesAvailable)
                break;
            
            for (int ch = 0; ch < channels; ch++)
            {
                int outputIdx = offset + i * channels + ch;
                
                // Safety bounds check for output buffer
                if (outputIdx >= buffer.Length)
                    break;
                
                int sourcePos1 = sourceIdx * channels + ch;
                int sourcePos2 = (sourceIdx + 1) * channels + ch;
                
                // Safety bounds check for source buffer
                if (sourcePos1 >= sourceBuffer.Length)
                {
                    buffer[outputIdx] = 0;
                    continue;
                }
                
                if (sourceIdx + 1 < sourceSamplesAvailable && sourcePos2 < sourceBuffer.Length)
                {
                    float sample1 = sourceBuffer[sourcePos1];
                    float sample2 = sourceBuffer[sourcePos2];
                    buffer[outputIdx] = (float)(sample1 + fraction * (sample2 - sample1));
                }
                else
                {
                    buffer[outputIdx] = sourceBuffer[sourcePos1];
                }
            }
            actualOutputSamples++;
        }
        
        return actualOutputSamples * channels;
    }
}

