using NAudio.Wave;
using System;

namespace Remixer.Core.Audio.Effects;

public class PhaserEffect : IEffect
{
    public bool IsEnabled { get; set; } = false;
    public double Rate { get; set; } = 0.5; // Hz (0.1-5)
    public double Depth { get; set; } = 0.8; // 0-1
    public double Feedback { get; set; } = 0.3; // -1 to 1
    public double Mix { get; set; } = 0.5; // 0-1 (wet/dry balance)

    private double _lfoPhase;
    private readonly double[] _allPassX1 = new double[6]; // Previous input for each stage
    private readonly double[] _allPassY1 = new double[6]; // Previous output for each stage
    private float _feedbackSample;

    public ISampleProvider Apply(ISampleProvider input)
    {
        if (!IsEnabled)
            return input;

        _lfoPhase = 0;
        Array.Fill(_allPassX1, 0.0);
        Array.Fill(_allPassY1, 0.0);
        _feedbackSample = 0;

        return new PhaserSampleProvider(input, this);
    }

    private class PhaserSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly PhaserEffect _phaser;
        private readonly int _sampleRate;

        public PhaserSampleProvider(ISampleProvider source, PhaserEffect phaser)
        {
            _source = source;
            _phaser = phaser;
            _sampleRate = source.WaveFormat.SampleRate;
            WaveFormat = source.WaveFormat;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);
            
            if (!_phaser.IsEnabled)
                return samplesRead;

            int channels = WaveFormat.Channels;
            double rate = Math.Clamp(_phaser.Rate, 0.1, 5.0);
            double depth = Math.Clamp(_phaser.Depth, 0.0, 1.0);
            double feedback = Math.Clamp(_phaser.Feedback, -1.0, 1.0);
            double mix = Math.Clamp(_phaser.Mix, 0.0, 1.0);
            double dryLevel = 1.0 - mix;
            double wetLevel = mix;

            // Phaser frequency range: 200Hz to 2000Hz
            double minFreq = 200.0;
            double maxFreq = 2000.0;
            double freqRange = maxFreq - minFreq;

            for (int i = 0; i < samplesRead; i += channels)
            {
                // Update LFO phase
                double lfoValue = Math.Sin(2.0 * Math.PI * _phaser._lfoPhase);
                _phaser._lfoPhase += rate / _sampleRate;
                if (_phaser._lfoPhase >= 1.0)
                    _phaser._lfoPhase -= 1.0;

                // Calculate modulated frequency
                double modFreq = minFreq + (lfoValue * 0.5 + 0.5) * freqRange * depth;

                for (int ch = 0; ch < channels; ch++)
                {
                    int idx = offset + i + ch;
                    
                    if (idx >= buffer.Length)
                        break;
                    
                    float inputSample = buffer[idx];
                    
                    // Add feedback
                    float phaserInput = inputSample + _phaser._feedbackSample * (float)feedback;
                    
                    // Apply 6-stage all-pass filter
                    double output = phaserInput;
                    for (int stage = 0; stage < 6; stage++)
                    {
                        // All-pass filter coefficient
                        double coeff = (2.0 * Math.PI * modFreq) / _sampleRate;
                        double a = (1.0 - Math.Tan(coeff / 2.0)) / (1.0 + Math.Tan(coeff / 2.0));
                        
                        // All-pass filter: y[n] = a * x[n] + x[n-1] - a * y[n-1]
                        double newOutput = a * output + _phaser._allPassX1[stage] - a * _phaser._allPassY1[stage];
                        _phaser._allPassX1[stage] = output;
                        _phaser._allPassY1[stage] = newOutput;
                        output = newOutput;
                    }
                    
                    float phaserOutput = (float)output;
                    _phaser._feedbackSample = phaserOutput;
                    
                    // Mix dry and wet signals
                    buffer[idx] = (float)(inputSample * dryLevel + phaserOutput * wetLevel);
                }
            }

            return samplesRead;
        }
    }
}

