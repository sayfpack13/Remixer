using Remixer.Core.Models;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Remixer.Core.Logging;

namespace Remixer.Core.AI;

public class AIService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiEndpoint;
    private readonly string? _apiKey;

    public AIService(string apiEndpoint, string? apiKey = null)
    {
        _apiEndpoint = apiEndpoint ?? throw new ArgumentNullException(nameof(apiEndpoint));
        _apiKey = apiKey;
        _httpClient = new HttpClient();
        
        if (!string.IsNullOrEmpty(_apiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
        }
    }

    public async Task<RemixSuggestion> GetRemixSuggestionAsync(AudioFeatures features, string? userPreference = null)
    {
        try
        {
            var prompt = PromptBuilder.BuildRemixPrompt(features, userPreference);
            var response = await CallAIAsync(prompt);
            return ParseAIResponse(response);
        }
        catch (Exception ex)
        {
            // Return default suggestion on error
            return new RemixSuggestion
            {
                Settings = new AudioSettings(),
                Reasoning = $"Error getting AI suggestion: {ex.Message}",
                Confidence = 0.0
            };
        }
    }

    private async Task<string> CallAIAsync(string prompt)
    {
        // DigitalOcean Agent API format
        // Endpoint should be: https://<agent-id>.ondigitalocean.app/api/v1/chat/completions
        // Or: https://<agent-id>.agents.do-ai.run/api/v1/chat/completions
        
        // Ensure endpoint has the correct path
        string endpoint = _apiEndpoint;
        if (!endpoint.EndsWith("/api/v1/chat/completions", StringComparison.OrdinalIgnoreCase) &&
            !endpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            // If endpoint is base URL, append the API path
            if (endpoint.EndsWith("/"))
                endpoint = endpoint + "api/v1/chat/completions";
            else
                endpoint = endpoint + "/api/v1/chat/completions";
        }
        else if (endpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase) && 
                 !endpoint.Contains("/api/v1/"))
        {
            // Fix old format: /v1/chat/completions -> /api/v1/chat/completions
            endpoint = endpoint.Replace("/v1/chat/completions", "/api/v1/chat/completions", StringComparison.OrdinalIgnoreCase);
        }
        
        // For DigitalOcean agents, model should be "n/a"
        // For other providers, use a default model name
        string model = (endpoint.Contains("ondigitalocean.app") || endpoint.Contains("agents.do-ai.run"))
            ? "n/a"
            : "gpt-4";
        
        var requestBody = new
        {
            model = model,
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            temperature = 0.7,
            max_tokens = 1000,
            stream = false,
            include_functions_info = false,
            include_retrieval_info = false,
            include_guardrails_info = false
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(endpoint, content);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"API request failed with status {response.StatusCode}: {errorContent}");
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        
        // Parse response based on API format
        // DigitalOcean agents return OpenAI-compatible format
        var doc = JsonDocument.Parse(responseJson);
        if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            return choices[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        }

        return responseJson;
    }

    private RemixSuggestion ParseAIResponse(string response)
    {
        try
        {
            // Try to extract JSON from response
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');
            
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var doc = JsonDocument.Parse(json);
                
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
                
                suggestion.Confidence = 0.8; // Default confidence
                suggestion.Settings.IsAISet = true;
                
                return suggestion;
            }
        }
        catch
        {
            // Fall through to default
        }

        // Return default if parsing fails
        return new RemixSuggestion
        {
            Settings = new AudioSettings(),
            Reasoning = "Could not parse AI response",
            Confidence = 0.0
        };
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

