using NAudio.Wave;
using System;

namespace Remixer.Core.Audio.Effects;

public class VolumeEffect : IEffect
{
    public bool IsEnabled { get; set; } = true;
    public double Volume { get; set; } = 1.0;

    public ISampleProvider Apply(ISampleProvider input)
    {
        if (!IsEnabled || Math.Abs(Volume - 1.0) < 0.01)
            return input;

        return new VolumeSampleProvider(input, Volume);
    }
}

internal class VolumeSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly double _volume;

    public VolumeSampleProvider(ISampleProvider source, double volume)
    {
        _source = source;
        _volume = volume;
        WaveFormat = source.WaveFormat;
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = _source.Read(buffer, offset, count);
        
        if (Math.Abs(_volume - 1.0) < 0.01)
            return samplesRead;

        for (int i = 0; i < samplesRead; i++)
        {
            int idx = offset + i;
            if (idx >= buffer.Length)
                break;
            buffer[idx] *= (float)_volume;
        }
        
        return samplesRead;
    }
}

