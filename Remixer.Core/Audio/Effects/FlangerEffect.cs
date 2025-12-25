using NAudio.Wave;
using System;

namespace Remixer.Core.Audio.Effects;

public class FlangerEffect : IEffect
{
    public bool IsEnabled { get; set; } = false;
    public double Delay { get; set; } = 0.005; // seconds (0-20ms)
    public double Rate { get; set; } = 0.5; // Hz (0.1-5)
    public double Depth { get; set; } = 0.8; // 0-1
    public double Feedback { get; set; } = 0.3; // -1 to 1
    public double Mix { get; set; } = 0.5; // 0-1 (wet/dry balance)

    private float[]? _delayBuffer;
    private int _delayBufferSize;
    private int _writePosition;
    private double _lfoPhase;

    public ISampleProvider Apply(ISampleProvider input)
    {
        if (!IsEnabled)
            return input;

        // Flanger uses short delays: 0-20ms
        double maxDelay = Math.Max(0.001, Math.Min(0.02, Delay)); // Clamp to 1-20ms
        _delayBufferSize = (int)(input.WaveFormat.SampleRate * maxDelay * 1.5); // Add headroom for modulation
        if (_delayBufferSize < 1)
            _delayBufferSize = 1;
        _delayBuffer = new float[_delayBufferSize * input.WaveFormat.Channels];
        _writePosition = 0;
        _lfoPhase = 0;

        return new FlangerSampleProvider(input, this);
    }

    private class FlangerSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly FlangerEffect _flanger;
        private readonly int _sampleRate;

        public FlangerSampleProvider(ISampleProvider source, FlangerEffect flanger)
        {
            _source = source;
            _flanger = flanger;
            _sampleRate = source.WaveFormat.SampleRate;
            WaveFormat = source.WaveFormat;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);
            
            if (!_flanger.IsEnabled || _flanger._delayBuffer == null || _flanger._delayBufferSize == 0)
                return samplesRead;

            int channels = WaveFormat.Channels;
            double baseDelay = Math.Clamp(_flanger.Delay, 0.001, 0.02);
            double rate = Math.Clamp(_flanger.Rate, 0.1, 5.0);
            double depth = Math.Clamp(_flanger.Depth, 0.0, 1.0);
            double feedback = Math.Clamp(_flanger.Feedback, -1.0, 1.0);
            double mix = Math.Clamp(_flanger.Mix, 0.0, 1.0);
            double dryLevel = 1.0 - mix;
            double wetLevel = mix;

            double baseDelaySamples = _sampleRate * baseDelay;
            double maxModulationSamples = _sampleRate * baseDelay * 0.5; // 50% modulation range

            for (int i = 0; i < samplesRead; i += channels)
            {
                // Update LFO phase
                double lfoValue = Math.Sin(2.0 * Math.PI * _flanger._lfoPhase);
                _flanger._lfoPhase += rate / _sampleRate;
                if (_flanger._lfoPhase >= 1.0)
                    _flanger._lfoPhase -= 1.0;

                // Calculate modulated delay
                double modulation = lfoValue * depth * maxModulationSamples;
                double delaySamples = baseDelaySamples + modulation;
                delaySamples = Math.Clamp(delaySamples, 0, _flanger._delayBufferSize - 1);

                for (int ch = 0; ch < channels; ch++)
                {
                    int idx = offset + i + ch;
                    
                    if (idx >= buffer.Length)
                        break;
                    
                    float inputSample = buffer[idx];
                    
                    // Read from delay buffer with interpolation
                    int delayInt = (int)delaySamples;
                    double delayFrac = delaySamples - delayInt;
                    int readPos1 = (_flanger._writePosition - delayInt + _flanger._delayBufferSize) % _flanger._delayBufferSize;
                    int readPos2 = (readPos1 - 1 + _flanger._delayBufferSize) % _flanger._delayBufferSize;
                    
                    int delayReadIdx1 = readPos1 * channels + ch;
                    int delayReadIdx2 = readPos2 * channels + ch;
                    
                    float delayedSample1 = 0;
                    float delayedSample2 = 0;
                    
                    if (delayReadIdx1 >= 0 && delayReadIdx1 < _flanger._delayBuffer.Length)
                        delayedSample1 = _flanger._delayBuffer[delayReadIdx1];
                    if (delayReadIdx2 >= 0 && delayReadIdx2 < _flanger._delayBuffer.Length)
                        delayedSample2 = _flanger._delayBuffer[delayReadIdx2];
                    
                    // Linear interpolation
                    float delayedSample = (float)(delayedSample1 * (1.0 - delayFrac) + delayedSample2 * delayFrac);
                    
                    // Mix dry and wet signals
                    buffer[idx] = (float)(inputSample * dryLevel + delayedSample * wetLevel);
                    
                    // Write to delay buffer with feedback
                    int delayWriteIdx = _flanger._writePosition * channels + ch;
                    if (delayWriteIdx >= 0 && delayWriteIdx < _flanger._delayBuffer.Length)
                    {
                        _flanger._delayBuffer[delayWriteIdx] = inputSample + delayedSample * (float)feedback;
                    }
                }
                
                _flanger._writePosition = (_flanger._writePosition + 1) % _flanger._delayBufferSize;
            }

            return samplesRead;
        }
    }
}

