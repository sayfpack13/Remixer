using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Remixer.Core.AI;
using Remixer.Core.Configuration;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace Remixer.WPF.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _apiEndpoint = "";

    [ObservableProperty]
    private string _apiKey = "";

    [ObservableProperty]
    private string _statusMessage = "";

    public event EventHandler? SettingsSaved;
    public event EventHandler? SettingsCancelled;

    public SettingsViewModel()
    {
        LoadSettings();
    }

    private void LoadSettings()
    {
        // Load configuration directly from code
        ApiEndpoint = AppConfiguration.AIEndpoint;
        ApiKey = AppConfiguration.AIKey;
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            // Save configuration directly to code class
            AppConfiguration.AIEndpoint = ApiEndpoint;
            AppConfiguration.AIKey = ApiKey;

            StatusMessage = "Settings saved successfully!";
            
            SettingsSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving settings: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        SettingsCancelled?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task TestConnection()
    {
        if (string.IsNullOrWhiteSpace(ApiEndpoint))
        {
            MessageBox.Show("Please enter an API endpoint.\n\nFor DigitalOcean agents, use the base URL:\nhttps://<agent-id>.ondigitalocean.app\nor\nhttps://<agent-id>.agents.do-ai.run", 
                "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            MessageBox.Show("Please enter an API key.\n\nFor DigitalOcean agents, use the Agent Access Key from the Settings tab.", 
                "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            StatusMessage = "Testing connection...";
            var aiService = new AIService(ApiEndpoint, ApiKey);
            
            // Test with a simple prompt - create a minimal test request
            var testFeatures = new AudioFeatures
            {
                BPM = 120,
                Energy = 0.5,
                Genre = "Test"
            };
            
            var suggestion = await aiService.GetRemixSuggestionAsync(testFeatures);
            
            if (suggestion.Confidence > 0 || !string.IsNullOrEmpty(suggestion.Reasoning))
            {
                StatusMessage = "Connection successful!";
                var responsePreview = string.IsNullOrEmpty(suggestion.Reasoning) 
                    ? "OK" 
                    : suggestion.Reasoning.Length > 100 
                        ? suggestion.Reasoning.Substring(0, 100) + "..." 
                        : suggestion.Reasoning;
                MessageBox.Show($"Connection test successful! The AI service is working correctly.\n\nResponse: {responsePreview}", 
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusMessage = "Connection failed - check endpoint and key";
                MessageBox.Show("Connection test failed. Please check:\n\n1. Endpoint URL is correct (base URL only)\n2. API key is valid\n3. Agent endpoint is set to private or public\n4. Network connection is working", 
                    "Connection Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection error: {ex.Message}";
            var errorMsg = $"Connection test failed: {ex.Message}";
            if (ex.InnerException != null)
            {
                errorMsg += $"\n\nInner exception: {ex.InnerException.Message}";
            }
            MessageBox.Show(errorMsg, "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

}

