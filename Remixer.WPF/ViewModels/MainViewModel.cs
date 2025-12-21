using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Remixer.Core.AI;
using Remixer.Core.Audio;
using Remixer.Core.Configuration;
using Remixer.Core.Logging;
using Remixer.Core.Models;
using Remixer.Core.Presets;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace Remixer.WPF.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AudioEngine _audioEngine = null!;
    private readonly PresetManager _presetManager = null!;
    private AIService? _aiService;
    private AIRemixEngine? _aiRemixEngine;
    private string? _currentAudioFile;

    [ObservableProperty]
    private AudioSettings _settings = new();
    
    [ObservableProperty]
    private bool _isAudioLoaded;
    
    [ObservableProperty]
    private bool _isProcessing;
    
    [ObservableProperty]
    private bool _isLoadingAudio;
    
    [ObservableProperty]
    private bool _isPlaying;
    
    [ObservableProperty]
    private string _currentPosition = "00:00";
    
    [ObservableProperty]
    private string _totalDuration = "00:00";
    
    [ObservableProperty]
    private double _playbackPosition;

    [ObservableProperty]
    private ObservableCollection<Preset> _presets = new();

    [ObservableProperty]
    private Preset? _selectedPreset;

    [ObservableProperty]
    private string _currentFile = "No file loaded";
    
    [ObservableProperty]
    private string _audioFileInfo = "";

    private string _aiStatus = "Ready";
    public string AIStatus
    {
        get => _aiStatus;
        set => SetProperty(ref _aiStatus, value);
    }

    private string _aiReasoning = "";
    public string AIReasoning
    {
        get => _aiReasoning;
        set => SetProperty(ref _aiReasoning, value);
    }

    private bool _isAISet;
    public bool IsAISet
    {
        get => _isAISet;
        set => SetProperty(ref _isAISet, value);
    }

    [ObservableProperty]
    private double _progress;

    public MainViewModel()
    {
        Logger.Info("=== MainViewModel Initialization Started ===");
        
        try
        {
            _audioEngine = new AudioEngine();
            _presetManager = new PresetManager();
            LoadPresets();
            InitializeAIService();
            
            // Subscribe to playback events
            _audioEngine.PlaybackStateChanged += OnPlaybackStateChanged;
            _audioEngine.PositionChanged += OnPositionChanged;
            
            Logger.Info($"MainViewModel initialized successfully. Log file: {Logger.GetLogFilePath()}");
        }
        catch (Exception ex)
        {
            // Log error but don't crash - show in status
            Logger.Critical("MainViewModel initialization failed", ex);
            AIStatus = $"Initialization error: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"MainViewModel initialization error: {ex}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }
    
    private void OnPlaybackStateChanged(object? sender, Remixer.Core.Audio.PlaybackStateChangedEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Logger.Debug($"Playback state changed to: {e.State}");
            IsPlaying = e.State == Remixer.Core.Audio.PlaybackState.Playing;
            Logger.Debug($"IsPlaying updated to: {IsPlaying}");
            
            PlayCommand.NotifyCanExecuteChanged();
            PauseCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
        });
    }
    
    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            CurrentPosition = $"{position:mm\\:ss}";
            var total = _audioEngine.TotalTime;
            TotalDuration = $"{total:mm\\:ss}";
            
            if (total.TotalSeconds > 0)
            {
                PlaybackPosition = position.TotalSeconds / total.TotalSeconds * 100.0;
            }
        });
    }

    private void InitializeAIService()
    {
        try
        {
            // Load configuration directly from code
            var apiEndpoint = AppConfiguration.AIEndpoint;
            var apiKey = AppConfiguration.AIKey;
            var modelName = AppConfiguration.AIModel;

            // Only initialize if endpoint is configured
            if (!string.IsNullOrEmpty(apiEndpoint))
            {
                _aiService = new AIService(apiEndpoint, apiKey, modelName);
                _aiRemixEngine = new AIRemixEngine(_aiService);
                _aiRemixEngine.AnalysisProgress += (s, e) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        AIStatus = e.Message;
                    });
                };
                _aiRemixEngine.SuggestionReady += (s, e) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        AIReasoning = e.Suggestion.Reasoning ?? "AI remix applied";
                        IsAISet = true;
                    });
                };
            }
        }
        catch
        {
            // AI service not available
            AIStatus = "AI service not configured";
        }
    }

    [RelayCommand(CanExecute = nameof(CanLoadAudio))]
    private async Task LoadAudioAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Audio Files|*.mp3;*.wav;*.flac;*.ogg;*.m4a;*.aac|All Files|*.*",
            Title = "Select Audio File"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                IsLoadingAudio = true;
                Progress = 0;
                AIStatus = "Loading audio file...";
                
                await Task.Run(() =>
                {
                    _currentAudioFile = dialog.FileName;
                    _audioEngine.LoadAudio(_currentAudioFile);
                });
                
                CurrentFile = Path.GetFileName(_currentAudioFile ?? "Unknown");
                IsAudioLoaded = true;
                Settings = _audioEngine.Settings;
                IsAISet = Settings.IsAISet;
                
                // Display audio file information
                var duration = _audioEngine.TotalTime;
                var format = _audioEngine.CurrentFormat;
                AudioFileInfo = $"Duration: {duration:mm\\:ss} | Format: {format?.Encoding} {format?.BitsPerSample}-bit | Sample Rate: {format?.SampleRate} Hz | Channels: {format?.Channels}";
                
                Progress = 100;
                AIStatus = "Audio loaded successfully";
                OnPropertyChanged(nameof(Settings));
                
                // Reset progress after a delay
                await Task.Delay(1000);
                Progress = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading audio: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                AIStatus = $"Error: {ex.Message}";
                IsAudioLoaded = false;
                CurrentFile = "No file loaded";
            }
            finally
            {
                IsLoadingAudio = false;
            }
        }
    }
    
    private bool CanLoadAudio() => !IsLoadingAudio && !IsProcessing;

    [RelayCommand(CanExecute = nameof(CanAIRemix))]
    private async Task AIRemix()
    {
        if (_aiRemixEngine == null || _currentAudioFile == null)
        {
            MessageBox.Show("AI service not configured or no audio loaded.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Logger.Info("Starting AI remix");
            
            // Save current playback state
            var wasPlaying = IsPlaying;
            var currentPosition = _audioEngine.CurrentTime;
            
            Logger.Debug($"Saved state: wasPlaying={wasPlaying}, position={currentPosition}");
            
            // Pause playback if playing
            if (wasPlaying)
            {
                Logger.Debug("Pausing playback for AI remix");
                _audioEngine.Pause();
            }
            
            IsProcessing = true;
            AIStatus = "Starting AI remix...";
            Progress = 0;

            await _aiRemixEngine.RemixAsync(_audioEngine);
            
            // Get the new settings from audio engine
            var newSettings = _audioEngine.Settings;
            
            // Unsubscribe from old settings
            var oldSettings = Settings;
            if (oldSettings != null)
            {
                oldSettings.PropertyChanged -= OnSettingsPropertyChanged;
                if (oldSettings.Reverb != null) oldSettings.Reverb.PropertyChanged -= OnSettingsPropertyChanged;
                if (oldSettings.Echo != null) oldSettings.Echo.PropertyChanged -= OnSettingsPropertyChanged;
                if (oldSettings.Filter != null) oldSettings.Filter.PropertyChanged -= OnSettingsPropertyChanged;
            }
            
            // Update settings
            Settings = newSettings;
            IsAISet = Settings.IsAISet;
            OnPropertyChanged(nameof(Settings));
            
            // Subscribe to new settings for real-time updates
            if (Settings != null)
            {
                Settings.PropertyChanged += OnSettingsPropertyChanged;
                if (Settings.Reverb != null) Settings.Reverb.PropertyChanged += OnSettingsPropertyChanged;
                if (Settings.Echo != null) Settings.Echo.PropertyChanged += OnSettingsPropertyChanged;
                if (Settings.Filter != null) Settings.Filter.PropertyChanged += OnSettingsPropertyChanged;
            }
            
            // Process audio with new AI settings, preserving position
            Logger.Debug($"Processing audio after AI remix, preserving position: {currentPosition}");
            _audioEngine.ProcessAudio(currentPosition);
            
            Progress = 100;
            AIStatus = "AI remix completed";
            
            // Restore playback if it was playing
            if (wasPlaying)
            {
                Logger.Debug($"Resuming playback from preserved position: {currentPosition}");
                _audioEngine.Play();
            }
            
            // Reset progress after a delay
            await Task.Delay(1000);
            Progress = 0;
            
            Logger.Info("AI remix completed successfully");
        }
        catch (Exception ex)
        {
            Logger.Error("AI remix failed", ex);
            MessageBox.Show($"Error during AI remix: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            AIStatus = $"Error: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }
    
    private bool CanAIRemix() => IsAudioLoaded && !IsProcessing && !IsLoadingAudio && _aiRemixEngine != null;

    [RelayCommand(CanExecute = nameof(CanApplyPreset))]
    private void ApplyPreset()
    {
        if (SelectedPreset == null)
            return;

        try
        {
            Logger.Info($"Applying preset: {SelectedPreset.Name}");
            
            // Temporarily disable real-time updates to prevent conflicts
            var oldSettings = Settings;
            if (oldSettings != null)
            {
                oldSettings.PropertyChanged -= OnSettingsPropertyChanged;
                if (oldSettings.Reverb != null) oldSettings.Reverb.PropertyChanged -= OnSettingsPropertyChanged;
                if (oldSettings.Echo != null) oldSettings.Echo.PropertyChanged -= OnSettingsPropertyChanged;
                if (oldSettings.Filter != null) oldSettings.Filter.PropertyChanged -= OnSettingsPropertyChanged;
            }
            
            // Save current playback state BEFORE any operations
            var wasPlaying = IsPlaying;
            var currentPosition = _audioEngine.CurrentTime;
            
            Logger.Debug($"Saved state: wasPlaying={wasPlaying}, position={currentPosition}");
            
            // Pause player if playing (we need to recreate it with new processed audio)
            // But preserve the position - we'll restore it after processing
            if (wasPlaying)
            {
                Logger.Debug("Pausing playback to apply preset");
                _audioEngine.Pause();
            }
            
            // Create new settings object
            var newSettings = new AudioSettings
            {
                Tempo = SelectedPreset.Settings.Tempo,
                Pitch = SelectedPreset.Settings.Pitch,
                Volume = SelectedPreset.Settings.Volume,
                Reverb = new ReverbSettings
                {
                    Enabled = SelectedPreset.Settings.Reverb.Enabled,
                    RoomSize = SelectedPreset.Settings.Reverb.RoomSize,
                    Damping = SelectedPreset.Settings.Reverb.Damping,
                    WetLevel = SelectedPreset.Settings.Reverb.WetLevel
                },
                Echo = new EchoSettings
                {
                    Enabled = SelectedPreset.Settings.Echo.Enabled,
                    Delay = SelectedPreset.Settings.Echo.Delay,
                    Feedback = SelectedPreset.Settings.Echo.Feedback,
                    WetLevel = SelectedPreset.Settings.Echo.WetLevel
                },
                Filter = new FilterSettings
                {
                    Enabled = SelectedPreset.Settings.Filter.Enabled,
                    LowCut = SelectedPreset.Settings.Filter.LowCut,
                    HighCut = SelectedPreset.Settings.Filter.HighCut,
                    LowGain = SelectedPreset.Settings.Filter.LowGain,
                    MidGain = SelectedPreset.Settings.Filter.MidGain,
                    HighGain = SelectedPreset.Settings.Filter.HighGain
                },
                IsAISet = false
            };

            // Update audio engine settings first
            _audioEngine.Settings = newSettings;
            
            // Now set the ViewModel property (this will subscribe to new settings)
            Settings = newSettings;
            IsAISet = false;
            OnPropertyChanged(nameof(Settings));
            
            // Subscribe to new settings for real-time updates
            if (Settings != null)
            {
                Settings.PropertyChanged += OnSettingsPropertyChanged;
                if (Settings.Reverb != null) Settings.Reverb.PropertyChanged += OnSettingsPropertyChanged;
                if (Settings.Echo != null) Settings.Echo.PropertyChanged += OnSettingsPropertyChanged;
                if (Settings.Filter != null) Settings.Filter.PropertyChanged += OnSettingsPropertyChanged;
            }
            
            // Process audio with new settings, preserving the current position
            Logger.Debug($"Processing audio after preset application, preserving position: {currentPosition}");
            _audioEngine.ProcessAudio(currentPosition);
            
            // Restore playback if it was playing
            if (wasPlaying)
            {
                Logger.Debug($"Resuming playback from preserved position: {currentPosition}");
                // Always create a new player with reprocessed audio
                _audioEngine.Play();
            }
            
            Logger.Info($"Preset '{SelectedPreset.Name}' applied successfully");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to apply preset: {SelectedPreset.Name}", ex);
            MessageBox.Show($"Error applying preset: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private bool CanApplyPreset() => IsAudioLoaded && !IsProcessing && !IsLoadingAudio && SelectedPreset != null;

    [RelayCommand(CanExecute = nameof(CanSavePreset))]
    private void SavePreset()
    {
        try
        {
            var dialog = new Views.SavePresetDialog
            {
                Owner = Application.Current.MainWindow
            };

            if (dialog.ShowDialog() == true)
            {
                var presetName = dialog.PresetName;
                var presetDescription = dialog.PresetDescription;
                
                // Check if preset already exists
                if (_presetManager.PresetNameExists(presetName))
                {
                    var result = MessageBox.Show(
                        $"A preset named '{presetName}' already exists. Do you want to overwrite it?",
                        "Preset Exists",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    
                    if (result != MessageBoxResult.Yes)
                        return;
                }
                
                // Create deep copy of settings to avoid reference issues
                var preset = new Preset
                {
                    Name = presetName,
                    Category = "Custom",
                    Description = string.IsNullOrWhiteSpace(presetDescription) 
                        ? "User-created preset" 
                        : presetDescription,
                    Settings = new AudioSettings
                    {
                        Tempo = Settings.Tempo,
                        Pitch = Settings.Pitch,
                        Volume = Settings.Volume,
                        Reverb = new ReverbSettings
                        {
                            Enabled = Settings.Reverb.Enabled,
                            RoomSize = Settings.Reverb.RoomSize,
                            Damping = Settings.Reverb.Damping,
                            WetLevel = Settings.Reverb.WetLevel
                        },
                        Echo = new EchoSettings
                        {
                            Enabled = Settings.Echo.Enabled,
                            Delay = Settings.Echo.Delay,
                            Feedback = Settings.Echo.Feedback,
                            WetLevel = Settings.Echo.WetLevel
                        },
                        Filter = new FilterSettings
                        {
                            Enabled = Settings.Filter.Enabled,
                            LowCut = Settings.Filter.LowCut,
                            HighCut = Settings.Filter.HighCut,
                            LowGain = Settings.Filter.LowGain,
                            MidGain = Settings.Filter.MidGain,
                            HighGain = Settings.Filter.HighGain
                        }
                    }
                };
                
                _presetManager.SavePreset(preset);
                LoadPresets();
                
                Logger.Info($"Preset '{presetName}' saved successfully");
                MessageBox.Show($"Preset '{presetName}' saved successfully!", "Success", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to save preset", ex);
            MessageBox.Show($"Error saving preset: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private bool CanSavePreset() => IsAudioLoaded && !IsProcessing && !IsLoadingAudio;
    
    [RelayCommand(CanExecute = nameof(CanDeletePreset))]
    private void DeletePreset()
    {
        if (SelectedPreset == null)
            return;
            
        try
        {
            var result = MessageBox.Show(
                $"Are you sure you want to delete the preset '{SelectedPreset.Name}'?",
                "Delete Preset",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                var presetName = SelectedPreset.Name;
                _presetManager.DeletePreset(SelectedPreset.Id);
                SelectedPreset = null;
                LoadPresets();
                
                Logger.Info($"Preset '{presetName}' deleted successfully");
                MessageBox.Show($"Preset '{presetName}' deleted successfully!", "Deleted", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to delete preset", ex);
            MessageBox.Show($"Error deleting preset: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private bool CanDeletePreset() => SelectedPreset != null && SelectedPreset.Category == "Custom";

    [RelayCommand(CanExecute = nameof(CanExportAudio))]
    private void ExportAudio()
    {
        if (_currentAudioFile == null)
        {
            MessageBox.Show("No audio loaded.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var defaultFileName = Path.GetFileNameWithoutExtension(_currentAudioFile) + "_remixed.wav";
            var exportDialog = new Views.ExportDialog(defaultFileName)
            {
                Owner = Application.Current.MainWindow
            };

            if (exportDialog.ShowDialog() == true)
            {
                Logger.Info($"Exporting audio to: {exportDialog.OutputPath} (SR:{exportDialog.SampleRate}, Ch:{exportDialog.Channels}, Bits:{exportDialog.BitsPerSample})");
                
                _audioEngine.Export(
                    exportDialog.OutputPath,
                    exportDialog.SampleRate,
                    exportDialog.Channels,
                    exportDialog.BitsPerSample);
                
                Logger.Info("Audio exported successfully");
                MessageBox.Show("Audio exported successfully!", "Success", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to export audio", ex);
            MessageBox.Show($"Error exporting audio: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private bool CanExportAudio() => IsAudioLoaded && !IsProcessing && !IsLoadingAudio;

    [RelayCommand]
    private void UpdateSettings()
    {
        if (_audioEngine != null)
        {
            Settings.IsAISet = false; // User override
            _audioEngine.Settings = Settings;
            IsAISet = false;
        }
    }

    private void LoadPresets()
    {
        Presets.Clear();
        foreach (var preset in _presetManager.Presets)
        {
            Presets.Add(preset);
        }
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var settingsWindow = new Views.SettingsWindow
        {
            Owner = Application.Current.MainWindow
        };

        if (settingsWindow.ShowDialog() == true)
        {
            // Reload AI service with new settings
            InitializeAIService();
            MessageBox.Show("Settings saved. AI service has been reloaded.", "Settings Updated", 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
    
    [RelayCommand]
    private void OpenLogFile()
    {
        try
        {
            var logPath = Logger.GetLogFilePath();
            if (!string.IsNullOrEmpty(logPath) && System.IO.File.Exists(logPath))
            {
                Logger.Info("User opened log file");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = logPath,
                    UseShellExecute = true
                });
            }
            else
            {
                MessageBox.Show("Log file not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to open log file", ex);
            MessageBox.Show($"Failed to open log file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            var logPath = Logger.GetLogFilePath();
            if (!string.IsNullOrEmpty(logPath))
            {
                var logFolder = System.IO.Path.GetDirectoryName(logPath);
                if (!string.IsNullOrEmpty(logFolder) && System.IO.Directory.Exists(logFolder))
                {
                    Logger.Info("User opened log folder");
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = logFolder,
                        UseShellExecute = true
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to open log folder", ex);
            MessageBox.Show($"Failed to open log folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    partial void OnSettingsChanged(AudioSettings? oldValue, AudioSettings newValue)
    {
        // Unsubscribe from old settings
        if (oldValue != null)
        {
            oldValue.PropertyChanged -= OnSettingsPropertyChanged;
            if (oldValue.Reverb != null) oldValue.Reverb.PropertyChanged -= OnSettingsPropertyChanged;
            if (oldValue.Echo != null) oldValue.Echo.PropertyChanged -= OnSettingsPropertyChanged;
            if (oldValue.Filter != null) oldValue.Filter.PropertyChanged -= OnSettingsPropertyChanged;
        }
        
        // Subscribe to new settings
        if (newValue != null)
        {
            newValue.PropertyChanged += OnSettingsPropertyChanged;
            if (newValue.Reverb != null) newValue.Reverb.PropertyChanged += OnSettingsPropertyChanged;
            if (newValue.Echo != null) newValue.Echo.PropertyChanged += OnSettingsPropertyChanged;
            if (newValue.Filter != null) newValue.Filter.PropertyChanged += OnSettingsPropertyChanged;
        }
        
        if (!IsProcessing && !IsLoadingAudio)
        {
            UpdateSettings();
        }
    }
    
    private System.Threading.Timer? _settingsUpdateTimer;
    private readonly object _settingsUpdateLock = new object();
    private volatile bool _isApplyingSettings = false;
    
    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Debounce rapid changes (e.g., dragging sliders)
        lock (_settingsUpdateLock)
        {
            _settingsUpdateTimer?.Dispose();
            _settingsUpdateTimer = new System.Threading.Timer(_ =>
            {
                try
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        if (!IsProcessing && !IsLoadingAudio && IsAudioLoaded && !_isApplyingSettings)
                        {
                            ApplySettingsRealtime();
                        }
                    });
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Error in settings update timer callback: {ex.Message}");
                }
            }, null, 150, System.Threading.Timeout.Infinite); // 150ms debounce
        }
    }
    
    private static int _applySettingsCount = 0;
    
    private void ApplySettingsRealtime()
    {
        // Prevent concurrent calls
        lock (_settingsUpdateLock)
        {
            if (_isApplyingSettings)
            {
                Logger.Debug("ApplySettingsRealtime skipped - already applying");
                return;
            }
            _isApplyingSettings = true;
        }
        
        try
        {
            _applySettingsCount++;
            Logger.Info($"=== ApplySettingsRealtime called (call #{_applySettingsCount}) ===");
            
            Logger.Debug($"Settings: Tempo={Settings.Tempo:F2}, Pitch={Settings.Pitch:F1}, Volume={Settings.Volume:F2}");
            Logger.Debug($"Effects: Reverb={Settings.Reverb.Enabled}, Echo={Settings.Echo.Enabled}, Filter={Settings.Filter.Enabled}");
            
            // Save current playback state BEFORE any operations
            var wasPlaying = IsPlaying;
            var engineIsPlaying = _audioEngine.IsPlaying;
            var currentPosition = _audioEngine.CurrentTime;
            var totalDuration = _audioEngine.TotalTime;
            
            Logger.Debug($"State: VM.IsPlaying={wasPlaying}, Engine.IsPlaying={engineIsPlaying}");
            Logger.Debug($"Position: {currentPosition} / {totalDuration}");
            
            // Clamp position to valid range
            if (currentPosition > totalDuration)
            {
                currentPosition = TimeSpan.Zero;
                Logger.Debug($"Position clamped to zero (was beyond total duration)");
            }
            
            // Stop playback if playing (we need to recreate it with new processed audio)
            // Use Stop instead of Pause to fully reset the player state
            if (wasPlaying || engineIsPlaying)
            {
                Logger.Debug("Stopping playback to apply new settings");
                try
                {
                    _audioEngine.Stop();
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Error stopping playback: {ex.Message}");
                }
            }
            
            // Update settings in audio engine
            Settings.IsAISet = false; // User override
            _audioEngine.Settings = Settings;
            IsAISet = false;
            
            // Update the current time so Play() will use it
            // Don't call ProcessAudio here - Play() will do it with the fresh position
            
            // Restore playback state if it was playing
            if (wasPlaying || engineIsPlaying)
            {
                Logger.Debug($"Calling Play() to resume playback from position: {currentPosition}");
                try
                {
                    // Set the current time before playing so Play() knows where to start
                    _audioEngine.Seek(currentPosition);
                    _audioEngine.Play();
                    Logger.Debug($"Play() returned. Engine.IsPlaying={_audioEngine.IsPlaying}");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to resume playback: {ex.Message}", ex);
                    IsPlaying = false;
                }
            }
            else
            {
                // Even if not playing, update the processed audio for when user clicks play
                // This ensures the new settings are applied
                try
                {
                    _audioEngine.ProcessAudio(currentPosition);
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Error processing audio: {ex.Message}");
                }
            }
            
            Logger.Info($"ApplySettingsRealtime #{_applySettingsCount} completed successfully");
        }
        catch (Exception ex)
        {
            Logger.Error($"ApplySettingsRealtime #{_applySettingsCount} FAILED", ex);
            // Reset playback state on error
            IsPlaying = false;
        }
        finally
        {
            lock (_settingsUpdateLock)
            {
                _isApplyingSettings = false;
            }
        }
    }
    
    partial void OnIsProcessingChanged(bool value)
    {
        LoadAudioCommand.NotifyCanExecuteChanged();
        AIRemixCommand.NotifyCanExecuteChanged();
        ApplyPresetCommand.NotifyCanExecuteChanged();
        SavePresetCommand.NotifyCanExecuteChanged();
        ExportAudioCommand.NotifyCanExecuteChanged();
    }
    
    partial void OnIsLoadingAudioChanged(bool value)
    {
        LoadAudioCommand.NotifyCanExecuteChanged();
        AIRemixCommand.NotifyCanExecuteChanged();
        ApplyPresetCommand.NotifyCanExecuteChanged();
        SavePresetCommand.NotifyCanExecuteChanged();
        ExportAudioCommand.NotifyCanExecuteChanged();
        PlayCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        
        Logger.Info($"IsLoadingAudio changed to: {value}");
    }
    
    partial void OnIsAudioLoadedChanged(bool value)
    {
        Logger.Info($"IsAudioLoaded changed to: {value}");
        LoadAudioCommand.NotifyCanExecuteChanged();
        AIRemixCommand.NotifyCanExecuteChanged();
        ApplyPresetCommand.NotifyCanExecuteChanged();
        SavePresetCommand.NotifyCanExecuteChanged();
        ExportAudioCommand.NotifyCanExecuteChanged();
        PlayCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        
        Logger.Info($"CanPlay() = {CanPlay()}, IsPlaying={IsPlaying}, IsProcessing={IsProcessing}, IsLoadingAudio={IsLoadingAudio}");
    }
    
    partial void OnSelectedPresetChanged(Preset? value)
    {
        ApplyPresetCommand.NotifyCanExecuteChanged();
        DeletePresetCommand.NotifyCanExecuteChanged();
    }
    
    partial void OnIsPlayingChanged(bool value)
    {
        Logger.Debug($"IsPlaying changed to: {value}");
        PlayCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }
    
    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Play()
    {
        try
        {
            Logger.Info($"Play command called. IsAudioLoaded={IsAudioLoaded}, IsPlaying={IsPlaying}, IsProcessing={IsProcessing}, IsLoadingAudio={IsLoadingAudio}");
            _audioEngine.Play();
            // IsPlaying will be set by OnPlaybackStateChanged event
            Logger.Info("Play command completed successfully");
        }
        catch (Exception ex)
        {
            Logger.Error("Play command failed", ex);
            IsPlaying = false;
            MessageBox.Show($"Error playing audio: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private bool CanPlay() => IsAudioLoaded && !IsPlaying && !IsProcessing && !IsLoadingAudio;
    
    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause()
    {
        try
        {
            Logger.Info($"Pause command called. IsPlaying={IsPlaying}");
            _audioEngine.Pause();
            IsPlaying = false;
            Logger.Info("Pause command completed successfully");
        }
        catch (Exception ex)
        {
            Logger.Error("Pause command failed", ex);
            IsPlaying = false;
            MessageBox.Show($"Error pausing audio: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private bool CanPause() => IsAudioLoaded && IsPlaying;
    
    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        try
        {
            Logger.Info($"Stop command called. IsPlaying={IsPlaying}, PlaybackPosition={PlaybackPosition}");
            _audioEngine.Stop();
            
            // Reset UI
            CurrentPosition = "00:00";
            PlaybackPosition = 0;
            IsPlaying = false;
            
            // Notify commands to update their enabled state
            PlayCommand.NotifyCanExecuteChanged();
            PauseCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
            
            Logger.Info("Stop command completed successfully");
        }
        catch (Exception ex)
        {
            Logger.Error("Stop command failed", ex);
            IsPlaying = false;
            MessageBox.Show($"Error stopping audio: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private bool CanStop() => IsAudioLoaded;
}


