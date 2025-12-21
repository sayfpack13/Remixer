using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;

namespace Remixer.Core.Audio.Effects;

public class PitchShifter : IEffect
{
    private double _pitchShift = 0.0; // in semitones
    
    public bool IsEnabled { get; set; } = true;

    public double PitchShift
    {
        get => _pitchShift;
        set
        {
            if (value < -24 || value > 24)
                throw new ArgumentOutOfRangeException(nameof(value), "Pitch shift must be between -24 and 24 semitones");
            _pitchShift = value;
        }
    }

    public ISampleProvider Apply(ISampleProvider input)
    {
        if (!IsEnabled || Math.Abs(_pitchShift) < 0.01)
            return input;

        // Simple pitch shifting using resampling
        // Calculate the pitch factor (2^(semitones/12))
        double pitchFactor = Math.Pow(2.0, _pitchShift / 12.0);
        int newSampleRate = (int)(input.WaveFormat.SampleRate * pitchFactor);
        
        // Resample to change pitch
        var resampler = new PitchResampler(input, input.WaveFormat.SampleRate, newSampleRate);
        
        // Resample back to original rate to maintain tempo
        var backResampler = new PitchResampler(resampler, newSampleRate, input.WaveFormat.SampleRate);
        
        return backResampler;
    }
}

// Simple resampler for pitch shifting
internal class PitchResampler : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _sourceRate;
    private readonly int _targetRate;

    public PitchResampler(ISampleProvider source, int sourceRate, int targetRate)
    {
        _source = source;
        _sourceRate = sourceRate;
        _targetRate = targetRate;
        // ISampleProvider always works with IEEE Float format
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(targetRate, source.WaveFormat.Channels);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        if (_sourceRate == _targetRate)
            return _source.Read(buffer, offset, count);

        int channels = WaveFormat.Channels;
        int outputFrames = count / channels; // Number of output frames (count is total floats)
        
        // Calculate how many source frames we need for interpolation
        int sourceFrames = (int)Math.Ceiling(outputFrames * (double)_sourceRate / _targetRate) + 2; // +2 for interpolation safety
        int sourceFloats = sourceFrames * channels;
        
        float[] sourceBuffer = new float[sourceFloats];
        int sourceRead = _source.Read(sourceBuffer, 0, sourceFloats);
        
        if (sourceRead == 0)
            return 0;

        int sourceFramesRead = sourceRead / channels;
        
        // Linear interpolation resampling - iterate over FRAMES, not samples
        int outputSamplesWritten = 0;
        for (int frame = 0; frame < outputFrames; frame++)
        {
            double sourceFrameIndex = frame * (double)_sourceRate / _targetRate;
            int sourceFrameIdx = (int)sourceFrameIndex;
            double fraction = sourceFrameIndex - sourceFrameIdx;
            
            // Check if we have enough source data
            if (sourceFrameIdx >= sourceFramesRead)
                break;
            
            for (int ch = 0; ch < channels; ch++)
            {
                int outputIndex = offset + frame * channels + ch;
                int sourceIndex1 = sourceFrameIdx * channels + ch;
                
                // Safety bounds check for output buffer
                if (outputIndex >= buffer.Length)
                    break;
                
                // Safety bounds check for source buffer
                if (sourceIndex1 >= sourceBuffer.Length)
                {
                    buffer[outputIndex] = 0;
                    continue;
                }
                
                if (sourceFrameIdx + 1 < sourceFramesRead)
                {
                    int sourceIndex2 = (sourceFrameIdx + 1) * channels + ch;
                    if (sourceIndex2 < sourceBuffer.Length)
                    {
                        float sample1 = sourceBuffer[sourceIndex1];
                        float sample2 = sourceBuffer[sourceIndex2];
                        buffer[outputIndex] = (float)(sample1 + fraction * (sample2 - sample1));
                    }
                    else
                    {
                        buffer[outputIndex] = sourceBuffer[sourceIndex1];
                    }
                }
                else
                {
                    buffer[outputIndex] = sourceBuffer[sourceIndex1];
                }
            }
            outputSamplesWritten += channels;
        }
        
        return outputSamplesWritten;
    }
}

