using NAudio.Wave;
using System;

namespace Remixer.Core.Audio.Effects;

public class ChorusEffect : IEffect
{
    public bool IsEnabled { get; set; } = false;
    public double Rate { get; set; } = 1.0; // Hz (0.1-5)
    public double Depth { get; set; } = 0.5; // 0-1
    public double Mix { get; set; } = 0.5; // 0-1 (wet/dry balance)

    private float[]? _delayBuffer;
    private int _delayBufferSize;
    private int _writePosition;
    private double _lfoPhase;

    public ISampleProvider Apply(ISampleProvider input)
    {
        if (!IsEnabled)
            return input;

        // Chorus uses a delay of 10-30ms, we'll use 20ms as base
        _delayBufferSize = (int)(input.WaveFormat.SampleRate * 0.025); // 25ms max delay
        _delayBuffer = new float[_delayBufferSize * input.WaveFormat.Channels];
        _writePosition = 0;
        _lfoPhase = 0;

        return new ChorusSampleProvider(input, this);
    }

    private class ChorusSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly ChorusEffect _chorus;
        private readonly int _sampleRate;

        public ChorusSampleProvider(ISampleProvider source, ChorusEffect chorus)
        {
            _source = source;
            _chorus = chorus;
            _sampleRate = source.WaveFormat.SampleRate;
            WaveFormat = source.WaveFormat;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);
            
            if (!_chorus.IsEnabled || _chorus._delayBuffer == null)
                return samplesRead;

            int channels = WaveFormat.Channels;
            double rate = Math.Clamp(_chorus.Rate, 0.1, 5.0);
            double depth = Math.Clamp(_chorus.Depth, 0.0, 1.0);
            double mix = Math.Clamp(_chorus.Mix, 0.0, 1.0);
            double dryLevel = 1.0 - mix;
            double wetLevel = mix;

            // Base delay: 10ms, modulation range: 0-15ms
            double baseDelaySamples = _sampleRate * 0.01; // 10ms
            double maxModulationSamples = _sampleRate * 0.015; // 15ms

            for (int i = 0; i < samplesRead; i += channels)
            {
                // Update LFO phase
                double lfoValue = Math.Sin(2.0 * Math.PI * _chorus._lfoPhase);
                _chorus._lfoPhase += rate / _sampleRate;
                if (_chorus._lfoPhase >= 1.0)
                    _chorus._lfoPhase -= 1.0;

                // Calculate modulated delay
                double modulation = lfoValue * depth * maxModulationSamples;
                double delaySamples = baseDelaySamples + modulation;
                delaySamples = Math.Clamp(delaySamples, 0, _chorus._delayBufferSize - 1);

                for (int ch = 0; ch < channels; ch++)
                {
                    int idx = offset + i + ch;
                    
                    if (idx >= buffer.Length)
                        break;
                    
                    float inputSample = buffer[idx];
                    
                    // Read from delay buffer with interpolation
                    int delayInt = (int)delaySamples;
                    double delayFrac = delaySamples - delayInt;
                    int readPos1 = (_chorus._writePosition - delayInt + _chorus._delayBufferSize) % _chorus._delayBufferSize;
                    int readPos2 = (readPos1 - 1 + _chorus._delayBufferSize) % _chorus._delayBufferSize;
                    
                    int delayReadIdx1 = readPos1 * channels + ch;
                    int delayReadIdx2 = readPos2 * channels + ch;
                    
                    float delayedSample1 = 0;
                    float delayedSample2 = 0;
                    
                    if (delayReadIdx1 >= 0 && delayReadIdx1 < _chorus._delayBuffer.Length)
                        delayedSample1 = _chorus._delayBuffer[delayReadIdx1];
                    if (delayReadIdx2 >= 0 && delayReadIdx2 < _chorus._delayBuffer.Length)
                        delayedSample2 = _chorus._delayBuffer[delayReadIdx2];
                    
                    // Linear interpolation
                    float delayedSample = (float)(delayedSample1 * (1.0 - delayFrac) + delayedSample2 * delayFrac);
                    
                    // Mix dry and wet signals
                    buffer[idx] = (float)(inputSample * dryLevel + delayedSample * wetLevel);
                    
                    // Write to delay buffer
                    int delayWriteIdx = _chorus._writePosition * channels + ch;
                    if (delayWriteIdx >= 0 && delayWriteIdx < _chorus._delayBuffer.Length)
                    {
                        _chorus._delayBuffer[delayWriteIdx] = inputSample;
                    }
                }
                
                _chorus._writePosition = (_chorus._writePosition + 1) % _chorus._delayBufferSize;
            }

            return samplesRead;
        }
    }
}

