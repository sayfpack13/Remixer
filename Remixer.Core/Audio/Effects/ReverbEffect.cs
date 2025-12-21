using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;

namespace Remixer.Core.Audio.Effects;

public class ReverbEffect : IEffect
{
    public bool IsEnabled { get; set; } = false;
    public double RoomSize { get; set; } = 0.5;
    public double Damping { get; set; } = 0.5;
    public double WetLevel { get; set; } = 0.3;

    private float[]? _delayBuffer;
    private int _delayBufferSize;
    private int _writePosition;
    private readonly Random _random = new();

    public ISampleProvider Apply(ISampleProvider input)
    {
        if (!IsEnabled)
            return input;

        _delayBufferSize = (int)(input.WaveFormat.SampleRate * 0.1); // 100ms delay
        _delayBuffer = new float[_delayBufferSize * input.WaveFormat.Channels];
        _writePosition = 0;

        return new ReverbSampleProvider(input, this);
    }

    private class ReverbSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly ReverbEffect _reverb;
        private readonly float[] _readBuffer;

        public ReverbSampleProvider(ISampleProvider source, ReverbEffect reverb)
        {
            _source = source;
            _reverb = reverb;
            _readBuffer = new float[8192];
            WaveFormat = source.WaveFormat;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);
            
            if (!_reverb.IsEnabled)
                return samplesRead;

            int channels = WaveFormat.Channels;
            double roomSize = _reverb.RoomSize;
            double damping = _reverb.Damping;
            double wetLevel = _reverb.WetLevel;
            double dryLevel = 1.0 - wetLevel;

            if (_reverb._delayBuffer == null)
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
                    
                    // Simple reverb using delay buffer with bounds checking
                    int readPos = (_reverb._writePosition - (int)(_reverb._delayBufferSize * roomSize) + _reverb._delayBufferSize) % _reverb._delayBufferSize;
                    int delayReadIdx = readPos * channels + ch;
                    float delayedSample = 0;
                    if (delayReadIdx >= 0 && delayReadIdx < _reverb._delayBuffer.Length)
                    {
                        delayedSample = _reverb._delayBuffer[delayReadIdx] * (float)damping;
                    }
                    
                    // Mix dry and wet signals
                    buffer[idx] = (float)(inputSample * dryLevel + delayedSample * wetLevel);
                    
                    // Write to delay buffer with bounds checking
                    int delayWriteIdx = _reverb._writePosition * channels + ch;
                    if (delayWriteIdx >= 0 && delayWriteIdx < _reverb._delayBuffer.Length)
                    {
                        _reverb._delayBuffer[delayWriteIdx] = inputSample + delayedSample * (float)roomSize;
                    }
                }
                
                _reverb._writePosition = (_reverb._writePosition + 1) % _reverb._delayBufferSize;
            }

            return samplesRead;
        }
    }
}

