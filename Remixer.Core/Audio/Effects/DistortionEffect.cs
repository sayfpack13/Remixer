using NAudio.Wave;
using System;

namespace Remixer.Core.Audio.Effects;

public class DistortionEffect : IEffect
{
    public bool IsEnabled { get; set; } = false;
    public double Drive { get; set; } = 0.5; // 0-1
    public double Tone { get; set; } = 0.5; // 0-1 (high-frequency filtering)
    public double Mix { get; set; } = 0.5; // 0-1 (wet/dry balance)

    public ISampleProvider Apply(ISampleProvider input)
    {
        if (!IsEnabled)
            return input;

        return new DistortionSampleProvider(input, this);
    }

    private class DistortionSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly DistortionEffect _distortion;
        private float _lowPassState;

        public DistortionSampleProvider(ISampleProvider source, DistortionEffect distortion)
        {
            _source = source;
            _distortion = distortion;
            WaveFormat = source.WaveFormat;
            _lowPassState = 0;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);
            
            if (!_distortion.IsEnabled)
                return samplesRead;

            double drive = Math.Clamp(_distortion.Drive, 0.0, 1.0);
            double tone = Math.Clamp(_distortion.Tone, 0.0, 1.0);
            double mix = Math.Clamp(_distortion.Mix, 0.0, 1.0);
            double dryLevel = 1.0 - mix;
            double wetLevel = mix;

            // Drive multiplier: 1x to 10x
            double driveMultiplier = 1.0 + drive * 9.0;

            // Tone control: low-pass filter coefficient (0 = no filtering, 1 = heavy filtering)
            double filterCoeff = tone * 0.99; // 0 to 0.99

            for (int i = 0; i < samplesRead; i++)
            {
                int idx = offset + i;
                
                if (idx >= buffer.Length)
                    break;
                
                float inputSample = buffer[idx];
                float drySample = inputSample;
                
                // Apply drive (amplify before distortion)
                float drivenSample = inputSample * (float)driveMultiplier;
                
                // Apply waveshaping distortion (tanh for smooth saturation)
                float distortedSample = (float)Math.Tanh(drivenSample);
                
                // Apply tone control (low-pass filter)
                _lowPassState += (float)(filterCoeff * (distortedSample - _lowPassState));
                float filteredSample = (float)(distortedSample * (1.0 - filterCoeff) + _lowPassState * filterCoeff);
                
                // Mix dry and wet signals
                buffer[idx] = (float)(drySample * dryLevel + filteredSample * wetLevel);
            }

            return samplesRead;
        }
    }
}

