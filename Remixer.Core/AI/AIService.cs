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
            
            if (string.IsNullOrWhiteSpace(response))
            {
                Logger.Warning("AI API returned empty response, using default suggestion");
                return CreateDefaultSuggestion(features);
            }
            
            return ParseAIResponse(response);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("empty content") || ex.Message.Contains("empty response"))
        {
            // If API returns empty content, try to create a reasonable default suggestion
            Logger.Warning($"AI API returned empty content: {ex.Message}. Creating default suggestion based on audio features.");
            return CreateDefaultSuggestion(features);
        }
        catch (Exception ex)
        {
            // Return default suggestion on error
            Logger.Error($"Error getting AI suggestion: {ex.Message}", ex);
            return new RemixSuggestion
            {
                Settings = new AudioSettings(),
                Reasoning = $"Error getting AI suggestion: {ex.Message}",
                Confidence = 0.0
            };
        }
    }
    
    private static readonly Random _random = new Random();
    
    public RemixSuggestion CreateDefaultSuggestion(AudioFeatures features)
    {
        // Create an intelligent remix suggestion based on audio features
        // Uses musical knowledge to create coherent, genre-appropriate combinations
        var random = new Random(); // Use new instance to avoid thread safety issues
        
        // Determine remix style based on genre and BPM
        string genre = features.Genre ?? "Unknown";
        bool isElectronic = genre.Contains("Electronic") || genre.Contains("Dance") || genre.Contains("Techno");
        bool isHighEnergy = features.Energy > 0.7;
        bool isFastBPM = features.BPM > 130;
        bool isSlowBPM = features.BPM < 100;
        bool isMidEnergy = features.Energy >= 0.4 && features.Energy <= 0.7;
        
        // Intelligent effect selection based on genre and characteristics
        // High-energy electronic: Compressor + Reverb + Chorus/Flanger
        // Pop/Rock: Compressor + Reverb + Chorus
        // Ambient/Chill: Reverb + Echo + Light Chorus
        // Fast BPM: Compressor + Tremolo for rhythm
        // Slow BPM: Reverb + Echo for depth
        
        bool enableReverb = true; // Reverb is almost always beneficial
        bool enableCompressor = isHighEnergy || isFastBPM || isElectronic || random.NextDouble() > 0.3; // Essential for punchy tracks, but also useful for others
        bool enableChorus = isMidEnergy || isElectronic || random.NextDouble() > 0.3; // Good for width - more likely
        bool enableFlanger = isElectronic ? random.NextDouble() > 0.3 : random.NextDouble() > 0.5; // Electronic flavor, but can work elsewhere
        bool enablePhaser = isElectronic ? random.NextDouble() > 0.4 : random.NextDouble() > 0.6; // Psychedelic effect - more accessible
        bool enableDistortion = isHighEnergy ? (isElectronic || random.NextDouble() > 0.4) : random.NextDouble() > 0.6; // Grit for high energy, but can add character
        bool enableSaturation = isMidEnergy ? random.NextDouble() > 0.3 : random.NextDouble() > 0.5; // Warmer alternative - more versatile
        bool enableTremolo = isFastBPM ? random.NextDouble() > 0.4 : random.NextDouble() > 0.6; // Rhythmic variation - works on slower tracks too
        bool enableVibrato = !isFastBPM ? random.NextDouble() > 0.4 : random.NextDouble() > 0.6; // Pitch modulation - can add expression to fast tracks
        bool enableEcho = isSlowBPM || !isHighEnergy ? random.NextDouble() > 0.3 : random.NextDouble() > 0.5; // Depth - very versatile
        bool enableFilter = isElectronic ? random.NextDouble() > 0.4 : random.NextDouble() > 0.6; // EQ shaping - useful for all genres
        bool enableBitcrusher = isElectronic ? random.NextDouble() > 0.5 : random.NextDouble() > 0.7; // Lo-fi effect - more accessible for electronic
        bool enableGate = isFastBPM && isHighEnergy ? random.NextDouble() > 0.4 : (isFastBPM ? random.NextDouble() > 0.6 : random.NextDouble() > 0.7); // Rhythmic gating - more versatile
        
        // Don't enable too many modulation effects at once (they can conflict)
        int modulationCount = (enableChorus ? 1 : 0) + (enableFlanger ? 1 : 0) + (enablePhaser ? 1 : 0);
        if (modulationCount > 2)
        {
            // Keep only the most appropriate ones
            if (modulationCount == 3)
            {
                // Prefer Chorus for most genres, Flanger for electronic
                if (!isElectronic) enablePhaser = false;
                else enableChorus = false;
            }
        }
        
        // Intelligent tempo adjustment based on BPM
        double tempo;
        if (isFastBPM)
        {
            // For fast tracks, slightly slow down for groove (0.95-1.05x)
            tempo = 0.95 + random.NextDouble() * 0.1;
        }
        else if (isSlowBPM)
        {
            // For slow tracks, speed up slightly for energy (1.0-1.1x)
            tempo = 1.0 + random.NextDouble() * 0.1;
        }
        else
        {
            // Mid-tempo: subtle variation (0.98-1.02x)
            tempo = 0.98 + random.NextDouble() * 0.04;
        }
        tempo = Math.Clamp(tempo, 0.85, 1.15); // Safe range
        
        // Intelligent pitch adjustment (subtle, musically appropriate)
        double pitch;
        if (isHighEnergy && isFastBPM)
        {
            // Slight pitch up for energy (+0.5 to +1.5 semitones)
            pitch = 0.5 + random.NextDouble() * 1.0;
        }
        else if (isSlowBPM && !isHighEnergy)
        {
            // Slight pitch down for warmth (-0.5 to -1.0 semitones)
            pitch = -1.0 + random.NextDouble() * 0.5;
        }
        else
        {
            // Neutral with slight variation (-0.5 to +0.5 semitones)
            pitch = (random.NextDouble() - 0.5) * 1.0;
        }
        pitch = Math.Clamp(pitch, -2.0, 2.0);
        
        var settings = new AudioSettings
        {
            Tempo = tempo,
            Pitch = pitch,
            Volume = 1.0 + (random.NextDouble() - 0.5) * 0.2, // 0.9 to 1.1
            Reverb = new ReverbSettings
            {
                Enabled = enableReverb,
                // Larger room for slower tracks, smaller for fast
                RoomSize = isSlowBPM ? 0.5 + random.NextDouble() * 0.3 : 0.3 + random.NextDouble() * 0.4,
                // More damping for high energy, less for ambient
                Damping = isHighEnergy ? 0.5 + random.NextDouble() * 0.3 : 0.3 + random.NextDouble() * 0.4,
                // Moderate wet level, slightly higher for electronic
                WetLevel = enableReverb ? (isElectronic ? 0.25 + random.NextDouble() * 0.25 : 0.2 + random.NextDouble() * 0.3) : 0.0
            },
            Echo = new EchoSettings 
            { 
                Enabled = enableEcho,
                Delay = 0.1 + random.NextDouble() * 0.3, // 0.1 to 0.4s
                Feedback = enableEcho ? 0.2 + random.NextDouble() * 0.3 : 0.0, // 0.2 to 0.5
                WetLevel = enableEcho ? 0.2 + random.NextDouble() * 0.2 : 0.0 // 0.2 to 0.4
            },
            Filter = new FilterSettings { Enabled = false },
            Chorus = new ChorusSettings 
            { 
                Enabled = enableChorus,
                Rate = enableChorus ? 0.5 + random.NextDouble() * 2.0 : 1.0, // 0.5 to 2.5 Hz
                Depth = enableChorus ? 0.3 + random.NextDouble() * 0.4 : 0.5, // 0.3 to 0.7
                Mix = enableChorus ? 0.3 + random.NextDouble() * 0.4 : 0.5 // 0.3 to 0.7
            },
            Flanger = new FlangerSettings 
            { 
                Enabled = enableFlanger,
                Delay = enableFlanger ? 0.003 + random.NextDouble() * 0.007 : 0.005, // 0.003 to 0.01s
                Rate = enableFlanger ? 0.3 + random.NextDouble() * 1.7 : 0.5, // 0.3 to 2.0 Hz
                Depth = enableFlanger ? 0.5 + random.NextDouble() * 0.3 : 0.8, // 0.5 to 0.8
                Feedback = enableFlanger ? 0.2 + random.NextDouble() * 0.4 : 0.3, // 0.2 to 0.6
                Mix = enableFlanger ? 0.3 + random.NextDouble() * 0.4 : 0.5 // 0.3 to 0.7
            },
            Distortion = new DistortionSettings 
            { 
                Enabled = enableDistortion,
                Drive = enableDistortion ? 0.3 + random.NextDouble() * 0.4 : 0.5, // 0.3 to 0.7
                Tone = enableDistortion ? 0.4 + random.NextDouble() * 0.3 : 0.5, // 0.4 to 0.7
                Mix = enableDistortion ? 0.4 + random.NextDouble() * 0.3 : 0.5 // 0.4 to 0.7
            },
            Compressor = new CompressorSettings
            {
                Enabled = enableCompressor,
                // Lower threshold for high energy tracks
                Threshold = enableCompressor ? (isHighEnergy ? -24.0 + random.NextDouble() * 6.0 : -18.0 + random.NextDouble() * 6.0) : -12.0,
                // Higher ratio for electronic/dance music
                Ratio = enableCompressor ? (isElectronic ? 4.0 + random.NextDouble() * 6.0 : 3.0 + random.NextDouble() * 5.0) : 4.0,
                // Faster attack for punchy tracks
                Attack = enableCompressor ? (isFastBPM ? 3.0 + random.NextDouble() * 10.0 : 5.0 + random.NextDouble() * 15.0) : 10.0,
                // Faster release for fast BPM
                Release = enableCompressor ? (isFastBPM ? 30.0 + random.NextDouble() * 70.0 : 50.0 + random.NextDouble() * 100.0) : 100.0,
                // Makeup gain based on compression amount
                MakeupGain = enableCompressor ? (isHighEnergy ? 1.0 + random.NextDouble() * 2.0 : random.NextDouble() * 2.0) : 0.0
            },
            Phaser = new PhaserSettings 
            { 
                Enabled = enablePhaser,
                Rate = enablePhaser ? 0.3 + random.NextDouble() * 1.7 : 0.5, // 0.3 to 2.0 Hz
                Depth = enablePhaser ? 0.5 + random.NextDouble() * 0.3 : 0.8, // 0.5 to 0.8
                Feedback = enablePhaser ? 0.1 + random.NextDouble() * 0.4 : 0.3, // 0.1 to 0.5
                Mix = enablePhaser ? 0.3 + random.NextDouble() * 0.4 : 0.5 // 0.3 to 0.7
            },
            Tremolo = new TremoloSettings 
            { 
                Enabled = enableTremolo,
                // Rate synced to BPM for rhythmic effect
                Rate = enableTremolo ? (features.BPM / 60.0 * 0.5) + random.NextDouble() * (features.BPM / 60.0 * 0.5) : 5.0,
                // Moderate depth for musicality
                Depth = enableTremolo ? 0.3 + random.NextDouble() * 0.3 : 0.5
            },
            Bitcrusher = new BitcrusherSettings
            {
                Enabled = enableBitcrusher,
                // Lower bit depth for more lo-fi character
                BitDepth = enableBitcrusher ? 4.0 + random.NextDouble() * 6.0 : 8.0,
                // Moderate downsampling for character without destroying the sound
                Downsample = enableBitcrusher ? 1.5 + random.NextDouble() * 2.0 : 2.0,
                // Lower mix to preserve clarity
                Mix = enableBitcrusher ? 0.2 + random.NextDouble() * 0.3 : 0.5
            },
            Vibrato = new VibratoSettings
            {
                Enabled = enableVibrato,
                // Slower rate for expressive vibrato
                Rate = enableVibrato ? 4.0 + random.NextDouble() * 3.0 : 5.0,
                // Subtle depth for musical vibrato
                Depth = enableVibrato ? 0.05 + random.NextDouble() * 0.1 : 0.1,
                // Full mix for vibrato (it's meant to be heard)
                Mix = enableVibrato ? 0.7 + random.NextDouble() * 0.3 : 1.0
            },
            Saturation = new SaturationSettings
            {
                Enabled = enableSaturation,
                // Moderate drive for warmth without harshness
                Drive = enableSaturation ? 0.4 + random.NextDouble() * 0.3 : 0.5,
                // Balanced tone
                Tone = enableSaturation ? 0.4 + random.NextDouble() * 0.3 : 0.5,
                // Moderate mix to preserve clarity
                Mix = enableSaturation ? 0.3 + random.NextDouble() * 0.4 : 0.5
            },
            Gate = new GateSettings
            {
                Enabled = enableGate,
                // Threshold based on energy level
                Threshold = enableGate ? (isHighEnergy ? 0.08 + random.NextDouble() * 0.12 : 0.05 + random.NextDouble() * 0.15) : 0.1,
                // Higher ratio for more pronounced gating
                Ratio = enableGate ? 8.0 + random.NextDouble() * 12.0 : 10.0,
                // Very fast attack for rhythmic gating
                Attack = enableGate ? 0.5 + random.NextDouble() * 1.5 : 1.0,
                // Fast release synced to BPM
                Release = enableGate ? (1000.0 / (features.BPM / 60.0) * 0.5) + random.NextDouble() * (1000.0 / (features.BPM / 60.0) * 0.5) : 50.0,
                Floor = enableGate ? 0.0 : 0.0
            }
        };
        
        // Build reasoning message
        var enabledEffects = new List<string>();
        if (enableReverb) enabledEffects.Add("Reverb");
        if (enableCompressor) enabledEffects.Add("Compressor");
        if (enableChorus) enabledEffects.Add("Chorus");
        if (enableFlanger) enabledEffects.Add("Flanger");
        if (enablePhaser) enabledEffects.Add("Phaser");
        if (enableDistortion) enabledEffects.Add("Distortion");
        if (enableSaturation) enabledEffects.Add("Saturation");
        if (enableTremolo) enabledEffects.Add("Tremolo");
        if (enableVibrato) enabledEffects.Add("Vibrato");
        if (enableEcho) enabledEffects.Add("Echo");
        if (enableBitcrusher) enabledEffects.Add("Bitcrusher");
        if (enableGate) enabledEffects.Add("Gate");
        
        var effectsList = enabledEffects.Count > 0 ? string.Join(", ", enabledEffects) : "None";
        var reasoning = $"Applied randomized remix settings based on audio analysis (BPM: {features.BPM:F1}, Energy: {features.Energy:F2}). " +
                       $"Effects enabled: {effectsList}. Tempo: {tempo:F2}x, Pitch: {pitch:F1}st. " +
                       $"AI API returned empty response, using randomized defaults.";
        
        return new RemixSuggestion
        {
            Settings = settings,
            Reasoning = reasoning,
            Confidence = 0.5
        };
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
        
        // Use higher temperature for remix suggestions to get more varied/creative results
        // For remix suggestions (no conversation history), use higher temperature
        // For chat (with conversation history), use moderate temperature
        double temperature = conversationHistory == null || conversationHistory.Count == 0 ? 0.9 : 0.7;
        
        var requestBody = new
        {
            model = model,
            messages = messages.ToArray(),
            temperature = temperature,
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
            
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                Logger.Error("AI API returned empty response");
                throw new InvalidOperationException("AI API returned an empty response");
            }
            
            // Parse response based on API format
            // DigitalOcean agents return OpenAI-compatible format
            try
            {
                var doc = JsonDocument.Parse(responseJson);
                
                // Try OpenAI-compatible format first
                if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    
                    // Log the structure for debugging
                    Logger.Debug($"First choice structure: {string.Join(", ", firstChoice.EnumerateObject().Select(p => p.Name))}");
                    
                    if (firstChoice.TryGetProperty("message", out var message))
                    {
                        Logger.Debug($"Message structure: {string.Join(", ", message.EnumerateObject().Select(p => p.Name))}");
                        
                        if (message.TryGetProperty("content", out var contentElement))
                        {
                            var contentStr = contentElement.GetString() ?? "";
                            Logger.Debug($"Extracted AI response content length: {contentStr.Length} characters");
                            
                            if (string.IsNullOrWhiteSpace(contentStr))
                            {
                                Logger.Warning("AI response content field is empty. Checking for alternatives...");
                                
                                // Try alternative content locations in the message
                                if (message.TryGetProperty("text", out var textElement))
                                {
                                    var textStr = textElement.GetString() ?? "";
                                    if (!string.IsNullOrWhiteSpace(textStr))
                                    {
                                        Logger.Debug($"Found content in 'text' field, length: {textStr.Length} characters");
                                        return textStr;
                                    }
                                }
                                
                                // Try delta format (for streaming responses)
                                if (firstChoice.TryGetProperty("delta", out var delta))
                                {
                                    if (delta.TryGetProperty("content", out var deltaContent))
                                    {
                                        var deltaStr = deltaContent.GetString() ?? "";
                                        if (!string.IsNullOrWhiteSpace(deltaStr))
                                        {
                                            Logger.Debug($"Found content in 'delta.content' field, length: {deltaStr.Length} characters");
                                            return deltaStr;
                                        }
                                    }
                                }
                                
                                // Log full response for debugging
                                Logger.Warning("Full response structure:");
                                Logger.Debug($"Response JSON: {responseJson.Substring(0, Math.Min(2000, responseJson.Length))}");
                                
                                // Try to get error message if present
                                if (doc.RootElement.TryGetProperty("error", out var error))
                                {
                                    var errorMsg = error.TryGetProperty("message", out var errorMessage) 
                                        ? errorMessage.GetString() 
                                        : "Unknown error";
                                    throw new InvalidOperationException($"AI API error: {errorMsg}");
                                }
                                
                                // Check if there's a finish_reason that might explain the empty content
                                if (firstChoice.TryGetProperty("finish_reason", out var finishReason))
                                {
                                    var reason = finishReason.GetString();
                                    Logger.Warning($"Finish reason: {reason}");
                                    if (reason == "length" || reason == "stop")
                                    {
                                        throw new InvalidOperationException($"AI API returned empty content. Finish reason: {reason}. The response may have been truncated or the model returned no content.");
                                    }
                                }
                                
                                throw new InvalidOperationException("AI API returned empty content in response. Check logs for full response structure.");
                            }
                            
                            return contentStr;
                        }
                        else
                        {
                            Logger.Warning("Message object does not contain 'content' property");
                            Logger.Debug($"Message properties: {string.Join(", ", message.EnumerateObject().Select(p => p.Name))}");
                        }
                    }
                    else
                    {
                        Logger.Warning("First choice does not contain 'message' property");
                        Logger.Debug($"First choice properties: {string.Join(", ", firstChoice.EnumerateObject().Select(p => p.Name))}");
                    }
                }
                
                // Try alternative format - direct content
                if (doc.RootElement.TryGetProperty("content", out var directContent))
                {
                    var contentStr = directContent.GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(contentStr))
                    {
                        Logger.Debug($"Extracted AI response from direct content field, length: {contentStr.Length} characters");
                        return contentStr;
                    }
                }
                
                // Try text field
                if (doc.RootElement.TryGetProperty("text", out var text))
                {
                    var textStr = text.GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(textStr))
                    {
                        Logger.Debug($"Extracted AI response from text field, length: {textStr.Length} characters");
                        return textStr;
                    }
                }

                Logger.Warning($"AI response does not contain expected structure. Response keys: {string.Join(", ", doc.RootElement.EnumerateObject().Select(p => p.Name))}");
                Logger.Debug($"Full response (first 1000 chars): {responseJson.Substring(0, Math.Min(1000, responseJson.Length))}");
                
                // If response looks like it might be plain text, return it
                if (!responseJson.TrimStart().StartsWith("{"))
                {
                    Logger.Debug("Response appears to be plain text, returning as-is");
                    return responseJson;
                }
                
                return responseJson;
            }
            catch (JsonException ex)
            {
                Logger.Error($"Failed to parse AI response as JSON: {ex.Message}");
                Logger.Debug($"Response content: {responseJson.Substring(0, Math.Min(500, responseJson.Length))}");
                
                // If it's not JSON, maybe it's plain text - return it
                if (!responseJson.TrimStart().StartsWith("{"))
                {
                    Logger.Debug("Response is not JSON, treating as plain text");
                    return responseJson;
                }
                
                throw;
            }
        }
        catch (HttpRequestException ex)
        {
            Logger.Error($"HTTP error calling AI service: {ex.Message}", ex);
            throw;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("empty content") || ex.Message.Contains("empty response"))
        {
            // Don't log as error for empty content - this will be handled gracefully by the caller
            Logger.Warning($"AI API returned empty content: {ex.Message}");
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
            if (string.IsNullOrWhiteSpace(response))
            {
                Logger.Error("ParseAIResponse called with empty response");
                throw new FormatException("AI response is empty");
            }
            
            Logger.Debug($"Parsing AI response. Response preview: {response.Substring(0, Math.Min(200, response.Length))}...");
            
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
                        Logger.Debug("Found JSON in code block");
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
                    Logger.Warning($"No JSON found in AI response. Response: {response}");
                    throw new FormatException("No JSON object found in AI response");
                }
                
                json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                Logger.Debug("Extracted JSON directly from response");
            }
            
            if (string.IsNullOrEmpty(json))
            {
                Logger.Warning($"Failed to extract JSON from response. Response: {response}");
                throw new FormatException("No JSON object found in AI response");
            }
            
            Logger.Debug($"Extracted JSON: {json.Substring(0, Math.Min(200, json.Length))}...");
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
            
            if (doc.RootElement.TryGetProperty("volume", out var volume))
            {
                suggestion.Settings.Volume = volume.GetDouble();
                Logger.Debug($"Parsed volume: {suggestion.Settings.Volume}");
            }
            
            if (doc.RootElement.TryGetProperty("tremolo", out var tremolo))
            {
                suggestion.Settings.Tremolo.Enabled = tremolo.TryGetProperty("enabled", out var enabled) && enabled.GetBoolean();
                if (tremolo.TryGetProperty("rate", out var rate))
                    suggestion.Settings.Tremolo.Rate = rate.GetDouble();
                if (tremolo.TryGetProperty("depth", out var depth))
                    suggestion.Settings.Tremolo.Depth = depth.GetDouble();
                Logger.Debug($"Parsed tremolo: Enabled={suggestion.Settings.Tremolo.Enabled}");
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
                Logger.Debug($"Parsed compressor: Enabled={suggestion.Settings.Compressor.Enabled}");
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
                Logger.Debug($"Parsed distortion: Enabled={suggestion.Settings.Distortion.Enabled}");
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
                Logger.Debug($"Parsed chorus: Enabled={suggestion.Settings.Chorus.Enabled}");
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
                Logger.Debug($"Parsed flanger: Enabled={suggestion.Settings.Flanger.Enabled}");
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
                Logger.Debug($"Parsed phaser: Enabled={suggestion.Settings.Phaser.Enabled}");
            }
            
            if (doc.RootElement.TryGetProperty("bitcrusher", out var bitcrusher))
            {
                suggestion.Settings.Bitcrusher.Enabled = bitcrusher.TryGetProperty("enabled", out var enabled) && enabled.GetBoolean();
                if (bitcrusher.TryGetProperty("bitDepth", out var bitDepth))
                    suggestion.Settings.Bitcrusher.BitDepth = bitDepth.GetDouble();
                if (bitcrusher.TryGetProperty("downsample", out var downsample))
                    suggestion.Settings.Bitcrusher.Downsample = downsample.GetDouble();
                if (bitcrusher.TryGetProperty("mix", out var mix))
                    suggestion.Settings.Bitcrusher.Mix = mix.GetDouble();
                Logger.Debug($"Parsed bitcrusher: Enabled={suggestion.Settings.Bitcrusher.Enabled}");
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
                Logger.Debug($"Parsed vibrato: Enabled={suggestion.Settings.Vibrato.Enabled}");
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
                Logger.Debug($"Parsed saturation: Enabled={suggestion.Settings.Saturation.Enabled}");
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
                Logger.Debug($"Parsed gate: Enabled={suggestion.Settings.Gate.Enabled}");
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

