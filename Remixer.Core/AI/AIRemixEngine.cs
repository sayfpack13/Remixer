using Remixer.Core.Audio;
using Remixer.Core.Logging;
using Remixer.Core.Models;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Remixer.Core.AI;

public class AIRemixEngine
{
    private readonly AudioAnalyzer _analyzer;
    private readonly AIService _aiService;
    private AudioFeatures? _lastFeatures;
    private RemixSuggestion? _lastSuggestion;

    public AIRemixEngine(AIService aiService)
    {
        _analyzer = new AudioAnalyzer();
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
    }

    public event EventHandler<AnalysisProgressEventArgs>? AnalysisProgress;
    public event EventHandler<RemixSuggestionEventArgs>? SuggestionReady;

    public async Task<RemixSuggestion> RemixAsync(AudioEngine audioEngine, string? userPreference = null)
    {
        var sw = Stopwatch.StartNew();
        Logger.Info($"Starting AI remix process. User preference: {userPreference ?? "None"}");
        
        try
        {
            if (audioEngine == null)
                throw new ArgumentNullException(nameof(audioEngine));

            if (_aiService == null)
                throw new InvalidOperationException("AI service is not initialized");

            // Step 1: Analyze audio (use original file, not processed)
            OnAnalysisProgress("Analyzing audio features...");
            var filePath = audioEngine.CurrentFilePath;
            if (string.IsNullOrEmpty(filePath))
                throw new InvalidOperationException("No audio file loaded in engine");
            
            if (!System.IO.File.Exists(filePath))
                throw new InvalidOperationException($"Audio file not found: {filePath}");
            
            Logger.Debug($"Analyzing audio file: {filePath}");
            try
            {
                // Run audio analysis on background thread to avoid blocking UI
                _lastFeatures = await Task.Run(() => _analyzer.Analyze(filePath));
                Logger.Info($"Audio analysis complete: BPM={_lastFeatures.BPM:F1}, Energy={_lastFeatures.Energy:F2}, Duration={_lastFeatures.Duration:F1}s");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to analyze audio", ex);
                throw new InvalidOperationException($"Audio analysis failed: {ex.Message}", ex);
            }
            
            OnAnalysisProgress("Audio analysis complete");

            // Step 2: Get AI suggestion
            OnAnalysisProgress("Getting AI remix suggestion...");
            Logger.Debug("Calling AI service for remix suggestion...");
            try
            {
                _lastSuggestion = await _aiService.GetRemixSuggestionAsync(_lastFeatures, userPreference);
                Logger.Info($"AI suggestion received. Reasoning: {_lastSuggestion.Reasoning ?? "None"}, Confidence: {_lastSuggestion.Confidence:F2}");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to get AI suggestion", ex);
                throw new InvalidOperationException($"AI service error: {ex.Message}", ex);
            }
            
            OnAnalysisProgress("AI suggestion received");

            // Step 3: Apply suggestion
            OnAnalysisProgress("Applying remix parameters...");
            try
            {
                ApplySuggestion(audioEngine, _lastSuggestion);
                Logger.Info($"Applied AI remix settings: Tempo={_lastSuggestion.Settings.Tempo:F2}x, Pitch={_lastSuggestion.Settings.Pitch:F1}st");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to apply AI suggestion", ex);
                throw new InvalidOperationException($"Failed to apply remix settings: {ex.Message}", ex);
            }
            
            OnAnalysisProgress("Remix applied");

            OnSuggestionReady(_lastSuggestion);
            
            sw.Stop();
            Logger.LogPerformance("Complete AI remix process", sw.ElapsedMilliseconds);
            Logger.Info("AI remix completed successfully");
            
            return _lastSuggestion;
        }
        catch (Exception ex)
        {
            sw.Stop();
            Logger.Error($"AI remix process failed after {sw.ElapsedMilliseconds}ms", ex);
            OnAnalysisProgress($"Error: {ex.Message}");
            throw;
        }
    }

    public void ApplySuggestion(AudioEngine audioEngine, RemixSuggestion suggestion)
    {
        if (audioEngine == null || suggestion == null)
            return;

        // Mark all settings as AI-set
        suggestion.Settings.IsAISet = true;
        audioEngine.Settings = suggestion.Settings;
    }

    public void OverrideSetting(AudioEngine audioEngine, Action<AudioSettings> overrideAction)
    {
        if (audioEngine == null || overrideAction == null)
            return;

        var settings = audioEngine.Settings;
        overrideAction(settings);
        settings.IsAISet = false; // Mark as user-overridden
        audioEngine.Settings = settings;
    }

    public RemixSuggestion? GetLastSuggestion() => _lastSuggestion;
    public AudioFeatures? GetLastFeatures() => _lastFeatures;

    protected virtual void OnAnalysisProgress(string message)
    {
        AnalysisProgress?.Invoke(this, new AnalysisProgressEventArgs(message));
    }

    protected virtual void OnSuggestionReady(RemixSuggestion suggestion)
    {
        SuggestionReady?.Invoke(this, new RemixSuggestionEventArgs(suggestion));
    }
}

public class AnalysisProgressEventArgs : EventArgs
{
    public string Message { get; }

    public AnalysisProgressEventArgs(string message)
    {
        Message = message;
    }
}

public class RemixSuggestionEventArgs : EventArgs
{
    public RemixSuggestion Suggestion { get; }

    public RemixSuggestionEventArgs(RemixSuggestion suggestion)
    {
        Suggestion = suggestion;
    }
}

