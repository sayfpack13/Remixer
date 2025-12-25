using NAudio.Wave;
using System;

namespace Remixer.Core.Audio.Effects;

public class TremoloEffect : IEffect
{
    public bool IsEnabled { get; set; } = false;
    public double Rate { get; set; } = 3.0; // Hz (0.1-10)
    public double Depth { get; set; } = 0.5; // 0-1

    private double _lfoPhase;

    public ISampleProvider Apply(ISampleProvider input)
    {
        if (!IsEnabled)
            return input;

        _lfoPhase = 0;

        return new TremoloSampleProvider(input, this);
    }

    private class TremoloSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly TremoloEffect _tremolo;
        private readonly int _sampleRate;

        public TremoloSampleProvider(ISampleProvider source, TremoloEffect tremolo)
        {
            _source = source;
            _tremolo = tremolo;
            _sampleRate = source.WaveFormat.SampleRate;
            WaveFormat = source.WaveFormat;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);
            
            if (!_tremolo.IsEnabled)
                return samplesRead;

            double rate = Math.Clamp(_tremolo.Rate, 0.1, 10.0);
            double depth = Math.Clamp(_tremolo.Depth, 0.0, 1.0);

            for (int i = 0; i < samplesRead; i++)
            {
                int idx = offset + i;
                
                if (idx >= buffer.Length)
                    break;
                
                // Update LFO phase
                double lfoValue = Math.Sin(2.0 * Math.PI * _tremolo._lfoPhase);
                _tremolo._lfoPhase += rate / _sampleRate;
                if (_tremolo._lfoPhase >= 1.0)
                    _tremolo._lfoPhase -= 1.0;

                // Calculate modulation: 1.0 (no change) to (1.0 - depth) (maximum reduction)
                // LFO ranges from -1 to 1, we want it to range from (1-depth) to 1
                double modulation = 1.0 - depth * (1.0 - lfoValue) * 0.5;
                
                // Apply amplitude modulation
                buffer[idx] *= (float)modulation;
            }

            return samplesRead;
        }
    }
}

