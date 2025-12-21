using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.Dsp;
using System;

namespace Remixer.Core.Audio.Effects;

public class FilterEffect : IEffect
{
    public bool IsEnabled { get; set; } = false;
    public double LowCut { get; set; } = 20.0; // Hz
    public double HighCut { get; set; } = 20000.0; // Hz
    public double LowGain { get; set; } = 0.0; // dB
    public double MidGain { get; set; } = 0.0; // dB
    public double HighGain { get; set; } = 0.0; // dB

    public ISampleProvider Apply(ISampleProvider input)
    {
        if (!IsEnabled)
            return input;

        return new FilterSampleProvider(input, this, input.WaveFormat.SampleRate);
    }

    private class FilterSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly FilterEffect _filter;
        private readonly int _sampleRate;
        private BiQuadFilter[]? _lowFilters;
        private BiQuadFilter[]? _midFilters;
        private BiQuadFilter[]? _highFilters;

        public FilterSampleProvider(ISampleProvider source, FilterEffect filter, int sampleRate)
        {
            _source = source;
            _filter = filter;
            _sampleRate = sampleRate;
            WaveFormat = source.WaveFormat;
            
            InitializeFilters();
        }

        private void InitializeFilters()
        {
            int channels = WaveFormat.Channels;
            _lowFilters = new BiQuadFilter[channels];
            _midFilters = new BiQuadFilter[channels];
            _highFilters = new BiQuadFilter[channels];

            for (int i = 0; i < channels; i++)
            {
                // Low shelf filter
                _lowFilters[i] = BiQuadFilter.LowShelf(_sampleRate, (float)_filter.LowCut, 1.0f, (float)_filter.LowGain);
                
                // High shelf filter
                _highFilters[i] = BiQuadFilter.HighShelf(_sampleRate, (float)_filter.HighCut, 1.0f, (float)_filter.HighGain);
                
                // Mid band (simplified - would use peaking filter in production)
                _midFilters[i] = BiQuadFilter.PeakingEQ(_sampleRate, 1000.0f, 1.0f, (float)_filter.MidGain);
            }
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);
            
            if (!_filter.IsEnabled)
                return samplesRead;

            int channels = WaveFormat.Channels;

            if (_lowFilters == null || _midFilters == null || _highFilters == null)
                return samplesRead;

            for (int i = 0; i < samplesRead; i += channels)
            {
                for (int ch = 0; ch < channels; ch++)
                {
                    int idx = offset + i + ch;
                    
                    // Safety bounds check
                    if (idx >= buffer.Length || ch >= _lowFilters.Length)
                        break;
                    
                    float sample = buffer[idx];
                    
                    // Apply filters
                    sample = _lowFilters[ch].Transform(sample);
                    sample = _midFilters[ch].Transform(sample);
                    sample = _highFilters[ch].Transform(sample);
                    
                    buffer[idx] = sample;
                }
            }

            return samplesRead;
        }
    }
}

