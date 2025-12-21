namespace Remixer.Core.AI;

public class AudioFeatures
{
    public double BPM { get; set; } // Beats per minute
    public string? KeySignature { get; set; } // Musical key
    public double Energy { get; set; } // 0.0 to 1.0
    public double SpectralCentroid { get; set; } // Hz
    public double SpectralRolloff { get; set; } // Hz
    public double ZeroCrossingRate { get; set; } // 0.0 to 1.0
    public double RMS { get; set; } // Root mean square (loudness)
    public string? Genre { get; set; } // Estimated genre
    public double Duration { get; set; } // seconds
    public int SampleRate { get; set; }
    public int Channels { get; set; }
}

