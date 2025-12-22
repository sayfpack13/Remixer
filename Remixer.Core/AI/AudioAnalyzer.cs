using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Remixer.Core.Audio;
using System;
using System.Linq;

namespace Remixer.Core.AI;

public class AudioAnalyzer
{
    public AudioFeatures Analyze(string filePath)
    {
        using var stream = AudioFileHandler.LoadAudioFile(filePath);
        return Analyze(stream);
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
        // Limit to first 10 seconds to avoid memory issues with large files
        const int maxSamples = 44100 * 10; // 10 seconds at 44.1kHz
        var samples = new System.Collections.Generic.List<float>();
        var buffer = new float[8192];
        int samplesRead;
        int totalSamples = 0;

        while ((samplesRead = provider.Read(buffer, 0, buffer.Length)) > 0 && totalSamples < maxSamples)
        {
            int samplesToAdd = Math.Min(samplesRead, maxSamples - totalSamples);
            for (int i = 0; i < samplesToAdd; i++)
            {
                samples.Add(buffer[i]);
            }
            totalSamples += samplesToAdd;
            
            if (totalSamples >= maxSamples)
                break;
        }

        return samples.ToArray();
    }

    private double EstimateBPM(float[] samples, int sampleRate)
    {
        // Simplified BPM detection using autocorrelation
        // This is a basic implementation - production would use more sophisticated algorithms
        
        int minPeriod = sampleRate / 4; // 240 BPM max
        int maxPeriod = sampleRate / 2; // 120 BPM min
        double maxCorrelation = 0;
        int bestPeriod = sampleRate / 2;

        for (int period = minPeriod; period < maxPeriod && period < samples.Length / 2; period++)
        {
            double correlation = 0;
            int count = 0;

            for (int i = 0; i < samples.Length - period; i++)
            {
                correlation += samples[i] * samples[i + period];
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

        return 60.0 * sampleRate / bestPeriod;
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

