using NAudio.Wave;
using System;

namespace Remixer.Core.Audio.Effects;

public class GateEffect : IEffect
{
    public bool IsEnabled { get; set; } = false;
    public double Threshold { get; set; } = 0.1; // 0-1 (amplitude threshold)
    public double Ratio { get; set; } = 10.0; // 1-100 (gate ratio)
    public double Attack { get; set; } = 1.0; // ms (0.1 to 100)
    public double Release { get; set; } = 50.0; // ms (1 to 1000)
    public double Floor { get; set; } = 0.0; // 0-1 (minimum output level when gated)

    public ISampleProvider Apply(ISampleProvider input)
    {
        if (!IsEnabled)
            return input;

        return new GateSampleProvider(input, this);
    }

    private class GateSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly GateEffect _gate;
        private double _envelope = 0.0;
        private double _gateState = 0.0; // 0 = closed, 1 = open

        public GateSampleProvider(ISampleProvider source, GateEffect gate)
        {
            _source = source;
            _gate = gate;
            WaveFormat = source.WaveFormat;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);
            
            if (!_gate.IsEnabled)
                return samplesRead;

            double threshold = Math.Clamp(_gate.Threshold, 0.0, 1.0);
            double ratio = Math.Clamp(_gate.Ratio, 1.0, 100.0);
            double attack = Math.Clamp(_gate.Attack, 0.1, 100.0);
            double release = Math.Clamp(_gate.Release, 1.0, 1000.0);
            double floor = Math.Clamp(_gate.Floor, 0.0, 1.0);

            double sampleRate = WaveFormat.SampleRate;
            double attackCoeff = Math.Exp(-1.0 / (attack * sampleRate / 1000.0));
            double releaseCoeff = Math.Exp(-1.0 / (release * sampleRate / 1000.0));

            for (int i = 0; i < samplesRead; i++)
            {
                float sample = buffer[offset + i];
                double amplitude = Math.Abs(sample);

                // Update envelope follower
                if (amplitude > _envelope)
                {
                    // Attack
                    _envelope = amplitude + (_envelope - amplitude) * attackCoeff;
                }
                else
                {
                    // Release
                    _envelope = amplitude + (_envelope - amplitude) * releaseCoeff;
                }

                // Determine gate state
                if (_envelope > threshold)
                {
                    // Gate should be open
                    _gateState = 1.0 - (1.0 - _gateState) * attackCoeff;
                }
                else
                {
                    // Gate should be closed
                    double reduction = (threshold - _envelope) / threshold;
                    double gateReduction = 1.0 - (reduction / ratio);
                    _gateState = _gateState * releaseCoeff;
                    _gateState = Math.Max(_gateState, gateReduction);
                }

                // Apply gate
                double gateLevel = Math.Max(_gateState, floor);
                buffer[offset + i] = (float)(sample * gateLevel);
            }

            return samplesRead;
        }
    }
}

