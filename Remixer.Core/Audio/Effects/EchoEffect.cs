using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;

namespace Remixer.Core.Audio.Effects;

public class EchoEffect : IEffect
{
    public bool IsEnabled { get; set; } = false;
    public double Delay { get; set; } = 0.2; // seconds
    public double Feedback { get; set; } = 0.3;
    public double WetLevel { get; set; } = 0.3;

    private float[]? _delayBuffer;
    private int _delayBufferSize;
    private int _writePosition;

    public ISampleProvider Apply(ISampleProvider input)
    {
        if (!IsEnabled)
            return input;

        // Ensure delay is at least a small value to prevent divide by zero
        var effectiveDelay = Math.Max(Delay, 0.001); // Minimum 1ms delay
        _delayBufferSize = (int)(input.WaveFormat.SampleRate * effectiveDelay);
        
        // Ensure buffer size is at least 1 to prevent divide by zero in modulo operations
        if (_delayBufferSize < 1)
            _delayBufferSize = 1;
            
        _delayBuffer = new float[_delayBufferSize * input.WaveFormat.Channels];
        _writePosition = 0;

        return new EchoSampleProvider(input, this);
    }

    private class EchoSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly EchoEffect _echo;
        private readonly float[] _readBuffer;

        public EchoSampleProvider(ISampleProvider source, EchoEffect echo)
        {
            _source = source;
            _echo = echo;
            _readBuffer = new float[8192];
            WaveFormat = source.WaveFormat;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);
            
            if (!_echo.IsEnabled)
                return samplesRead;

            int channels = WaveFormat.Channels;
            int delaySamples = _echo._delayBufferSize;
            double feedback = _echo.Feedback;
            double wetLevel = _echo.WetLevel;
            double dryLevel = 1.0 - wetLevel;

            if (_echo._delayBuffer == null || delaySamples == 0 || _echo._delayBufferSize == 0)
                return samplesRead;

            for (int i = 0; i < samplesRead; i += channels)
            {
                for (int ch = 0; ch < channels; ch++)
                {
                    int idx = offset + i + ch;
                    
                    // Safety bounds check
                    if (idx >= buffer.Length)
                        break;
                    
                    float inputSample = buffer[idx];
                    
                    // Read delayed sample with bounds checking
                    // _delayBufferSize is guaranteed to be >= 1 from Apply method
                    int readPos = (_echo._writePosition - delaySamples + _echo._delayBufferSize) % _echo._delayBufferSize;
                    int delayReadIdx = readPos * channels + ch;
                    float delayedSample = 0;
                    if (delayReadIdx >= 0 && delayReadIdx < _echo._delayBuffer.Length)
                    {
                        delayedSample = _echo._delayBuffer[delayReadIdx];
                    }
                    
                    // Mix dry and wet signals
                    buffer[idx] = (float)(inputSample * dryLevel + delayedSample * wetLevel);
                    
                    // Write to delay buffer with feedback (with bounds checking)
                    int delayWriteIdx = _echo._writePosition * channels + ch;
                    if (delayWriteIdx >= 0 && delayWriteIdx < _echo._delayBuffer.Length)
                    {
                        _echo._delayBuffer[delayWriteIdx] = inputSample + delayedSample * (float)feedback;
                    }
                }
                
                // _delayBufferSize is guaranteed to be >= 1, so modulo is safe
                _echo._writePosition = (_echo._writePosition + 1) % _echo._delayBufferSize;
            }

            return samplesRead;
        }
    }
}

