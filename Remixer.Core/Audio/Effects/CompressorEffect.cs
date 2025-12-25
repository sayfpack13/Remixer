using NAudio.Wave;
using System;

namespace Remixer.Core.Audio.Effects;

public class CompressorEffect : IEffect
{
    public bool IsEnabled { get; set; } = false;
    public double Threshold { get; set; } = -12.0; // dB (-60 to 0)
    public double Ratio { get; set; } = 4.0; // 1:1 to 20:1
    public double Attack { get; set; } = 10.0; // ms (0.1-100)
    public double Release { get; set; } = 100.0; // ms (10-1000)
    public double MakeupGain { get; set; } = 0.0; // dB (0-12)

    public ISampleProvider Apply(ISampleProvider input)
    {
        if (!IsEnabled)
            return input;

        return new CompressorSampleProvider(input, this);
    }

    private class CompressorSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly CompressorEffect _compressor;
        private readonly int _sampleRate;
        private double _envelopeFollower;
        private double _gainReduction;

        public CompressorSampleProvider(ISampleProvider source, CompressorEffect compressor)
        {
            _source = source;
            _compressor = compressor;
            _sampleRate = source.WaveFormat.SampleRate;
            WaveFormat = source.WaveFormat;
            _envelopeFollower = 0;
            _gainReduction = 1.0;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);
            
            if (!_compressor.IsEnabled)
                return samplesRead;

            double threshold = Math.Clamp(_compressor.Threshold, -60.0, 0.0);
            double ratio = Math.Clamp(_compressor.Ratio, 1.0, 20.0);
            double attack = Math.Clamp(_compressor.Attack, 0.1, 100.0);
            double release = Math.Clamp(_compressor.Release, 10.0, 1000.0);
            double makeupGain = Math.Clamp(_compressor.MakeupGain, 0.0, 12.0);

            // Convert threshold from dB to linear
            double thresholdLinear = Math.Pow(10.0, threshold / 20.0);

            // Attack and release coefficients (time constants)
            double attackCoeff = Math.Exp(-1.0 / (attack * _sampleRate / 1000.0));
            double releaseCoeff = Math.Exp(-1.0 / (release * _sampleRate / 1000.0));

            // Makeup gain in linear
            double makeupGainLinear = Math.Pow(10.0, makeupGain / 20.0);

            for (int i = 0; i < samplesRead; i++)
            {
                int idx = offset + i;
                
                if (idx >= buffer.Length)
                    break;
                
                float inputSample = buffer[idx];
                
                // Calculate input level (RMS-like, using absolute value)
                double inputLevel = Math.Abs(inputSample);
                
                // Envelope follower (smooth level detection)
                if (inputLevel > _envelopeFollower)
                {
                    // Attack
                    _envelopeFollower = inputLevel + (_envelopeFollower - inputLevel) * attackCoeff;
                }
                else
                {
                    // Release
                    _envelopeFollower = inputLevel + (_envelopeFollower - inputLevel) * releaseCoeff;
                }
                
                // Calculate gain reduction
                if (_envelopeFollower > thresholdLinear)
                {
                    // Signal is above threshold, apply compression
                    double overThreshold = _envelopeFollower / thresholdLinear;
                    double compressedLevel = thresholdLinear * (1.0 + (overThreshold - 1.0) / ratio);
                    _gainReduction = compressedLevel / _envelopeFollower;
                }
                else
                {
                    // Signal is below threshold, no compression
                    _gainReduction = 1.0;
                }
                
                // Apply gain reduction and makeup gain
                buffer[idx] = (float)(inputSample * _gainReduction * makeupGainLinear);
            }

            return samplesRead;
        }
    }
}

