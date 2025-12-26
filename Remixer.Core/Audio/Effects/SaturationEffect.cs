using NAudio.Wave;
using System;

namespace Remixer.Core.Audio.Effects;

public class SaturationEffect : IEffect
{
    public bool IsEnabled { get; set; } = false;
    public double Drive { get; set; } = 0.5; // 0-1 (softer than distortion)
    public double Tone { get; set; } = 0.5; // 0-1 (high-frequency filtering)
    public double Mix { get; set; } = 0.5; // 0-1 (wet/dry balance)

    public ISampleProvider Apply(ISampleProvider input)
    {
        if (!IsEnabled)
            return input;

        return new SaturationSampleProvider(input, this);
    }

    private class SaturationSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly SaturationEffect _saturation;
        private float _lowPassState = 0;

        public SaturationSampleProvider(ISampleProvider source, SaturationEffect saturation)
        {
            _source = source;
            _saturation = saturation;
            WaveFormat = source.WaveFormat;
            _lowPassState = 0;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);
            
            if (!_saturation.IsEnabled)
                return samplesRead;

            double drive = Math.Clamp(_saturation.Drive, 0.0, 1.0);
            double tone = Math.Clamp(_saturation.Tone, 0.0, 1.0);
            double mix = Math.Clamp(_saturation.Mix, 0.0, 1.0);
            double dryLevel = 1.0 - mix;
            double wetLevel = mix;

            // Drive multiplier: 1x to 3x (softer than distortion's 10x)
            double driveMultiplier = 1.0 + drive * 2.0;

            // Tone control: low-pass filter coefficient
            double filterCoeff = tone * 0.95;

            for (int i = 0; i < samplesRead; i++)
            {
                float sample = buffer[offset + i];
                float original = sample;

                // Apply drive (soft saturation)
                sample *= (float)driveMultiplier;

                // Soft saturation using tanh (smoother than hard clipping)
                sample = (float)Math.Tanh(sample);

                // Apply tone control (low-pass filter)
                _lowPassState = (float)(_lowPassState + filterCoeff * (sample - _lowPassState));
                sample = _lowPassState;

                // Mix wet and dry
                buffer[offset + i] = (float)(original * dryLevel + sample * wetLevel);
            }

            return samplesRead;
        }
    }
}

