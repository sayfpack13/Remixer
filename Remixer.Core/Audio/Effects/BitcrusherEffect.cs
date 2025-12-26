using NAudio.Wave;
using System;

namespace Remixer.Core.Audio.Effects;

public class BitcrusherEffect : IEffect
{
    public bool IsEnabled { get; set; } = false;
    public double BitDepth { get; set; } = 16.0; // 1-16 bits
    public double Downsample { get; set; } = 1.0; // 1.0 = no downsampling, higher = more downsampling
    public double Mix { get; set; } = 0.5; // 0-1 (wet/dry balance)

    public ISampleProvider Apply(ISampleProvider input)
    {
        if (!IsEnabled)
            return input;

        return new BitcrusherSampleProvider(input, this);
    }

    private class BitcrusherSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly BitcrusherEffect _bitcrusher;
        private int _sampleCounter = 0;
        private float _lastSample = 0;

        public BitcrusherSampleProvider(ISampleProvider source, BitcrusherEffect bitcrusher)
        {
            _source = source;
            _bitcrusher = bitcrusher;
            WaveFormat = source.WaveFormat;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);
            
            if (!_bitcrusher.IsEnabled)
                return samplesRead;

            double bitDepth = Math.Clamp(_bitcrusher.BitDepth, 1.0, 16.0);
            double downsample = Math.Max(1.0, _bitcrusher.Downsample);
            double mix = Math.Clamp(_bitcrusher.Mix, 0.0, 1.0);
            double dryLevel = 1.0 - mix;
            double wetLevel = mix;

            // Calculate quantization levels
            int levels = (int)Math.Pow(2, bitDepth);
            double step = 2.0 / levels;

            for (int i = 0; i < samplesRead; i++)
            {
                float original = buffer[offset + i];
                float processed = original;

                // Downsampling (sample rate reduction)
                if (downsample > 1.0)
                {
                    int downsampleInt = (int)downsample;
                    if (_sampleCounter % downsampleInt == 0)
                    {
                        _lastSample = original;
                    }
                    processed = _lastSample;
                    _sampleCounter++;
                }

                // Bit crushing (quantization)
                if (bitDepth < 16.0)
                {
                    // Quantize the sample
                    processed = (float)(Math.Round(processed / step) * step);
                    // Clamp to prevent overflow
                    processed = Math.Clamp(processed, -1.0f, 1.0f);
                }

                // Mix wet and dry
                buffer[offset + i] = (float)(original * dryLevel + processed * wetLevel);
            }

            return samplesRead;
        }
    }
}

