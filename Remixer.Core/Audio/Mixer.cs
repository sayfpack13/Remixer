using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Remixer.Core.Audio;

public class Mixer : IDisposable
{
    private readonly List<MixerTrack> _tracks = new();
    private bool _disposed = false;

    public void AddTrack(WaveStream audioStream, double volume = 1.0, double pan = 0.0)
    {
        var track = new MixerTrack
        {
            Stream = audioStream,
            Volume = volume,
            Pan = pan
        };
        _tracks.Add(track);
    }

    public void RemoveTrack(int index)
    {
        if (index >= 0 && index < _tracks.Count)
        {
            _tracks[index].Stream?.Dispose();
            _tracks.RemoveAt(index);
        }
    }

    public void SetTrackVolume(int index, double volume)
    {
        if (index >= 0 && index < _tracks.Count)
        {
            _tracks[index].Volume = Math.Clamp(volume, 0.0, 1.0);
        }
    }

    public void SetTrackPan(int index, double pan)
    {
        if (index >= 0 && index < _tracks.Count)
        {
            _tracks[index].Pan = Math.Clamp(pan, -1.0, 1.0);
        }
    }

    public ISampleProvider Mix()
    {
        if (_tracks.Count == 0)
            throw new InvalidOperationException("No tracks to mix");

        if (_tracks.Count == 1)
        {
            var track = _tracks[0];
            var sampleProvider = new WaveToSampleProvider(track.Stream);
            var volumeProvider = new VolumeSampleProvider(sampleProvider) { Volume = (float)track.Volume };
            return volumeProvider;
        }

        // Mix multiple tracks
        var mixers = new List<ISampleProvider>();
        
        foreach (var track in _tracks)
        {
            var sampleProvider = new WaveToSampleProvider(track.Stream);
            var volumeProvider = new VolumeSampleProvider(sampleProvider) { Volume = (float)track.Volume };
            
            // Apply panning
            if (Math.Abs(track.Pan) > 0.01)
            {
                var panProvider = new PanningSampleProvider(volumeProvider);
                panProvider.Pan = (float)track.Pan;
                mixers.Add(panProvider);
            }
            else
            {
                mixers.Add(volumeProvider);
            }
        }

        return new MixingSampleProvider(mixers);
    }

    public IWaveProvider MixToWaveProvider()
    {
        var provider = Mix();
        return new SampleToWaveProvider(provider);
    }

    public int TrackCount => _tracks.Count;

    public void Dispose()
    {
        if (!_disposed)
        {
            foreach (var track in _tracks)
            {
                track.Stream?.Dispose();
            }
            _tracks.Clear();
            _disposed = true;
        }
    }

    private class MixerTrack
    {
        public WaveStream Stream { get; set; } = null!;
        public double Volume { get; set; } = 1.0;
        public double Pan { get; set; } = 0.0;
    }
}

