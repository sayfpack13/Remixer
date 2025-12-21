using System.Text;

namespace Remixer.Core.AI;

public class PromptBuilder
{
    public static string BuildRemixPrompt(AudioFeatures features, string? userPreference = null)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("You are an expert audio remixing assistant. Analyze the following audio features and suggest optimal remix parameters.");
        prompt.AppendLine();
        prompt.AppendLine("Audio Features:");
        prompt.AppendLine($"- BPM: {features.BPM:F1}");
        prompt.AppendLine($"- Energy Level: {features.Energy:F2} (0.0 = low, 1.0 = high)");
        prompt.AppendLine($"- Spectral Centroid: {features.SpectralCentroid:F1} Hz");
        prompt.AppendLine($"- Estimated Genre: {features.Genre ?? "Unknown"}");
        prompt.AppendLine($"- Duration: {features.Duration:F1} seconds");
        
        if (!string.IsNullOrEmpty(userPreference))
        {
            prompt.AppendLine($"- User Preference: {userPreference}");
        }
        
        prompt.AppendLine();
        prompt.AppendLine("Please provide remix parameters in JSON format with the following structure:");
        prompt.AppendLine("{");
        prompt.AppendLine("  \"tempo\": 1.0,  // Multiplier (0.5 to 2.0)");
        prompt.AppendLine("  \"pitch\": 0.0,  // Semitones (-12 to 12)");
        prompt.AppendLine("  \"reverb\": { \"enabled\": true, \"roomSize\": 0.5, \"damping\": 0.5, \"wetLevel\": 0.3 },");
        prompt.AppendLine("  \"echo\": { \"enabled\": false, \"delay\": 0.2, \"feedback\": 0.3, \"wetLevel\": 0.3 },");
        prompt.AppendLine("  \"filter\": { \"enabled\": false, \"lowCut\": 20.0, \"highCut\": 20000.0, \"lowGain\": 0.0, \"midGain\": 0.0, \"highGain\": 0.0 },");
        prompt.AppendLine("  \"reasoning\": \"Brief explanation of the remix choices\"");
        prompt.AppendLine("}");
        prompt.AppendLine();
        prompt.AppendLine("Focus on enhancing the audio's energy and appeal while maintaining musicality.");

        return prompt.ToString();
    }
}

