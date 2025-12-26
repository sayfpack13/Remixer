using System;
using System.Text;

namespace Remixer.Core.AI;

public class PromptBuilder
{
    private static readonly Random _random = new Random();
    
    public static string BuildRemixPrompt(AudioFeatures features, string? userPreference = null)
    {
        var prompt = new StringBuilder();
        
        // Add a unique request ID to ensure each request is treated as distinct
        var requestId = Guid.NewGuid().ToString("N").Substring(0, 8);
        var timestamp = DateTime.Now.Ticks % 1000000; // Use last 6 digits of timestamp for variation
        
        // Add variation to the prompt to encourage different suggestions
        var styleVariations = new[]
        {
            "You are an expert audio remixing assistant. Analyze the following audio features and suggest optimal remix parameters.",
            "You are a creative audio engineer specializing in remixing. Analyze the audio and suggest unique remix parameters.",
            "You are a professional music producer. Analyze the audio features and create an innovative remix suggestion.",
            "You are an audio remix specialist. Analyze the following features and suggest creative remix parameters."
        };
        var style = styleVariations[_random.Next(styleVariations.Length)];
        prompt.AppendLine(style);
        prompt.AppendLine($"Request ID: {requestId} (Timestamp: {timestamp})");
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
        prompt.AppendLine("  \"volume\": 1.0,  // Volume multiplier (0.0 to 2.0)");
        prompt.AppendLine("  \"tremolo\": { \"enabled\": false, \"rate\": 3.0, \"depth\": 0.5 },  // Rate: 0.1-10 Hz, Depth: 0-1");
        prompt.AppendLine("  \"compressor\": { \"enabled\": false, \"threshold\": -12.0, \"ratio\": 4.0, \"attack\": 10.0, \"release\": 100.0, \"makeupGain\": 0.0 },  // Threshold: -60 to 0 dB, Ratio: 1-20, Attack/Release: ms, MakeupGain: 0-12 dB");
        prompt.AppendLine("  \"distortion\": { \"enabled\": false, \"drive\": 0.5, \"tone\": 0.5, \"mix\": 0.5 },  // Drive/Tone/Mix: 0-1");
        prompt.AppendLine("  \"chorus\": { \"enabled\": false, \"rate\": 1.0, \"depth\": 0.5, \"mix\": 0.5 },  // Rate: 0.1-5 Hz, Depth/Mix: 0-1");
        prompt.AppendLine("  \"flanger\": { \"enabled\": false, \"delay\": 0.005, \"rate\": 0.5, \"depth\": 0.8, \"feedback\": 0.3, \"mix\": 0.5 },  // Delay: 0.001-0.02s, Rate: 0.1-5 Hz, Depth/Feedback/Mix: 0-1 (Feedback: -1 to 1)");
        prompt.AppendLine("  \"phaser\": { \"enabled\": false, \"rate\": 0.5, \"depth\": 0.8, \"feedback\": 0.3, \"mix\": 0.5 },  // Rate: 0.1-5 Hz, Depth/Feedback/Mix: 0-1 (Feedback: -1 to 1)");
        prompt.AppendLine("  \"reverb\": { \"enabled\": true, \"roomSize\": 0.5, \"damping\": 0.5, \"wetLevel\": 0.3 },  // All values: 0-1");
        prompt.AppendLine("  \"echo\": { \"enabled\": false, \"delay\": 0.2, \"feedback\": 0.3, \"wetLevel\": 0.3 },  // Delay: seconds, Feedback/WetLevel: 0-1");
        prompt.AppendLine("  \"filter\": { \"enabled\": false, \"lowCut\": 20.0, \"highCut\": 20000.0, \"lowGain\": 0.0, \"midGain\": 0.0, \"highGain\": 0.0 },  // LowCut: 20-500 Hz, HighCut: 5000-20000 Hz, Gains: -12 to 12 dB");
        prompt.AppendLine("  \"bitcrusher\": { \"enabled\": false, \"bitDepth\": 8.0, \"downsample\": 2.0, \"mix\": 0.5 },  // BitDepth: 1-16 bits, Downsample: 1.0-10.0, Mix: 0-1 (lo-fi effect)");
        prompt.AppendLine("  \"vibrato\": { \"enabled\": false, \"rate\": 5.0, \"depth\": 0.1, \"mix\": 1.0 },  // Rate: 0.1-10 Hz, Depth: 0-2 semitones, Mix: 0-1 (pitch modulation)");
        prompt.AppendLine("  \"saturation\": { \"enabled\": false, \"drive\": 0.5, \"tone\": 0.5, \"mix\": 0.5 },  // Drive/Tone/Mix: 0-1 (warmer alternative to distortion)");
        prompt.AppendLine("  \"gate\": { \"enabled\": false, \"threshold\": 0.1, \"ratio\": 10.0, \"attack\": 1.0, \"release\": 50.0, \"floor\": 0.0 },  // Threshold: 0-1, Ratio: 1-100, Attack/Release: ms, Floor: 0-1 (rhythmic gating)");
        prompt.AppendLine("  \"reasoning\": \"Brief explanation of the remix choices\"");
        prompt.AppendLine("}");
        prompt.AppendLine();
        prompt.AppendLine("Focus on enhancing the audio's energy and appeal while maintaining musicality.");
        prompt.AppendLine("Use effects strategically: Tremolo for rhythmic variation, Vibrato for pitch modulation, Compressor for dynamic control, Gate for rhythmic gating, Distortion for grit, Saturation for warmth, Bitcrusher for lo-fi character, Chorus/Flanger/Phaser for spatial effects, Reverb/Echo for depth, and Filter for tonal shaping.");
        prompt.AppendLine();
        prompt.AppendLine("IMPORTANT: You have access to ALL effects. Don't be afraid to use multiple effects together to create rich, layered remixes. Experiment with combinations like:");
        prompt.AppendLine("- Saturation + Reverb for warm, spacious sounds");
        prompt.AppendLine("- Bitcrusher + Vibrato for retro/lo-fi character");
        prompt.AppendLine("- Gate + Tremolo for rhythmic, stuttering effects");
        prompt.AppendLine("- Chorus + Phaser for wide, swirling textures");
        prompt.AppendLine("- Compressor + Distortion for aggressive, punchy sounds");
        prompt.AppendLine("Use 3-6 effects per remix to create interesting combinations while maintaining musicality.");
        prompt.AppendLine();
        
        // Add randomized creative instructions to encourage variation
        var creativeInstructions = new[]
        {
            "IMPORTANT: Be creative and try different effect combinations each time. Don't use the same settings repeatedly - experiment with enabling different effects and varying their parameters to create unique remix variations. Consider using Bitcrusher for lo-fi character, Vibrato for pitch modulation, Saturation for warmth, or Gate for rhythmic effects.",
            "IMPORTANT: Each remix should be unique. Vary which effects you enable and their parameters. Try different combinations of Chorus, Flanger, Phaser, Distortion, Saturation, Bitcrusher, Vibrato, Gate, Compressor, and Tremolo to create distinct sounds.",
            "IMPORTANT: Create a fresh, unique remix each time. Experiment with different effect combinations - maybe focus on spatial effects (Chorus/Flanger/Phaser) this time, dynamic effects (Compressor/Gate) next time, or character effects (Distortion/Saturation/Bitcrusher) for another variation.",
            "IMPORTANT: Be experimental! Try different effect combinations and parameter values. Don't repeat the same remix - make each suggestion unique and creative. Consider using the new effects: Bitcrusher for retro/lo-fi sounds, Vibrato for expressive pitch variation, Saturation for analog warmth, or Gate for rhythmic gating effects."
        };
        var instruction = creativeInstructions[_random.Next(creativeInstructions.Length)];
        prompt.AppendLine(instruction);

        return prompt.ToString();
    }
}

