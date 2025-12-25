using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Remixer.Core.Audio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Remixer.Core.AI;

public class AudioAnalyzer
{
    // Cache for analysis results (key: file path + modification time)
    private static readonly Dictionary<string, AudioFeatures> _cache = new();
    private static readonly object _cacheLock = new object();
    
    public AudioFeatures Analyze(string filePath)
    {
        // Check cache first
        var cacheKey = GetCacheKey(filePath);
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }
        }
        
        using var stream = AudioFileHandler.LoadAudioFile(filePath);
        var features = Analyze(stream);
        
        // Cache the result
        lock (_cacheLock)
        {
            _cache[cacheKey] = features;
            // Limit cache size to prevent memory issues
            if (_cache.Count > 10)
            {
                var firstKey = _cache.Keys.First();
                _cache.Remove(firstKey);
            }
        }
        
        return features;
    }
    
    private static string GetCacheKey(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            return $"{filePath}|{fileInfo.LastWriteTime.Ticks}|{fileInfo.Length}";
        }
        catch
        {
            return filePath;
        }
    }
    
    public static void ClearCache()
    {
        lock (_cacheLock)
        {
            _cache.Clear();
        }
    }

    public AudioFeatures Analyze(WaveStream audioStream)
    {
        var features = new AudioFeatures
        {
            SampleRate = audioStream.WaveFormat.SampleRate,
            Channels = audioStream.WaveFormat.Channels,
            Duration = audioStream.TotalTime.TotalSeconds
        };

        // Convert to samples for analysis
        var sampleProvider = new WaveToSampleProvider(audioStream);
        var samples = ReadAllSamples(sampleProvider);

        if (samples.Length == 0)
            return features;

        // Calculate BPM (simplified - uses autocorrelation)
        features.BPM = EstimateBPM(samples, audioStream.WaveFormat.SampleRate);

        // Calculate energy (RMS)
        features.RMS = CalculateRMS(samples);
        features.Energy = Math.Min(features.RMS * 10, 1.0); // Normalize to 0-1

        // Calculate spectral features
        var fftSize = 2048;
        var fftSamples = samples.Take(fftSize).ToArray();
        if (fftSamples.Length == fftSize)
        {
            features.SpectralCentroid = CalculateSpectralCentroid(fftSamples, audioStream.WaveFormat.SampleRate);
            features.SpectralRolloff = CalculateSpectralRolloff(fftSamples, audioStream.WaveFormat.SampleRate);
        }

        // Calculate zero crossing rate
        features.ZeroCrossingRate = CalculateZeroCrossingRate(samples);

        // Estimate genre (simplified heuristic)
        features.Genre = EstimateGenre(features);

        return features;
    }

    private float[] ReadAllSamples(ISampleProvider provider)
    {
        // Limit to first 5 seconds and downsample for faster analysis
        // Use 22kHz instead of 44kHz for analysis (still accurate enough)
        const int maxSamples = 22050 * 5; // 5 seconds at 22kHz (downsampled)
        var samples = new List<float>();
        var buffer = new float[8192];
        int samplesRead;
        int totalSamples = 0;
        int skipFactor = provider.WaveFormat.SampleRate > 22050 ? 2 : 1; // Downsample if > 22kHz

        while ((samplesRead = provider.Read(buffer, 0, buffer.Length)) > 0 && totalSamples < maxSamples)
        {
            int samplesToAdd = Math.Min(samplesRead / skipFactor, maxSamples - totalSamples);
            for (int i = 0; i < samplesRead; i += skipFactor)
            {
                if (totalSamples >= maxSamples) break;
                samples.Add(buffer[i]);
                totalSamples++;
            }
            
            if (totalSamples >= maxSamples)
                break;
        }

        return samples.ToArray();
    }

    private double EstimateBPM(float[] samples, int sampleRate)
    {
        // Optimized BPM detection - use fewer samples and larger step size for speed
        // Limit analysis to reasonable BPM range (60-200 BPM)
        
        if (samples.Length < sampleRate) // Need at least 1 second
            return 120.0; // Default BPM
        
        // Use only first 3 seconds for faster analysis
        int analysisLength = Math.Min(samples.Length, sampleRate * 3);
        var analysisSamples = new float[analysisLength];
        Array.Copy(samples, 0, analysisSamples, 0, analysisLength);
        
        // Calculate energy envelope (simpler than full autocorrelation)
        var envelope = new double[analysisLength / 64]; // Downsample envelope
        for (int i = 0; i < envelope.Length; i++)
        {
            double sum = 0;
            for (int j = 0; j < 64 && (i * 64 + j) < analysisLength; j++)
            {
                sum += Math.Abs(analysisSamples[i * 64 + j]);
            }
            envelope[i] = sum / 64;
        }
        
        // Find period using autocorrelation on envelope (much faster)
        int minPeriod = envelope.Length / 4; // ~200 BPM max
        int maxPeriod = envelope.Length / 2; // ~60 BPM min
        double maxCorrelation = 0;
        int bestPeriod = envelope.Length / 2;
        
        // Use step size of 2 for faster search
        for (int period = minPeriod; period < maxPeriod && period < envelope.Length / 2; period += 2)
        {
            double correlation = 0;
            int count = 0;

            for (int i = 0; i < envelope.Length - period; i++)
            {
                correlation += envelope[i] * envelope[i + period];
                count++;
            }

            if (count > 0)
            {
                correlation /= count;
                if (correlation > maxCorrelation)
                {
                    maxCorrelation = correlation;
                    bestPeriod = period;
                }
            }
        }

        // Convert envelope period to BPM
        // envelope period is in 64-sample blocks, so period * 64 / sampleRate gives seconds per beat
        double secondsPerBeat = (bestPeriod * 64.0) / sampleRate;
        double bpm = 60.0 / secondsPerBeat;
        
        // Clamp to reasonable range
        return Math.Max(60, Math.Min(200, bpm));
    }

    private double CalculateRMS(float[] samples)
    {
        if (samples.Length == 0)
            return 0;

        double sum = 0;
        foreach (var sample in samples)
        {
            sum += sample * sample;
        }
        return Math.Sqrt(sum / samples.Length);
    }

    private double CalculateSpectralCentroid(float[] samples, int sampleRate)
    {
        // Simplified spectral centroid calculation
        // Production would use FFT
        double weightedSum = 0;
        double magnitudeSum = 0;

        for (int i = 0; i < samples.Length; i++)
        {
            double freq = i * (double)sampleRate / samples.Length;
            double magnitude = Math.Abs(samples[i]);
            weightedSum += freq * magnitude;
            magnitudeSum += magnitude;
        }

        return magnitudeSum > 0 ? weightedSum / magnitudeSum : 0;
    }

    private double CalculateSpectralRolloff(float[] samples, int sampleRate)
    {
        // Simplified spectral rolloff (frequency below which 85% of energy is contained)
        double totalEnergy = samples.Sum(s => Math.Abs(s));
        double threshold = totalEnergy * 0.85;
        double cumulativeEnergy = 0;

        for (int i = 0; i < samples.Length; i++)
        {
            cumulativeEnergy += Math.Abs(samples[i]);
            if (cumulativeEnergy >= threshold)
            {
                return i * (double)sampleRate / samples.Length;
            }
        }

        return sampleRate / 2.0;
    }

    private double CalculateZeroCrossingRate(float[] samples)
    {
        if (samples.Length < 2)
            return 0;

        int crossings = 0;
        for (int i = 1; i < samples.Length; i++)
        {
            if ((samples[i - 1] >= 0 && samples[i] < 0) || (samples[i - 1] < 0 && samples[i] >= 0))
                crossings++;
        }

        return (double)crossings / samples.Length;
    }

    private string? EstimateGenre(AudioFeatures features)
    {
        // Simplified genre estimation based on features
        if (features.BPM > 140 && features.Energy > 0.7)
            return "Electronic/Dance";
        if (features.BPM > 120 && features.Energy > 0.6)
            return "Pop/Rock";
        if (features.BPM < 100 && features.Energy < 0.5)
            return "Ambient/Chill";
        if (features.BPM > 160)
            return "Hardcore/Techno";
        
        return "Unknown";
    }
}

