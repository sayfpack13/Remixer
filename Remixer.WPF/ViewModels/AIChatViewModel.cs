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
            // Build conversation context
            var conversationContext = BuildConversationContext();
            var fullPrompt = BuildChatPrompt(userMessage, conversationContext);

            // Call AI with conversation history
            var history = GetConversationHistory().Select(m => new Remixer.Core.AI.ChatMessage 
            { 
                Role = m.Role, 
                Content = m.Content 
            }).ToList();
            var response = await _aiService.SendChatMessageAsync(fullPrompt, history);

            // Remove typing indicator and add AI response
            Messages.Remove(typingMessage);
            var aiMessage = AddAIMessage(response);

            // Try to parse remix settings from response
            var suggestion = TryParseRemixSuggestion(response);
            if (suggestion != null)
            {
                RemixSuggestionReady?.Invoke(this, suggestion);
                AddSystemMessage("Remix settings have been applied! You can continue chatting to refine them.");
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
        prompt.AppendLine("Respond naturally in conversation. If the user wants to change remix settings, provide your response in JSON format:");
        prompt.AppendLine("{");
        prompt.AppendLine("  \"tempo\": 1.0,");
        prompt.AppendLine("  \"pitch\": 0.0,");
        prompt.AppendLine("  \"reverb\": { \"enabled\": true, \"roomSize\": 0.5, \"damping\": 0.5, \"wetLevel\": 0.3 },");
        prompt.AppendLine("  \"echo\": { \"enabled\": false, \"delay\": 0.2, \"feedback\": 0.3, \"wetLevel\": 0.3 },");
        prompt.AppendLine("  \"filter\": { \"enabled\": false, \"lowCut\": 20.0, \"highCut\": 20000.0, \"lowGain\": 0.0, \"midGain\": 0.0, \"highGain\": 0.0 },");
        prompt.AppendLine("  \"reasoning\": \"Your explanation\"");
        prompt.AppendLine("}");
        prompt.AppendLine();
        prompt.AppendLine("Otherwise, just chat naturally and help the user understand audio remixing.");

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
            // Try to extract JSON from response
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');
            
            if (jsonStart < 0 || jsonEnd <= jsonStart)
                return null;

            var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
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
            
            if (doc.RootElement.TryGetProperty("reasoning", out var reasoning))
                suggestion.Reasoning = reasoning.GetString();
            
            suggestion.Confidence = 0.8;
            suggestion.Settings.IsAISet = true;
            
            return suggestion;
        }
        catch
        {
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

