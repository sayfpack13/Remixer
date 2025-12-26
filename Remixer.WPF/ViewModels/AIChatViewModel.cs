using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Remixer.Core.AI;
using Remixer.Core.Audio;
using Remixer.Core.Logging;
using Remixer.Core.Models;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Remixer.WPF.ViewModels;

public partial class AIChatViewModel : ObservableObject
{
    private readonly AIService _aiService;
    private readonly AudioEngine _audioEngine;
    private readonly AudioAnalyzer _audioAnalyzer;
    private AudioFeatures? _currentFeatures;

    [ObservableProperty]
    private ObservableCollection<ChatMessage> _messages = new();

    [ObservableProperty]
    private string _userInput = "";

    [ObservableProperty]
    private bool _isSending;

    [ObservableProperty]
    private bool _isAudioLoaded;

    public event EventHandler<RemixSuggestion>? RemixSuggestionReady;

    public AIChatViewModel(AIService aiService, AudioEngine audioEngine)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _audioEngine = audioEngine ?? throw new ArgumentNullException(nameof(audioEngine));
        _audioAnalyzer = new AudioAnalyzer();
        
        // Add welcome message
        AddSystemMessage("Hello! I'm your AI remix assistant. I can help you customize your audio remix. " +
                        "Tell me what kind of sound you're looking for, or ask me to analyze your audio and suggest settings.");
    }

    public void SetAudioLoaded(bool isLoaded)
    {
        IsAudioLoaded = isLoaded;
        if (isLoaded && _currentFeatures == null)
        {
            _ = LoadAudioFeaturesAsync();
        }
    }

    private async Task LoadAudioFeaturesAsync()
    {
        try
        {
            var filePath = _audioEngine.CurrentFilePath;
            if (!string.IsNullOrEmpty(filePath))
            {
                // Run audio analysis on background thread to avoid blocking UI
                _currentFeatures = await Task.Run(() => _audioAnalyzer.Analyze(filePath));
                
                // Update UI on dispatcher thread
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    AddSystemMessage($"Audio analyzed: BPM={_currentFeatures.BPM:F1}, Energy={_currentFeatures.Energy:F2}, Genre={_currentFeatures.Genre ?? "Unknown"}");
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to load audio features for chat", ex);
        }
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(UserInput) || IsSending || !IsAudioLoaded)
            return;

        var userMessage = UserInput.Trim();
        UserInput = "";

        // Add user message to chat
        AddUserMessage(userMessage);

        // Show typing indicator
        IsSending = true;
        var typingMessage = AddAIMessage("Thinking...");

        try
        {
            // Ensure we have audio features (use cached if available, otherwise quick analysis)
            if (_currentFeatures == null)
            {
                var filePath = _audioEngine.CurrentFilePath;
                if (!string.IsNullOrEmpty(filePath))
                {
                    // Quick analysis in background
                    _currentFeatures = await Task.Run(() => _audioAnalyzer.Analyze(filePath));
                }
            }
            
            // Build conversation context
            var conversationContext = BuildConversationContext();
            var fullPrompt = BuildChatPrompt(userMessage, conversationContext);

            // Call AI with conversation history
            var history = GetConversationHistory().Select(m => new Remixer.Core.AI.ChatMessage 
            { 
                Role = m.Role, 
                Content = m.Content 
            }).ToList();
            
            string? response = null;
            try
            {
                response = await _aiService.SendChatMessageAsync(fullPrompt, history);
            }
            catch (Exception apiEx)
            {
                Logger.Warning($"AI API call failed: {apiEx.Message}. Falling back to intelligent remix suggestion.");
                // Fall back to intelligent remix suggestion based on audio features
                if (_currentFeatures != null)
                {
                    var fallbackSuggestion = _aiService.CreateDefaultSuggestion(_currentFeatures);
                    if (fallbackSuggestion != null)
                    {
                        response = BuildFallbackResponse(fallbackSuggestion, userMessage);
                    }
                }
                
                if (string.IsNullOrEmpty(response))
                {
                    throw; // Re-throw if we couldn't create a fallback
                }
            }

            // Remove typing indicator
            Messages.Remove(typingMessage);

            // Try to parse remix settings from response
            var suggestion = TryParseRemixSuggestion(response);
            if (suggestion != null)
            {
                Logger.Info($"Parsed remix suggestion from AI chat. Tempo={suggestion.Settings.Tempo:F2}x, Pitch={suggestion.Settings.Pitch:F1}st");
                
                // Apply settings immediately
                RemixSuggestionReady?.Invoke(this, suggestion);
                Logger.Info("RemixSuggestionReady event fired");
                
                // Show user-friendly message instead of raw JSON
                string friendlyMessage;
                if (!string.IsNullOrWhiteSpace(suggestion.Reasoning))
                {
                    // Use the reasoning from the AI if available
                    friendlyMessage = suggestion.Reasoning;
                }
                else
                {
                    // Build a friendly message describing the applied settings
                    var settingsDesc = new System.Text.StringBuilder();
                    settingsDesc.Append($"Applied remix settings: ");
                    settingsDesc.Append($"Tempo {suggestion.Settings.Tempo:F2}x");
                    if (Math.Abs(suggestion.Settings.Pitch) > 0.1)
                        settingsDesc.Append($", Pitch {suggestion.Settings.Pitch:+0.0;-0.0} semitones");
                    if (suggestion.Settings.Tremolo?.Enabled == true)
                        settingsDesc.Append(", Tremolo enabled");
                    if (suggestion.Settings.Compressor?.Enabled == true)
                        settingsDesc.Append(", Compressor enabled");
                    if (suggestion.Settings.Distortion?.Enabled == true)
                        settingsDesc.Append(", Distortion enabled");
                    if (suggestion.Settings.Chorus?.Enabled == true)
                        settingsDesc.Append(", Chorus enabled");
                    if (suggestion.Settings.Flanger?.Enabled == true)
                        settingsDesc.Append(", Flanger enabled");
                    if (suggestion.Settings.Phaser?.Enabled == true)
                        settingsDesc.Append(", Phaser enabled");
                    if (suggestion.Settings.Reverb?.Enabled == true)
                        settingsDesc.Append(", Reverb enabled");
                    if (suggestion.Settings.Echo?.Enabled == true)
                        settingsDesc.Append(", Echo enabled");
                    if (suggestion.Settings.Filter?.Enabled == true)
                        settingsDesc.Append(", Filter enabled");
                    if (suggestion.Settings.Bitcrusher?.Enabled == true)
                        settingsDesc.Append(", Bitcrusher enabled");
                    if (suggestion.Settings.Vibrato?.Enabled == true)
                        settingsDesc.Append(", Vibrato enabled");
                    if (suggestion.Settings.Saturation?.Enabled == true)
                        settingsDesc.Append(", Saturation enabled");
                    if (suggestion.Settings.Gate?.Enabled == true)
                        settingsDesc.Append(", Gate enabled");
                    friendlyMessage = settingsDesc.ToString();
                }
                
                AddAIMessage(friendlyMessage);
            }
            else
            {
                // No JSON found, show the full response as normal conversation
                AddAIMessage(response);
            }
        }
        catch (Exception ex)
        {
            Messages.Remove(typingMessage);
            AddAIMessage($"Sorry, I encountered an error: {ex.Message}. Please try again.");
            Logger.Error("Error in chat", ex);
        }
        finally
        {
            IsSending = false;
        }
    }

    private string BuildChatPrompt(string userMessage, string context)
    {
        var prompt = new System.Text.StringBuilder();
        prompt.AppendLine("You are an expert audio remixing assistant. Help the user customize their audio remix through conversation.");
        prompt.AppendLine();
        prompt.AppendLine(context);
        prompt.AppendLine();
        prompt.AppendLine("User's request:");
        prompt.AppendLine(userMessage);
        prompt.AppendLine();
        prompt.AppendLine("IMPORTANT: When the user requests remix settings or changes, you MUST provide your response in JSON format. Include both:");
        prompt.AppendLine("1. A natural conversational explanation");
        prompt.AppendLine("2. A JSON object with the settings (you can wrap it in a code block with ```json ... ```)");
        prompt.AppendLine();
        prompt.AppendLine("Required JSON format (include ALL fields even if unchanged):");
        prompt.AppendLine("```json");
        prompt.AppendLine("{");
        prompt.AppendLine("  \"tempo\": 1.0,  // 0.5 to 2.0");
        prompt.AppendLine("  \"pitch\": 0.0,  // -12 to 12 semitones");
        prompt.AppendLine("  \"volume\": 1.0,  // 0.0 to 2.0");
        prompt.AppendLine("  \"tremolo\": { \"enabled\": false, \"rate\": 3.0, \"depth\": 0.5 },  // Rate: 0.1-10 Hz, Depth: 0-1");
        prompt.AppendLine("  \"compressor\": { \"enabled\": false, \"threshold\": -12.0, \"ratio\": 4.0, \"attack\": 10.0, \"release\": 100.0, \"makeupGain\": 0.0 },");
        prompt.AppendLine("  \"distortion\": { \"enabled\": false, \"drive\": 0.5, \"tone\": 0.5, \"mix\": 0.5 },  // All: 0-1");
        prompt.AppendLine("  \"chorus\": { \"enabled\": false, \"rate\": 1.0, \"depth\": 0.5, \"mix\": 0.5 },  // Rate: 0.1-5 Hz, Depth/Mix: 0-1");
        prompt.AppendLine("  \"flanger\": { \"enabled\": false, \"delay\": 0.005, \"rate\": 0.5, \"depth\": 0.8, \"feedback\": 0.3, \"mix\": 0.5 },");
        prompt.AppendLine("  \"phaser\": { \"enabled\": false, \"rate\": 0.5, \"depth\": 0.8, \"feedback\": 0.3, \"mix\": 0.5 },");
        prompt.AppendLine("  \"reverb\": { \"enabled\": true, \"roomSize\": 0.5, \"damping\": 0.5, \"wetLevel\": 0.3 },");
        prompt.AppendLine("  \"echo\": { \"enabled\": false, \"delay\": 0.2, \"feedback\": 0.3, \"wetLevel\": 0.3 },");
        prompt.AppendLine("  \"filter\": { \"enabled\": false, \"lowCut\": 20.0, \"highCut\": 20000.0, \"lowGain\": 0.0, \"midGain\": 0.0, \"highGain\": 0.0 },");
        prompt.AppendLine("  \"bitcrusher\": { \"enabled\": false, \"bitDepth\": 8.0, \"downsample\": 2.0, \"mix\": 0.5 },  // BitDepth: 1-16 bits, Downsample: 1.0-10.0, Mix: 0-1");
        prompt.AppendLine("  \"vibrato\": { \"enabled\": false, \"rate\": 5.0, \"depth\": 0.1, \"mix\": 1.0 },  // Rate: 0.1-10 Hz, Depth: 0-2 semitones, Mix: 0-1");
        prompt.AppendLine("  \"saturation\": { \"enabled\": false, \"drive\": 0.5, \"tone\": 0.5, \"mix\": 0.5 },  // Drive/Tone/Mix: 0-1");
        prompt.AppendLine("  \"gate\": { \"enabled\": false, \"threshold\": 0.1, \"ratio\": 10.0, \"attack\": 1.0, \"release\": 50.0, \"floor\": 0.0 },  // Threshold: 0-1, Ratio: 1-100, Attack/Release: ms, Floor: 0-1");
        prompt.AppendLine("  \"reasoning\": \"Your explanation\"");
        prompt.AppendLine("}");
        prompt.AppendLine("```");
        prompt.AppendLine();
        prompt.AppendLine("If the user is just asking questions or having a conversation, respond naturally without JSON.");

        return prompt.ToString();
    }

    private string BuildConversationContext()
    {
        var context = new System.Text.StringBuilder();
        
        if (_currentFeatures != null)
        {
            context.AppendLine("Current Audio Features:");
            context.AppendLine($"- BPM: {_currentFeatures.BPM:F1}");
            context.AppendLine($"- Energy Level: {_currentFeatures.Energy:F2}");
            context.AppendLine($"- Spectral Centroid: {_currentFeatures.SpectralCentroid:F1} Hz");
            context.AppendLine($"- Genre: {_currentFeatures.Genre ?? "Unknown"}");
            context.AppendLine($"- Duration: {_currentFeatures.Duration:F1} seconds");
        }

        var currentSettings = _audioEngine.Settings;
        if (currentSettings != null)
        {
            context.AppendLine();
            context.AppendLine("Current Remix Settings:");
            context.AppendLine($"- Tempo: {currentSettings.Tempo:F2}x");
            context.AppendLine($"- Pitch: {currentSettings.Pitch:F1} semitones");
            context.AppendLine($"- Reverb: {(currentSettings.Reverb?.Enabled == true ? "Enabled" : "Disabled")}");
            context.AppendLine($"- Echo: {(currentSettings.Echo?.Enabled == true ? "Enabled" : "Disabled")}");
        }

        return context.ToString();
    }

    private System.Collections.Generic.List<ChatMessage> GetConversationHistory()
    {
        // Get last 10 messages for context (excluding system messages)
        return Messages
            .Where(m => m.Role != "system")
            .TakeLast(10)
            .ToList();
    }

    private RemixSuggestion? TryParseRemixSuggestion(string response)
    {
        try
        {
            string? json = null;
            
            // First, try to find JSON in code blocks (```json ... ``` or ``` ... ```)
            var codeBlockStart = response.IndexOf("```");
            if (codeBlockStart >= 0)
            {
                var codeBlockEnd = response.IndexOf("```", codeBlockStart + 3);
                if (codeBlockEnd > codeBlockStart)
                {
                    var codeBlock = response.Substring(codeBlockStart + 3, codeBlockEnd - codeBlockStart - 3);
                    // Remove "json" marker if present
                    codeBlock = codeBlock.TrimStart();
                    if (codeBlock.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                        codeBlock = codeBlock.Substring(4).TrimStart();
                    
                    // Check if it looks like JSON
                    if (codeBlock.TrimStart().StartsWith("{"))
                    {
                        json = codeBlock.Trim();
                    }
                }
            }
            
            // If no code block found, try to extract JSON directly
            if (string.IsNullOrEmpty(json))
            {
                var jsonStart = response.IndexOf('{');
                var jsonEnd = response.LastIndexOf('}');
                
                if (jsonStart < 0 || jsonEnd <= jsonStart)
                {
                    Logger.Debug("No JSON found in AI response");
                    return null;
                }

                json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
            }
            
            if (string.IsNullOrEmpty(json))
            {
                Logger.Debug("Failed to extract JSON from response");
                return null;
            }

            // Try to parse the JSON
            var doc = System.Text.Json.JsonDocument.Parse(json);
            
            var suggestion = new RemixSuggestion();
            
            if (doc.RootElement.TryGetProperty("tempo", out var tempo))
                suggestion.Settings.Tempo = tempo.GetDouble();
            
            if (doc.RootElement.TryGetProperty("pitch", out var pitch))
                suggestion.Settings.Pitch = pitch.GetDouble();
            
            if (doc.RootElement.TryGetProperty("reverb", out var reverb))
            {
                suggestion.Settings.Reverb.Enabled = reverb.TryGetProperty("enabled", out var enabled) && enabled.GetBoolean();
                if (reverb.TryGetProperty("roomSize", out var roomSize))
                    suggestion.Settings.Reverb.RoomSize = roomSize.GetDouble();
                if (reverb.TryGetProperty("damping", out var damping))
                    suggestion.Settings.Reverb.Damping = damping.GetDouble();
                if (reverb.TryGetProperty("wetLevel", out var wetLevel))
                    suggestion.Settings.Reverb.WetLevel = wetLevel.GetDouble();
            }
            
            if (doc.RootElement.TryGetProperty("echo", out var echo))
            {
                suggestion.Settings.Echo.Enabled = echo.TryGetProperty("enabled", out var enabled) && enabled.GetBoolean();
                if (echo.TryGetProperty("delay", out var delay))
                    suggestion.Settings.Echo.Delay = delay.GetDouble();
                if (echo.TryGetProperty("feedback", out var feedback))
                    suggestion.Settings.Echo.Feedback = feedback.GetDouble();
                if (echo.TryGetProperty("wetLevel", out var wetLevel))
                    suggestion.Settings.Echo.WetLevel = wetLevel.GetDouble();
            }
            
            if (doc.RootElement.TryGetProperty("filter", out var filter))
            {
                suggestion.Settings.Filter.Enabled = filter.TryGetProperty("enabled", out var enabled) && enabled.GetBoolean();
                if (filter.TryGetProperty("lowCut", out var lowCut))
                    suggestion.Settings.Filter.LowCut = lowCut.GetDouble();
                if (filter.TryGetProperty("highCut", out var highCut))
                    suggestion.Settings.Filter.HighCut = highCut.GetDouble();
                if (filter.TryGetProperty("lowGain", out var lowGain))
                    suggestion.Settings.Filter.LowGain = lowGain.GetDouble();
                if (filter.TryGetProperty("midGain", out var midGain))
                    suggestion.Settings.Filter.MidGain = midGain.GetDouble();
                if (filter.TryGetProperty("highGain", out var highGain))
                    suggestion.Settings.Filter.HighGain = highGain.GetDouble();
            }
            
            if (doc.RootElement.TryGetProperty("volume", out var volume))
                suggestion.Settings.Volume = volume.GetDouble();
            
            if (doc.RootElement.TryGetProperty("tremolo", out var tremolo))
            {
                suggestion.Settings.Tremolo.Enabled = tremolo.TryGetProperty("enabled", out var enabled) && enabled.GetBoolean();
                if (tremolo.TryGetProperty("rate", out var rate))
                    suggestion.Settings.Tremolo.Rate = rate.GetDouble();
                if (tremolo.TryGetProperty("depth", out var depth))
                    suggestion.Settings.Tremolo.Depth = depth.GetDouble();
            }
            
            if (doc.RootElement.TryGetProperty("compressor", out var compressor))
            {
                suggestion.Settings.Compressor.Enabled = compressor.TryGetProperty("enabled", out var enabled) && enabled.GetBoolean();
                if (compressor.TryGetProperty("threshold", out var threshold))
                    suggestion.Settings.Compressor.Threshold = threshold.GetDouble();
                if (compressor.TryGetProperty("ratio", out var ratio))
                    suggestion.Settings.Compressor.Ratio = ratio.GetDouble();
                if (compressor.TryGetProperty("attack", out var attack))
                    suggestion.Settings.Compressor.Attack = attack.GetDouble();
                if (compressor.TryGetProperty("release", out var release))
                    suggestion.Settings.Compressor.Release = release.GetDouble();
                if (compressor.TryGetProperty("makeupGain", out var makeupGain))
                    suggestion.Settings.Compressor.MakeupGain = makeupGain.GetDouble();
            }
            
            if (doc.RootElement.TryGetProperty("distortion", out var distortion))
            {
                suggestion.Settings.Distortion.Enabled = distortion.TryGetProperty("enabled", out var enabled) && enabled.GetBoolean();
                if (distortion.TryGetProperty("drive", out var drive))
                    suggestion.Settings.Distortion.Drive = drive.GetDouble();
                if (distortion.TryGetProperty("tone", out var tone))
                    suggestion.Settings.Distortion.Tone = tone.GetDouble();
                if (distortion.TryGetProperty("mix", out var mix))
                    suggestion.Settings.Distortion.Mix = mix.GetDouble();
            }
            
            if (doc.RootElement.TryGetProperty("chorus", out var chorus))
            {
                suggestion.Settings.Chorus.Enabled = chorus.TryGetProperty("enabled", out var enabled) && enabled.GetBoolean();
                if (chorus.TryGetProperty("rate", out var rate))
                    suggestion.Settings.Chorus.Rate = rate.GetDouble();
                if (chorus.TryGetProperty("depth", out var depth))
                    suggestion.Settings.Chorus.Depth = depth.GetDouble();
                if (chorus.TryGetProperty("mix", out var mix))
                    suggestion.Settings.Chorus.Mix = mix.GetDouble();
            }
            
            if (doc.RootElement.TryGetProperty("flanger", out var flanger))
            {
                suggestion.Settings.Flanger.Enabled = flanger.TryGetProperty("enabled", out var enabled) && enabled.GetBoolean();
                if (flanger.TryGetProperty("delay", out var delay))
                    suggestion.Settings.Flanger.Delay = delay.GetDouble();
                if (flanger.TryGetProperty("rate", out var rate))
                    suggestion.Settings.Flanger.Rate = rate.GetDouble();
                if (flanger.TryGetProperty("depth", out var depth))
                    suggestion.Settings.Flanger.Depth = depth.GetDouble();
                if (flanger.TryGetProperty("feedback", out var feedback))
                    suggestion.Settings.Flanger.Feedback = feedback.GetDouble();
                if (flanger.TryGetProperty("mix", out var mix))
                    suggestion.Settings.Flanger.Mix = mix.GetDouble();
            }
            
            if (doc.RootElement.TryGetProperty("phaser", out var phaser))
            {
                suggestion.Settings.Phaser.Enabled = phaser.TryGetProperty("enabled", out var enabled) && enabled.GetBoolean();
                if (phaser.TryGetProperty("rate", out var rate))
                    suggestion.Settings.Phaser.Rate = rate.GetDouble();
                if (phaser.TryGetProperty("depth", out var depth))
                    suggestion.Settings.Phaser.Depth = depth.GetDouble();
                if (phaser.TryGetProperty("feedback", out var feedback))
                    suggestion.Settings.Phaser.Feedback = feedback.GetDouble();
                if (phaser.TryGetProperty("mix", out var mix))
                    suggestion.Settings.Phaser.Mix = mix.GetDouble();
            }
            
            if (doc.RootElement.TryGetProperty("bitcrusher", out var bitcrusher))
            {
                suggestion.Settings.Bitcrusher.Enabled = bitcrusher.TryGetProperty("enabled", out var enabled) && enabled.GetBoolean();
                if (bitcrusher.TryGetProperty("bitDepth", out var bitDepth))
                    suggestion.Settings.Bitcrusher.BitDepth = (int)bitDepth.GetDouble();
                if (bitcrusher.TryGetProperty("downsample", out var downsample))
                    suggestion.Settings.Bitcrusher.Downsample = downsample.GetDouble();
                if (bitcrusher.TryGetProperty("mix", out var mix))
                    suggestion.Settings.Bitcrusher.Mix = mix.GetDouble();
            }
            
            if (doc.RootElement.TryGetProperty("vibrato", out var vibrato))
            {
                suggestion.Settings.Vibrato.Enabled = vibrato.TryGetProperty("enabled", out var enabled) && enabled.GetBoolean();
                if (vibrato.TryGetProperty("rate", out var rate))
                    suggestion.Settings.Vibrato.Rate = rate.GetDouble();
                if (vibrato.TryGetProperty("depth", out var depth))
                    suggestion.Settings.Vibrato.Depth = depth.GetDouble();
                if (vibrato.TryGetProperty("mix", out var mix))
                    suggestion.Settings.Vibrato.Mix = mix.GetDouble();
            }
            
            if (doc.RootElement.TryGetProperty("saturation", out var saturation))
            {
                suggestion.Settings.Saturation.Enabled = saturation.TryGetProperty("enabled", out var enabled) && enabled.GetBoolean();
                if (saturation.TryGetProperty("drive", out var drive))
                    suggestion.Settings.Saturation.Drive = drive.GetDouble();
                if (saturation.TryGetProperty("tone", out var tone))
                    suggestion.Settings.Saturation.Tone = tone.GetDouble();
                if (saturation.TryGetProperty("mix", out var mix))
                    suggestion.Settings.Saturation.Mix = mix.GetDouble();
            }
            
            if (doc.RootElement.TryGetProperty("gate", out var gate))
            {
                suggestion.Settings.Gate.Enabled = gate.TryGetProperty("enabled", out var enabled) && enabled.GetBoolean();
                if (gate.TryGetProperty("threshold", out var threshold))
                    suggestion.Settings.Gate.Threshold = threshold.GetDouble();
                if (gate.TryGetProperty("ratio", out var ratio))
                    suggestion.Settings.Gate.Ratio = ratio.GetDouble();
                if (gate.TryGetProperty("attack", out var attack))
                    suggestion.Settings.Gate.Attack = attack.GetDouble();
                if (gate.TryGetProperty("release", out var release))
                    suggestion.Settings.Gate.Release = release.GetDouble();
                if (gate.TryGetProperty("floor", out var floor))
                    suggestion.Settings.Gate.Floor = floor.GetDouble();
            }
            
            if (doc.RootElement.TryGetProperty("reasoning", out var reasoning))
                suggestion.Reasoning = reasoning.GetString();
            
            suggestion.Confidence = 0.8;
            suggestion.Settings.IsAISet = true;
            
            Logger.Info($"Successfully parsed remix suggestion from AI response. Tempo={suggestion.Settings.Tempo:F2}x, Pitch={suggestion.Settings.Pitch:F1}st");
            return suggestion;
        }
        catch (System.Text.Json.JsonException ex)
        {
            Logger.Warning($"Failed to parse JSON from AI response: {ex.Message}");
            Logger.Debug($"Response content (first 500 chars): {response.Substring(0, Math.Min(500, response.Length))}");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Warning($"Error parsing remix suggestion: {ex.Message}");
            return null;
        }
    }

    private ChatMessage AddUserMessage(string content)
    {
        var message = new ChatMessage { Role = "user", Content = content, Timestamp = DateTime.Now };
        Messages.Add(message);
        return message;
    }

    private ChatMessage AddAIMessage(string content)
    {
        var message = new ChatMessage { Role = "assistant", Content = content, Timestamp = DateTime.Now };
        Messages.Add(message);
        return message;
    }

    private ChatMessage AddSystemMessage(string content)
    {
        var message = new ChatMessage { Role = "system", Content = content, Timestamp = DateTime.Now };
        Messages.Add(message);
        return message;
    }

    private string BuildFallbackResponse(RemixSuggestion suggestion, string userMessage)
    {
        // Build a JSON response that matches the expected format
        var json = new System.Text.StringBuilder();
        json.AppendLine("{");
        json.AppendLine($"  \"tempo\": {suggestion.Settings.Tempo:F2},");
        json.AppendLine($"  \"pitch\": {suggestion.Settings.Pitch:F1},");
        json.AppendLine($"  \"volume\": {suggestion.Settings.Volume:F2},");
        
        if (suggestion.Settings.Tremolo != null)
        {
            json.AppendLine($"  \"tremolo\": {{ \"enabled\": {suggestion.Settings.Tremolo.Enabled.ToString().ToLower()}, \"rate\": {suggestion.Settings.Tremolo.Rate:F2}, \"depth\": {suggestion.Settings.Tremolo.Depth:F2} }},");
        }
        
        if (suggestion.Settings.Compressor != null)
        {
            json.AppendLine($"  \"compressor\": {{ \"enabled\": {suggestion.Settings.Compressor.Enabled.ToString().ToLower()}, \"threshold\": {suggestion.Settings.Compressor.Threshold:F1}, \"ratio\": {suggestion.Settings.Compressor.Ratio:F1}, \"attack\": {suggestion.Settings.Compressor.Attack:F1}, \"release\": {suggestion.Settings.Compressor.Release:F1}, \"makeupGain\": {suggestion.Settings.Compressor.MakeupGain:F1} }},");
        }
        
        if (suggestion.Settings.Distortion != null)
        {
            json.AppendLine($"  \"distortion\": {{ \"enabled\": {suggestion.Settings.Distortion.Enabled.ToString().ToLower()}, \"drive\": {suggestion.Settings.Distortion.Drive:F2}, \"tone\": {suggestion.Settings.Distortion.Tone:F2}, \"mix\": {suggestion.Settings.Distortion.Mix:F2} }},");
        }
        
        if (suggestion.Settings.Chorus != null)
        {
            json.AppendLine($"  \"chorus\": {{ \"enabled\": {suggestion.Settings.Chorus.Enabled.ToString().ToLower()}, \"rate\": {suggestion.Settings.Chorus.Rate:F2}, \"depth\": {suggestion.Settings.Chorus.Depth:F2}, \"mix\": {suggestion.Settings.Chorus.Mix:F2} }},");
        }
        
        if (suggestion.Settings.Flanger != null)
        {
            json.AppendLine($"  \"flanger\": {{ \"enabled\": {suggestion.Settings.Flanger.Enabled.ToString().ToLower()}, \"delay\": {suggestion.Settings.Flanger.Delay:F4}, \"rate\": {suggestion.Settings.Flanger.Rate:F2}, \"depth\": {suggestion.Settings.Flanger.Depth:F2}, \"feedback\": {suggestion.Settings.Flanger.Feedback:F2}, \"mix\": {suggestion.Settings.Flanger.Mix:F2} }},");
        }
        
        if (suggestion.Settings.Phaser != null)
        {
            json.AppendLine($"  \"phaser\": {{ \"enabled\": {suggestion.Settings.Phaser.Enabled.ToString().ToLower()}, \"rate\": {suggestion.Settings.Phaser.Rate:F2}, \"depth\": {suggestion.Settings.Phaser.Depth:F2}, \"feedback\": {suggestion.Settings.Phaser.Feedback:F2}, \"mix\": {suggestion.Settings.Phaser.Mix:F2} }},");
        }
        
        if (suggestion.Settings.Reverb != null)
        {
            json.AppendLine($"  \"reverb\": {{ \"enabled\": {suggestion.Settings.Reverb.Enabled.ToString().ToLower()}, \"roomSize\": {suggestion.Settings.Reverb.RoomSize:F2}, \"damping\": {suggestion.Settings.Reverb.Damping:F2}, \"wetLevel\": {suggestion.Settings.Reverb.WetLevel:F2} }},");
        }
        
        if (suggestion.Settings.Echo != null)
        {
            json.AppendLine($"  \"echo\": {{ \"enabled\": {suggestion.Settings.Echo.Enabled.ToString().ToLower()}, \"delay\": {suggestion.Settings.Echo.Delay:F2}, \"feedback\": {suggestion.Settings.Echo.Feedback:F2}, \"wetLevel\": {suggestion.Settings.Echo.WetLevel:F2} }},");
        }
        
        if (suggestion.Settings.Filter != null)
        {
            json.AppendLine($"  \"filter\": {{ \"enabled\": {suggestion.Settings.Filter.Enabled.ToString().ToLower()}, \"lowCut\": {suggestion.Settings.Filter.LowCut:F1}, \"highCut\": {suggestion.Settings.Filter.HighCut:F1}, \"lowGain\": {suggestion.Settings.Filter.LowGain:F1}, \"midGain\": {suggestion.Settings.Filter.MidGain:F1}, \"highGain\": {suggestion.Settings.Filter.HighGain:F1} }},");
        }
        
        if (suggestion.Settings.Bitcrusher != null)
        {
            json.AppendLine($"  \"bitcrusher\": {{ \"enabled\": {suggestion.Settings.Bitcrusher.Enabled.ToString().ToLower()}, \"bitDepth\": {suggestion.Settings.Bitcrusher.BitDepth:F0}, \"downsample\": {suggestion.Settings.Bitcrusher.Downsample:F2}, \"mix\": {suggestion.Settings.Bitcrusher.Mix:F2} }},");
        }
        
        if (suggestion.Settings.Vibrato != null)
        {
            json.AppendLine($"  \"vibrato\": {{ \"enabled\": {suggestion.Settings.Vibrato.Enabled.ToString().ToLower()}, \"rate\": {suggestion.Settings.Vibrato.Rate:F2}, \"depth\": {suggestion.Settings.Vibrato.Depth:F2}, \"mix\": {suggestion.Settings.Vibrato.Mix:F2} }},");
        }
        
        if (suggestion.Settings.Saturation != null)
        {
            json.AppendLine($"  \"saturation\": {{ \"enabled\": {suggestion.Settings.Saturation.Enabled.ToString().ToLower()}, \"drive\": {suggestion.Settings.Saturation.Drive:F2}, \"tone\": {suggestion.Settings.Saturation.Tone:F2}, \"mix\": {suggestion.Settings.Saturation.Mix:F2} }},");
        }
        
        if (suggestion.Settings.Gate != null)
        {
            json.AppendLine($"  \"gate\": {{ \"enabled\": {suggestion.Settings.Gate.Enabled.ToString().ToLower()}, \"threshold\": {suggestion.Settings.Gate.Threshold:F2}, \"ratio\": {suggestion.Settings.Gate.Ratio:F1}, \"attack\": {suggestion.Settings.Gate.Attack:F1}, \"release\": {suggestion.Settings.Gate.Release:F1}, \"floor\": {suggestion.Settings.Gate.Floor:F2} }},");
        }
        
        var reasoning = suggestion.Reasoning ?? $"Applied intelligent remix settings based on your request: '{userMessage}' and audio analysis.";
        json.AppendLine($"  \"reasoning\": \"{reasoning.Replace("\"", "\\\"")}\"");
        json.AppendLine("}");
        
        return json.ToString();
    }

    [RelayCommand]
    private void ClearChat()
    {
        Messages.Clear();
        AddSystemMessage("Chat cleared. How can I help you customize your remix?");
    }
}

public class ChatMessage
{
    public string Role { get; set; } = ""; // "user", "assistant", "system"
    public string Content { get; set; } = "";
    public DateTime Timestamp { get; set; }
}

