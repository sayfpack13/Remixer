using Remixer.Core.Models;

namespace Remixer.Core.AI;

public class RemixSuggestion
{
    public AudioSettings Settings { get; set; } = new();
    public string? Reasoning { get; set; }
    public double Confidence { get; set; } // 0.0 to 1.0
    public string? PresetName { get; set; }
}

