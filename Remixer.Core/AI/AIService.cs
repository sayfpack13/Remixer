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
            var response = await CallAIAsync(prompt, null);
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

    public async Task<string> SendChatMessageAsync(string prompt, System.Collections.Generic.List<ChatMessage>? conversationHistory = null)
    {
        try
        {
            var messages = conversationHistory?.Select(m => new { role = m.Role, content = m.Content }).Cast<object>().ToList();
            return await CallAIAsync(prompt, messages);
        }
        catch (Exception ex)
        {
            Logger.Error("Error sending chat message", ex);
            throw;
        }
    }

    private async Task<string> CallAIAsync(string prompt, System.Collections.Generic.List<object>? conversationHistory = null)
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
        
        Logger.Debug($"Calling AI endpoint: {endpoint}");
        
        // For DigitalOcean agents, model should be "n/a"
        // For other providers, use a default model name
        string model = (endpoint.Contains("ondigitalocean.app") || endpoint.Contains("agents.do-ai.run"))
            ? "n/a"
            : "gpt-4";
        
        // Build messages array with conversation history
        var messages = new System.Collections.Generic.List<object>();
        
        if (conversationHistory != null && conversationHistory.Count > 0)
        {
            messages.AddRange(conversationHistory);
        }
        
        // Add current user message
        messages.Add(new { role = "user", content = prompt });
        
        var requestBody = new
        {
            model = model,
            messages = messages.ToArray(),
            temperature = 0.7,
            max_tokens = conversationHistory != null && conversationHistory.Count > 0 ? 1500 : 1000,
            stream = false,
            include_functions_info = false,
            include_retrieval_info = false,
            include_guardrails_info = false
        };

        var json = JsonSerializer.Serialize(requestBody);
        Logger.Debug($"AI request body length: {json.Length} characters");
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(endpoint, content);
            Logger.Debug($"AI API response status: {response.StatusCode}");
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Logger.Error($"AI API request failed: {response.StatusCode} - {errorContent}");
                throw new HttpRequestException($"API request failed with status {response.StatusCode}: {errorContent}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            Logger.Debug($"AI API response length: {responseJson.Length} characters");
            
            // Parse response based on API format
            // DigitalOcean agents return OpenAI-compatible format
            var doc = JsonDocument.Parse(responseJson);
            if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var contentStr = choices[0].GetProperty("message").GetProperty("content").GetString() ?? "";
                Logger.Debug($"Extracted AI response content length: {contentStr.Length} characters");
                return contentStr;
            }

            Logger.Warning("AI response does not contain expected 'choices' structure");
            return responseJson;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error($"HTTP error calling AI service: {ex.Message}", ex);
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error($"Unexpected error calling AI service: {ex.Message}", ex);
            throw;
        }
    }

    private RemixSuggestion ParseAIResponse(string response)
    {
        try
        {
            Logger.Debug($"Parsing AI response. Response preview: {response.Substring(0, Math.Min(200, response.Length))}...");
            
            // Try to extract JSON from response
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');
            
            if (jsonStart < 0 || jsonEnd <= jsonStart)
            {
                Logger.Warning($"No JSON found in AI response. Response: {response}");
                throw new FormatException("No JSON object found in AI response");
            }
            
            var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
            Logger.Debug($"Extracted JSON: {json}");
            var doc = JsonDocument.Parse(json);
            
            var suggestion = new RemixSuggestion();
            
            if (doc.RootElement.TryGetProperty("tempo", out var tempo))
            {
                suggestion.Settings.Tempo = tempo.GetDouble();
                Logger.Debug($"Parsed tempo: {suggestion.Settings.Tempo}");
            }
            
            if (doc.RootElement.TryGetProperty("pitch", out var pitch))
            {
                suggestion.Settings.Pitch = pitch.GetDouble();
                Logger.Debug($"Parsed pitch: {suggestion.Settings.Pitch}");
            }
            
            if (doc.RootElement.TryGetProperty("reverb", out var reverb))
            {
                suggestion.Settings.Reverb.Enabled = reverb.TryGetProperty("enabled", out var enabled) && enabled.GetBoolean();
                if (reverb.TryGetProperty("roomSize", out var roomSize))
                    suggestion.Settings.Reverb.RoomSize = roomSize.GetDouble();
                if (reverb.TryGetProperty("damping", out var damping))
                    suggestion.Settings.Reverb.Damping = damping.GetDouble();
                if (reverb.TryGetProperty("wetLevel", out var wetLevel))
                    suggestion.Settings.Reverb.WetLevel = wetLevel.GetDouble();
                Logger.Debug($"Parsed reverb: Enabled={suggestion.Settings.Reverb.Enabled}");
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
                Logger.Debug($"Parsed echo: Enabled={suggestion.Settings.Echo.Enabled}");
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
                Logger.Debug($"Parsed filter: Enabled={suggestion.Settings.Filter.Enabled}");
            }
            
            if (doc.RootElement.TryGetProperty("reasoning", out var reasoning))
            {
                suggestion.Reasoning = reasoning.GetString();
                Logger.Debug($"Parsed reasoning: {suggestion.Reasoning}");
            }
            
            suggestion.Confidence = 0.8; // Default confidence
            suggestion.Settings.IsAISet = true;
            
            Logger.Info($"Successfully parsed AI response. Tempo={suggestion.Settings.Tempo:F2}x, Pitch={suggestion.Settings.Pitch:F1}st");
            return suggestion;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to parse AI response: {ex.Message}. Response: {response}", ex);
            // Return default if parsing fails
            return new RemixSuggestion
            {
                Settings = new AudioSettings(),
                Reasoning = $"Could not parse AI response: {ex.Message}",
                Confidence = 0.0
            };
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

