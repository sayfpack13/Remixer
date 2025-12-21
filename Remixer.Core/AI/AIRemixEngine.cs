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

            // Step 1: Analyze audio (use original file, not processed)
            OnAnalysisProgress("Analyzing audio features...");
            var filePath = audioEngine.CurrentFilePath;
            if (string.IsNullOrEmpty(filePath))
                throw new InvalidOperationException("No audio file loaded in engine");
            
            Logger.Debug($"Analyzing audio file: {filePath}");
            _lastFeatures = _analyzer.Analyze(filePath);
            OnAnalysisProgress("Audio analysis complete");
            Logger.Info($"Audio analysis complete: BPM={_lastFeatures.BPM}, Energy={_lastFeatures.Energy:F2}");

            // Step 2: Get AI suggestion
            OnAnalysisProgress("Getting AI remix suggestion...");
            _lastSuggestion = await _aiService.GetRemixSuggestionAsync(_lastFeatures, userPreference);
            OnAnalysisProgress("AI suggestion received");

            // Step 3: Apply suggestion
            OnAnalysisProgress("Applying remix parameters...");
            ApplySuggestion(audioEngine, _lastSuggestion);
            OnAnalysisProgress("Remix applied");

            OnSuggestionReady(_lastSuggestion);
            
            sw.Stop();
            Logger.LogPerformance("Complete AI remix process", sw.ElapsedMilliseconds);
            Logger.Info("AI remix completed successfully");
            
            return _lastSuggestion;
        }
        catch (Exception ex)
        {
            Logger.Error("AI remix process failed", ex);
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

