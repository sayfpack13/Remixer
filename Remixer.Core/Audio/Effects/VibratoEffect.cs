using NAudio.Wave;
using System;

namespace Remixer.Core.Audio.Effects;

public class VibratoEffect : IEffect
{
    public bool IsEnabled { get; set; } = false;
    public double Rate { get; set; } = 5.0; // Hz (0.1 to 10)
    public double Depth { get; set; } = 0.1; // Semitones (0 to 2)
    public double Mix { get; set; } = 1.0; // 0-1 (wet/dry balance)

    private double _phase = 0.0;

    public ISampleProvider Apply(ISampleProvider input)
    {
        if (!IsEnabled)
            return input;

        return new VibratoSampleProvider(input, this);
    }

    private class VibratoSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly VibratoEffect _vibrato;
        private double _phase = 0.0;
        private float[]? _delayBuffer;
        private int _delayBufferSize;
        private int _writePosition;

        public VibratoSampleProvider(ISampleProvider source, VibratoEffect vibrato)
        {
            _source = source;
            _vibrato = vibrato;
            WaveFormat = source.WaveFormat;
            
            // Create delay buffer for pitch modulation (max depth = 2 semitones = ~12% delay)
            int maxDelaySamples = (int)(source.WaveFormat.SampleRate * 0.02); // 20ms max delay
            _delayBufferSize = maxDelaySamples * source.WaveFormat.Channels;
            _delayBuffer = new float[_delayBufferSize];
            _writePosition = 0;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);
            
            if (!_vibrato.IsEnabled || _delayBuffer == null)
                return samplesRead;

            double rate = Math.Clamp(_vibrato.Rate, 0.1, 10.0);
            double depth = Math.Clamp(_vibrato.Depth, 0.0, 2.0);
            double mix = Math.Clamp(_vibrato.Mix, 0.0, 1.0);
            double dryLevel = 1.0 - mix;
            double wetLevel = mix;

            int channels = WaveFormat.Channels;
            double sampleRate = WaveFormat.SampleRate;
            double phaseIncrement = 2.0 * Math.PI * rate / sampleRate;

            // Convert depth from semitones to delay time variation
            // 1 semitone = ~5.9% pitch change = ~5.9% delay variation
            double maxDelayVariation = depth * 0.059; // Max delay variation in seconds
            int maxDelaySamples = (int)(sampleRate * maxDelayVariation);
            int baseDelaySamples = maxDelaySamples / 2; // Center delay

            for (int i = 0; i < samplesRead; i += channels)
            {
                // Update phase for LFO
                _phase += phaseIncrement;
                if (_phase > 2.0 * Math.PI)
                    _phase -= 2.0 * Math.PI;

                // Calculate delay offset using sine wave
                double lfo = Math.Sin(_phase);
                int delayOffset = baseDelaySamples + (int)(lfo * maxDelaySamples / 2);
                delayOffset = Math.Clamp(delayOffset, 0, maxDelaySamples);

                for (int ch = 0; ch < channels; ch++)
                {
                    int sampleIndex = offset + i + ch;
                    float original = buffer[sampleIndex];

                    // Write to delay buffer
                    int writeIndex = (_writePosition + ch) % _delayBufferSize;
                    _delayBuffer[writeIndex] = original;

                    // Read from delay buffer with variable delay
                    int readIndex = (_writePosition - delayOffset * channels + ch + _delayBufferSize) % _delayBufferSize;
                    float delayed = _delayBuffer[readIndex];

                    // Mix wet and dry
                    buffer[sampleIndex] = (float)(original * dryLevel + delayed * wetLevel);
                }

                _writePosition = (_writePosition + channels) % _delayBufferSize;
            }

            return samplesRead;
        }
    }
}

