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
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;

namespace Remixer.WPF.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AudioEngine _audioEngine;
    private readonly PresetManager _presetManager;
    private AIService? _aiService;
    private AIRemixEngine? _aiRemixEngine;
    private AIChatViewModel? _aiChatViewModel;
    private Views.AIChatWindow? _aiChatWindow;
    private string? _currentAudioFile;

    [ObservableProperty]
    private AudioSettings _settings = new();
    
    [ObservableProperty]
    private bool _isAudioLoaded;

    private bool _hasEverLoadedAudio;
    private AudioSettings? _lastAppliedSettingsSnapshot;

    public bool ShouldShowLoadAudioFirst => !IsAudioLoaded && !_hasEverLoadedAudio;
    
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
    
    private bool _isUserDraggingSlider = false;

    [ObservableProperty]
    private ObservableCollection<Preset> _presets = new();

    [ObservableProperty]
    private Preset? _selectedPreset;

    [ObservableProperty]
    private bool _isApplyingEffects;

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
            // Log critical error and re-throw - the application cannot function without these core components
            Logger.Critical("MainViewModel initialization failed", ex);
            System.Diagnostics.Debug.WriteLine($"MainViewModel initialization error: {ex}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            throw new InvalidOperationException("Failed to initialize MainViewModel", ex);
        }
    }
    
    private void OnPlaybackStateChanged(object? sender, Remixer.Core.Audio.PlaybackStateChangedEventArgs e)
    {
        if (Application.Current == null)
        {
            return;
        }

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
        if (Application.Current == null || _audioEngine == null)
        {
            return;
        }

        Application.Current.Dispatcher.Invoke(() =>
        {
            // Don't update slider position if user is dragging it
            if (_isUserDraggingSlider)
            {
                return;
            }

            // Format time display - use hours if needed
            CurrentPosition = FormatTime(position);
            var total = _audioEngine.TotalTime;
            TotalDuration = FormatTime(total);

            if (total.TotalSeconds > 0)
            {
                // Clamp position percentage to 0-100
                var percentage = position.TotalSeconds / total.TotalSeconds * 100.0;
                PlaybackPosition = Math.Max(0.0, Math.Min(100.0, percentage));
            }
            else
            {
                PlaybackPosition = 0;
            }
        });
    }
    
    public void SetUserDraggingSlider(bool isDragging)
    {
        _isUserDraggingSlider = isDragging;
    }
    
    private string FormatTime(TimeSpan time)
    {
        if (time.TotalHours >= 1)
        {
            return $"{(int)time.TotalHours:D2}:{time.Minutes:D2}:{time.Seconds:D2}";
        }
        else
        {
            return $"{time.Minutes:D2}:{time.Seconds:D2}";
        }
    }

    private void InitializeAIService()
    {
        try
        {
            // Load configuration directly from code
            var apiEndpoint = AppConfiguration.AIEndpoint;
            var apiKey = AppConfiguration.AIKey;

            // Only initialize if endpoint is configured
            if (!string.IsNullOrEmpty(apiEndpoint))
            {
                _aiService = new AIService(apiEndpoint, apiKey);
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
        if (_audioEngine == null)
        {
            Logger.Error("LoadAudioAsync called but _audioEngine is null");
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "Audio Files|*.mp3;*.wav;*.flac;*.ogg;*.m4a;*.aac|All Files|*.*",
            Title = "Select Audio File"
        };

        if (dialog.ShowDialog() == true)
        {
            var filePath = dialog.FileName;
            if (string.IsNullOrEmpty(filePath))
            {
                Logger.Error("LoadAudioAsync: File path is null or empty");
                return;
            }

            try
            {
                IsLoadingAudio = true;
                Progress = 0;
                AIStatus = "Loading audio file...";

                await Task.Run(() =>
                {
                    _currentAudioFile = filePath;
                    _audioEngine.LoadAudio(_currentAudioFile);
                });
                
                CurrentFile = Path.GetFileName(_currentAudioFile ?? "Unknown");
                IsAudioLoaded = true;
                Settings = _audioEngine.Settings;
                IsAISet = Settings.IsAISet;
                
                // Display audio file information and update position display
                var duration = _audioEngine.TotalTime;
                var format = _audioEngine.CurrentFormat;
                AudioFileInfo = $"Duration: {duration:mm\\:ss} | Format: {format?.Encoding} {format?.BitsPerSample}-bit | Sample Rate: {format?.SampleRate} Hz | Channels: {format?.Channels}";
                
                // Initialize position display
                CurrentPosition = "00:00";
                TotalDuration = FormatTime(duration);
                PlaybackPosition = 0;
                
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
        if (_audioEngine == null)
        {
            Logger.Error("AIRemix called but _audioEngine is null");
            return;
        }

        if (_aiRemixEngine == null || _currentAudioFile == null)
        {
            MessageBox.Show("AI service not configured or no audio loaded.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Logger.Info("Starting AI remix");

            // Save current playback state (but don't pause - keep playing)
            var wasPlaying = IsPlaying || _audioEngine.IsPlaying;
            var currentPosition = _audioEngine.CurrentTime;
            var totalDuration = _audioEngine.TotalTime;
            
            // Calculate position as percentage for better preservation across tempo changes
            double positionPercentage = 0.0;
            if (totalDuration.TotalSeconds > 0)
            {
                positionPercentage = currentPosition.TotalSeconds / totalDuration.TotalSeconds;
                positionPercentage = Math.Max(0.0, Math.Min(1.0, positionPercentage));
            }
            
            Logger.Debug($"Saved state: wasPlaying={wasPlaying}, position={currentPosition} ({positionPercentage:P2})");
            
            // Set processing state immediately to show indicator
            IsProcessing = true;
            AIStatus = "Starting AI remix...";
            Progress = 10;
            
            // Yield to UI thread to ensure indicator is visible immediately
            await Task.Yield();

            // Subscribe to progress updates
            EventHandler<AnalysisProgressEventArgs>? progressHandler = (s, e) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    AIStatus = e.Message;
                    // Update progress based on step
                    if (e.Message.Contains("Analyzing"))
                        Progress = 30;
                    else if (e.Message.Contains("Getting AI"))
                        Progress = 50;
                    else if (e.Message.Contains("Applying"))
                        Progress = 80;
                    else if (e.Message.Contains("complete") || e.Message.Contains("applied"))
                        Progress = 90;
                });
            };
            
            _aiRemixEngine.AnalysisProgress += progressHandler;

            try
            {
                // Run AI remix on background thread to avoid blocking UI
                await Task.Run(async () => await _aiRemixEngine.RemixAsync(_audioEngine));
            }
            finally
            {
                _aiRemixEngine.AnalysisProgress -= progressHandler;
            }
            
            // Get the new settings from audio engine (clone to decouple UI from engine instance)
            var newSettings = CloneSettings(_audioEngine.Settings);
            _lastAppliedSettingsSnapshot = CloneSettings(newSettings);
            
            // Unsubscribe from old settings
            var oldSettings = Settings;
            if (oldSettings != null)
            {
                oldSettings.PropertyChanged -= OnSettingsPropertyChanged;
                if (oldSettings.Reverb != null) oldSettings.Reverb.PropertyChanged -= OnSettingsPropertyChanged;
                if (oldSettings.Echo != null) oldSettings.Echo.PropertyChanged -= OnSettingsPropertyChanged;
                if (oldSettings.Filter != null) oldSettings.Filter.PropertyChanged -= OnSettingsPropertyChanged;
                if (oldSettings.Chorus != null) oldSettings.Chorus.PropertyChanged -= OnSettingsPropertyChanged;
                if (oldSettings.Flanger != null) oldSettings.Flanger.PropertyChanged -= OnSettingsPropertyChanged;
                if (oldSettings.Distortion != null) oldSettings.Distortion.PropertyChanged -= OnSettingsPropertyChanged;
                if (oldSettings.Compressor != null) oldSettings.Compressor.PropertyChanged -= OnSettingsPropertyChanged;
                if (oldSettings.Phaser != null) oldSettings.Phaser.PropertyChanged -= OnSettingsPropertyChanged;
                if (oldSettings.Tremolo != null) oldSettings.Tremolo.PropertyChanged -= OnSettingsPropertyChanged;
                if (oldSettings.Bitcrusher != null) oldSettings.Bitcrusher.PropertyChanged -= OnSettingsPropertyChanged;
                if (oldSettings.Vibrato != null) oldSettings.Vibrato.PropertyChanged -= OnSettingsPropertyChanged;
                if (oldSettings.Saturation != null) oldSettings.Saturation.PropertyChanged -= OnSettingsPropertyChanged;
                if (oldSettings.Gate != null) oldSettings.Gate.PropertyChanged -= OnSettingsPropertyChanged;
            }
            
            // Update settings (UI gets its own instance)
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
                if (Settings.Chorus != null) Settings.Chorus.PropertyChanged += OnSettingsPropertyChanged;
                if (Settings.Flanger != null) Settings.Flanger.PropertyChanged += OnSettingsPropertyChanged;
                if (Settings.Distortion != null) Settings.Distortion.PropertyChanged += OnSettingsPropertyChanged;
                if (Settings.Compressor != null) Settings.Compressor.PropertyChanged += OnSettingsPropertyChanged;
                if (Settings.Phaser != null) Settings.Phaser.PropertyChanged += OnSettingsPropertyChanged;
                if (Settings.Tremolo != null) Settings.Tremolo.PropertyChanged += OnSettingsPropertyChanged;
                if (Settings.Bitcrusher != null) Settings.Bitcrusher.PropertyChanged += OnSettingsPropertyChanged;
                if (Settings.Vibrato != null) Settings.Vibrato.PropertyChanged += OnSettingsPropertyChanged;
                if (Settings.Saturation != null) Settings.Saturation.PropertyChanged += OnSettingsPropertyChanged;
                if (Settings.Gate != null) Settings.Gate.PropertyChanged += OnSettingsPropertyChanged;
            }
            
            // Process audio with new AI settings in background, seamlessly transitioning playback
            // This will smoothly transition to new settings without stopping playback
            Logger.Debug($"Processing audio after AI remix, preserving position percentage: {positionPercentage:P2}");
            
            // Run processing on background thread
            await Task.Run(() =>
            {
                if (wasPlaying)
                {
                    // Use ProcessAudioAndPlay to seamlessly transition while maintaining playback
                    _audioEngine.ProcessAudioAndPlay(positionPercentage);
                }
                else
                {
                    // If not playing, just process the audio so it's ready when user clicks play
                    _audioEngine.ProcessAudio();
                }
            });
            
            Progress = 100;
            AIStatus = "AI remix completed";
            
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

    [RelayCommand]
    private async Task OpenAIChat()
    {
        if (_aiService == null)
        {
            MessageBox.Show("AI service not configured. Please configure it in Settings.", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!IsAudioLoaded)
        {
            MessageBox.Show("Please load an audio file first.", "No Audio Loaded", 
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Create or show chat window
        if (_aiChatWindow == null || !_aiChatWindow.IsLoaded)
        {
            if (_aiChatViewModel == null)
            {
                _aiChatViewModel = new AIChatViewModel(_aiService, _audioEngine);
                _aiChatViewModel.RemixSuggestionReady += OnChatRemixSuggestionReady;
            }
            
            _aiChatViewModel.SetAudioLoaded(IsAudioLoaded);
            _aiChatWindow = new Views.AIChatWindow(_aiChatViewModel);
            _aiChatWindow.Closed += (s, e) => { _aiChatWindow = null; };
            
            // Show window asynchronously to avoid blocking UI
            _aiChatWindow.Show();
            
            // Activate window on next UI frame to ensure it's responsive
            await Task.Yield();
            _aiChatWindow.Activate();
        }
        else
        {
            _aiChatWindow.Activate();
        }
    }

    private void OnChatRemixSuggestionReady(object? sender, RemixSuggestion suggestion)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                Logger.Info($"OnChatRemixSuggestionReady called. Tempo={suggestion.Settings.Tempo:F2}x, Pitch={suggestion.Settings.Pitch:F1}st");
                
                // Save current playback state
                var wasPlaying = IsPlaying;
                var currentPosition = _audioEngine.CurrentTime;

                // Pause playback if playing
                if (wasPlaying)
                {
                    Logger.Debug("Pausing playback to apply chat remix settings");
                    _audioEngine.Pause();
                }

                // Apply the suggestion
                suggestion.Settings.IsAISet = true;
                _audioEngine.Settings = suggestion.Settings;
                Logger.Debug("Applied settings to audio engine");

                // Update UI settings
                var newSettings = CloneSettings(_audioEngine.Settings);
                _lastAppliedSettingsSnapshot = CloneSettings(newSettings);

                // Unsubscribe from old settings
                var oldSettings = Settings;
                if (oldSettings != null)
                {
                    oldSettings.PropertyChanged -= OnSettingsPropertyChanged;
                    if (oldSettings.Reverb != null) oldSettings.Reverb.PropertyChanged -= OnSettingsPropertyChanged;
                    if (oldSettings.Echo != null) oldSettings.Echo.PropertyChanged -= OnSettingsPropertyChanged;
                    if (oldSettings.Filter != null) oldSettings.Filter.PropertyChanged -= OnSettingsPropertyChanged;
                    if (oldSettings.Chorus != null) oldSettings.Chorus.PropertyChanged -= OnSettingsPropertyChanged;
                    if (oldSettings.Flanger != null) oldSettings.Flanger.PropertyChanged -= OnSettingsPropertyChanged;
                    if (oldSettings.Distortion != null) oldSettings.Distortion.PropertyChanged -= OnSettingsPropertyChanged;
                    if (oldSettings.Compressor != null) oldSettings.Compressor.PropertyChanged -= OnSettingsPropertyChanged;
                    if (oldSettings.Phaser != null) oldSettings.Phaser.PropertyChanged -= OnSettingsPropertyChanged;
                    if (oldSettings.Tremolo != null) oldSettings.Tremolo.PropertyChanged -= OnSettingsPropertyChanged;
                }

                // Update settings
                Settings = newSettings;
                IsAISet = Settings.IsAISet;
                OnPropertyChanged(nameof(Settings));

                // Subscribe to new settings
                if (Settings != null)
                {
                    Settings.PropertyChanged += OnSettingsPropertyChanged;
                    if (Settings.Reverb != null) Settings.Reverb.PropertyChanged += OnSettingsPropertyChanged;
                    if (Settings.Echo != null) Settings.Echo.PropertyChanged += OnSettingsPropertyChanged;
                    if (Settings.Filter != null) Settings.Filter.PropertyChanged += OnSettingsPropertyChanged;
                    if (Settings.Chorus != null) Settings.Chorus.PropertyChanged += OnSettingsPropertyChanged;
                    if (Settings.Flanger != null) Settings.Flanger.PropertyChanged += OnSettingsPropertyChanged;
                    if (Settings.Distortion != null) Settings.Distortion.PropertyChanged += OnSettingsPropertyChanged;
                    if (Settings.Compressor != null) Settings.Compressor.PropertyChanged += OnSettingsPropertyChanged;
                    if (Settings.Phaser != null) Settings.Phaser.PropertyChanged += OnSettingsPropertyChanged;
                    if (Settings.Tremolo != null) Settings.Tremolo.PropertyChanged += OnSettingsPropertyChanged;
                }

                // Process audio with new settings in background to avoid blocking UI
                Task.Run(() =>
                {
                    try
                    {
                        Logger.Debug($"Processing audio with chat remix settings, preserving position: {currentPosition}");
                        _audioEngine.ProcessAudio(currentPosition);
                        
                        // Restore playback if it was playing
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (wasPlaying)
                            {
                                Logger.Debug("Resuming playback after chat remix settings applied");
                                _audioEngine.Play();
                            }
                            
                            AIStatus = "Remix settings applied from chat";
                            AIReasoning = suggestion.Reasoning ?? "Settings applied from AI chat conversation";
                            Logger.Info("Chat remix settings applied successfully");
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Error processing audio with chat remix settings", ex);
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show($"Error applying remix settings: {ex.Message}", "Error", 
                                MessageBoxButton.OK, MessageBoxImage.Error);
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error("Error applying remix suggestion from chat", ex);
                MessageBox.Show($"Error applying remix settings: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanApplyPreset))]
    private void ApplyPreset()
    {
        if (_audioEngine == null)
        {
            Logger.Error("ApplyPreset called but _audioEngine is null");
            return;
        }

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
                if (oldSettings.Chorus != null) oldSettings.Chorus.PropertyChanged -= OnSettingsPropertyChanged;
                if (oldSettings.Flanger != null) oldSettings.Flanger.PropertyChanged -= OnSettingsPropertyChanged;
                if (oldSettings.Distortion != null) oldSettings.Distortion.PropertyChanged -= OnSettingsPropertyChanged;
                if (oldSettings.Compressor != null) oldSettings.Compressor.PropertyChanged -= OnSettingsPropertyChanged;
                if (oldSettings.Phaser != null) oldSettings.Phaser.PropertyChanged -= OnSettingsPropertyChanged;
                if (oldSettings.Tremolo != null) oldSettings.Tremolo.PropertyChanged -= OnSettingsPropertyChanged;
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
                Chorus = new ChorusSettings
                {
                    Enabled = SelectedPreset.Settings.Chorus.Enabled,
                    Rate = SelectedPreset.Settings.Chorus.Rate,
                    Depth = SelectedPreset.Settings.Chorus.Depth,
                    Mix = SelectedPreset.Settings.Chorus.Mix
                },
                Flanger = new FlangerSettings
                {
                    Enabled = SelectedPreset.Settings.Flanger.Enabled,
                    Delay = SelectedPreset.Settings.Flanger.Delay,
                    Rate = SelectedPreset.Settings.Flanger.Rate,
                    Depth = SelectedPreset.Settings.Flanger.Depth,
                    Feedback = SelectedPreset.Settings.Flanger.Feedback,
                    Mix = SelectedPreset.Settings.Flanger.Mix
                },
                Distortion = new DistortionSettings
                {
                    Enabled = SelectedPreset.Settings.Distortion.Enabled,
                    Drive = SelectedPreset.Settings.Distortion.Drive,
                    Tone = SelectedPreset.Settings.Distortion.Tone,
                    Mix = SelectedPreset.Settings.Distortion.Mix
                },
                Compressor = new CompressorSettings
                {
                    Enabled = SelectedPreset.Settings.Compressor.Enabled,
                    Threshold = SelectedPreset.Settings.Compressor.Threshold,
                    Ratio = SelectedPreset.Settings.Compressor.Ratio,
                    Attack = SelectedPreset.Settings.Compressor.Attack,
                    Release = SelectedPreset.Settings.Compressor.Release,
                    MakeupGain = SelectedPreset.Settings.Compressor.MakeupGain
                },
                Phaser = new PhaserSettings
                {
                    Enabled = SelectedPreset.Settings.Phaser.Enabled,
                    Rate = SelectedPreset.Settings.Phaser.Rate,
                    Depth = SelectedPreset.Settings.Phaser.Depth,
                    Feedback = SelectedPreset.Settings.Phaser.Feedback,
                    Mix = SelectedPreset.Settings.Phaser.Mix
                },
                Tremolo = new TremoloSettings
                {
                    Enabled = SelectedPreset.Settings.Tremolo.Enabled,
                    Rate = SelectedPreset.Settings.Tremolo.Rate,
                    Depth = SelectedPreset.Settings.Tremolo.Depth
                },
                Bitcrusher = new BitcrusherSettings
                {
                    Enabled = SelectedPreset.Settings.Bitcrusher.Enabled,
                    BitDepth = SelectedPreset.Settings.Bitcrusher.BitDepth,
                    Downsample = SelectedPreset.Settings.Bitcrusher.Downsample,
                    Mix = SelectedPreset.Settings.Bitcrusher.Mix
                },
                Vibrato = new VibratoSettings
                {
                    Enabled = SelectedPreset.Settings.Vibrato.Enabled,
                    Rate = SelectedPreset.Settings.Vibrato.Rate,
                    Depth = SelectedPreset.Settings.Vibrato.Depth,
                    Mix = SelectedPreset.Settings.Vibrato.Mix
                },
                Saturation = new SaturationSettings
                {
                    Enabled = SelectedPreset.Settings.Saturation.Enabled,
                    Drive = SelectedPreset.Settings.Saturation.Drive,
                    Tone = SelectedPreset.Settings.Saturation.Tone,
                    Mix = SelectedPreset.Settings.Saturation.Mix
                },
                Gate = new GateSettings
                {
                    Enabled = SelectedPreset.Settings.Gate.Enabled,
                    Threshold = SelectedPreset.Settings.Gate.Threshold,
                    Ratio = SelectedPreset.Settings.Gate.Ratio,
                    Attack = SelectedPreset.Settings.Gate.Attack,
                    Release = SelectedPreset.Settings.Gate.Release,
                    Floor = SelectedPreset.Settings.Gate.Floor
                },
                IsAISet = false
            };

            // Update audio engine settings first (use a deep copy so the engine doesn't share UI instance)
            var engineSettings = CloneSettings(newSettings);
            _audioEngine.Settings = engineSettings;
            _lastAppliedSettingsSnapshot = engineSettings;
            
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
                if (Settings.Chorus != null) Settings.Chorus.PropertyChanged += OnSettingsPropertyChanged;
                if (Settings.Flanger != null) Settings.Flanger.PropertyChanged += OnSettingsPropertyChanged;
                if (Settings.Distortion != null) Settings.Distortion.PropertyChanged += OnSettingsPropertyChanged;
                if (Settings.Compressor != null) Settings.Compressor.PropertyChanged += OnSettingsPropertyChanged;
                if (Settings.Phaser != null) Settings.Phaser.PropertyChanged += OnSettingsPropertyChanged;
                if (Settings.Tremolo != null) Settings.Tremolo.PropertyChanged += OnSettingsPropertyChanged;
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
                        },
                        Chorus = new ChorusSettings
                        {
                            Enabled = Settings.Chorus.Enabled,
                            Rate = Settings.Chorus.Rate,
                            Depth = Settings.Chorus.Depth,
                            Mix = Settings.Chorus.Mix
                        },
                        Flanger = new FlangerSettings
                        {
                            Enabled = Settings.Flanger.Enabled,
                            Delay = Settings.Flanger.Delay,
                            Rate = Settings.Flanger.Rate,
                            Depth = Settings.Flanger.Depth,
                            Feedback = Settings.Flanger.Feedback,
                            Mix = Settings.Flanger.Mix
                        },
                        Distortion = new DistortionSettings
                        {
                            Enabled = Settings.Distortion.Enabled,
                            Drive = Settings.Distortion.Drive,
                            Tone = Settings.Distortion.Tone,
                            Mix = Settings.Distortion.Mix
                        },
                        Compressor = new CompressorSettings
                        {
                            Enabled = Settings.Compressor.Enabled,
                            Threshold = Settings.Compressor.Threshold,
                            Ratio = Settings.Compressor.Ratio,
                            Attack = Settings.Compressor.Attack,
                            Release = Settings.Compressor.Release,
                            MakeupGain = Settings.Compressor.MakeupGain
                        },
                        Phaser = new PhaserSettings
                        {
                            Enabled = Settings.Phaser.Enabled,
                            Rate = Settings.Phaser.Rate,
                            Depth = Settings.Phaser.Depth,
                            Feedback = Settings.Phaser.Feedback,
                            Mix = Settings.Phaser.Mix
                        },
                        Tremolo = new TremoloSettings
                        {
                            Enabled = Settings.Tremolo.Enabled,
                            Rate = Settings.Tremolo.Rate,
                            Depth = Settings.Tremolo.Depth
                        },
                        Bitcrusher = new BitcrusherSettings
                        {
                            Enabled = Settings.Bitcrusher.Enabled,
                            BitDepth = Settings.Bitcrusher.BitDepth,
                            Downsample = Settings.Bitcrusher.Downsample,
                            Mix = Settings.Bitcrusher.Mix
                        },
                        Vibrato = new VibratoSettings
                        {
                            Enabled = Settings.Vibrato.Enabled,
                            Rate = Settings.Vibrato.Rate,
                            Depth = Settings.Vibrato.Depth,
                            Mix = Settings.Vibrato.Mix
                        },
                        Saturation = new SaturationSettings
                        {
                            Enabled = Settings.Saturation.Enabled,
                            Drive = Settings.Saturation.Drive,
                            Tone = Settings.Saturation.Tone,
                            Mix = Settings.Saturation.Mix
                        },
                        Gate = new GateSettings
                        {
                            Enabled = Settings.Gate.Enabled,
                            Threshold = Settings.Gate.Threshold,
                            Ratio = Settings.Gate.Ratio,
                            Attack = Settings.Gate.Attack,
                            Release = Settings.Gate.Release,
                            Floor = Settings.Gate.Floor
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
    private async Task ExportAudioAsync()
    {
        if (_audioEngine == null)
        {
            Logger.Error("ExportAudio called but _audioEngine is null");
            return;
        }

        if (_currentAudioFile == null)
        {
            MessageBox.Show("No audio loaded.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Views.ExportProgressDialog? progressDialog = null;
        
        try
        {
            // Stop/pause audio playback before exporting to keep the process clean
            var wasPlaying = IsPlaying;
            if (wasPlaying)
            {
                Logger.Debug("Stopping audio playback before export");
                _audioEngine?.Stop();
            }
            
            // Determine default format based on loaded audio file
            var sourceExtension = Path.GetExtension(_currentAudioFile)?.ToLowerInvariant() ?? ".wav";
            var defaultFormat = sourceExtension switch
            {
                ".mp3" => ".mp3",
                ".m4a" => ".m4a",
                ".wma" => ".wma",
                _ => ".wav" // Default to WAV for unknown formats
            };
            
            var defaultFileName = Path.GetFileNameWithoutExtension(_currentAudioFile) + "_remixed" + defaultFormat;
            var exportDialog = new Views.ExportDialog(defaultFileName, defaultFormat)
            {
                Owner = Application.Current?.MainWindow
            };

            if (exportDialog.ShowDialog() == true)
            {
                Logger.Info($"Exporting audio to: {exportDialog.OutputPath} (SR:{exportDialog.SampleRate}, Ch:{exportDialog.Channels}, Bits:{exportDialog.BitsPerSample})");
                
                // Show progress dialog
                progressDialog = new Views.ExportProgressDialog
                {
                    Owner = Application.Current?.MainWindow
                };
                progressDialog.Show();
                
                // Create progress reporter
                var progress = new Progress<Remixer.Core.Audio.ExportProgressEventArgs>(args =>
                {
                    if (progressDialog != null)
                    {
                        if (args.ProgressPercent < 0)
                        {
                            progressDialog.SetError(args.Message);
                        }
                        else
                        {
                            progressDialog.UpdateProgress(args.ProgressPercent, args.Message);
                        }
                    }
                });
                
                // Export asynchronously
                if (_audioEngine != null)
                {
                    await _audioEngine.ExportAsync(
                        exportDialog.OutputPath,
                        exportDialog.SampleRate,
                        exportDialog.Channels,
                        exportDialog.BitsPerSample,
                        progress);
                }
                else
                {
                    throw new InvalidOperationException("Audio engine is not available");
                }
                
                Logger.Info("Audio exported successfully");
                
                // Close progress dialog
                progressDialog?.Close();
                progressDialog = null;
                
                MessageBox.Show("Audio exported successfully!", "Success", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                // User cancelled export - playback was already stopped, no need to restore
                Logger.Debug("Export cancelled by user");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to export audio", ex);
            
            if (progressDialog != null)
            {
                progressDialog.SetError(ex.Message);
                await Task.Delay(2000); // Show error for 2 seconds
                progressDialog.Close();
            }
            else
            {
                MessageBox.Show($"Error exporting audio: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
    
    private bool CanExportAudio() => IsAudioLoaded && !IsProcessing && !IsLoadingAudio;

    [RelayCommand]
    private void UpdateSettings()
    {
        if (_audioEngine != null)
        {
            Settings.IsAISet = false; // User override
            var engineSettings = CloneSettings(Settings);
            _audioEngine.Settings = engineSettings;
            _lastAppliedSettingsSnapshot = engineSettings;
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
            if (oldValue.Chorus != null) oldValue.Chorus.PropertyChanged -= OnSettingsPropertyChanged;
            if (oldValue.Flanger != null) oldValue.Flanger.PropertyChanged -= OnSettingsPropertyChanged;
            if (oldValue.Distortion != null) oldValue.Distortion.PropertyChanged -= OnSettingsPropertyChanged;
            if (oldValue.Compressor != null) oldValue.Compressor.PropertyChanged -= OnSettingsPropertyChanged;
            if (oldValue.Phaser != null) oldValue.Phaser.PropertyChanged -= OnSettingsPropertyChanged;
            if (oldValue.Tremolo != null) oldValue.Tremolo.PropertyChanged -= OnSettingsPropertyChanged;
            if (oldValue.Bitcrusher != null) oldValue.Bitcrusher.PropertyChanged -= OnSettingsPropertyChanged;
            if (oldValue.Vibrato != null) oldValue.Vibrato.PropertyChanged -= OnSettingsPropertyChanged;
            if (oldValue.Saturation != null) oldValue.Saturation.PropertyChanged -= OnSettingsPropertyChanged;
            if (oldValue.Gate != null) oldValue.Gate.PropertyChanged -= OnSettingsPropertyChanged;
        }
        
        // Subscribe to new settings
        if (newValue != null)
        {
            newValue.PropertyChanged += OnSettingsPropertyChanged;
            if (newValue.Reverb != null) newValue.Reverb.PropertyChanged += OnSettingsPropertyChanged;
            if (newValue.Echo != null) newValue.Echo.PropertyChanged += OnSettingsPropertyChanged;
            if (newValue.Filter != null) newValue.Filter.PropertyChanged += OnSettingsPropertyChanged;
            if (newValue.Chorus != null) newValue.Chorus.PropertyChanged += OnSettingsPropertyChanged;
            if (newValue.Flanger != null) newValue.Flanger.PropertyChanged += OnSettingsPropertyChanged;
            if (newValue.Distortion != null) newValue.Distortion.PropertyChanged += OnSettingsPropertyChanged;
            if (newValue.Compressor != null) newValue.Compressor.PropertyChanged += OnSettingsPropertyChanged;
            if (newValue.Phaser != null) newValue.Phaser.PropertyChanged += OnSettingsPropertyChanged;
            if (newValue.Tremolo != null) newValue.Tremolo.PropertyChanged += OnSettingsPropertyChanged;
            if (newValue.Bitcrusher != null) newValue.Bitcrusher.PropertyChanged += OnSettingsPropertyChanged;
            if (newValue.Vibrato != null) newValue.Vibrato.PropertyChanged += OnSettingsPropertyChanged;
            if (newValue.Saturation != null) newValue.Saturation.PropertyChanged += OnSettingsPropertyChanged;
            if (newValue.Gate != null) newValue.Gate.PropertyChanged += OnSettingsPropertyChanged;
        }
        
        if (!IsProcessing && !IsLoadingAudio)
        {
            UpdateSettings();
        }
    }
    
    private System.Threading.Timer? _settingsUpdateTimer;
    private readonly object _settingsUpdateLock = new object();
    private volatile bool _isApplyingSettings = false;
    private bool _disposed = false;
    
    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        // Debounce rapid changes (e.g., dragging sliders)
        lock (_settingsUpdateLock)
        {
            _settingsUpdateTimer?.Dispose();
            _settingsUpdateTimer = new System.Threading.Timer(_ =>
            {
                try
                {
                    if (_disposed)
                    {
                        return;
                    }

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

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;

            lock (_settingsUpdateLock)
            {
                _settingsUpdateTimer?.Dispose();
                _settingsUpdateTimer = null;
            }

            _audioEngine?.Dispose();
        }
    }
    
    private static int _applySettingsCount = 0;
    
    private void ApplySettingsRealtime()
    {
        if (_audioEngine == null)
        {
            Logger.Error("ApplySettingsRealtime called but _audioEngine is null");
            return;
        }

        // Check if settings actually changed to avoid unnecessary reprocessing.
        // IMPORTANT: the engine must not share the same Settings instance as the UI, otherwise changes
        // look "already applied" and realtime preview breaks.
        if (_lastAppliedSettingsSnapshot != null && SettingsEqual(_lastAppliedSettingsSnapshot, Settings))
        {
            Logger.Debug("Settings unchanged, skipping reprocessing");
            return;
        }

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

        // Set the applying effects indicator
        IsApplyingEffects = true;

        try
        {
            _applySettingsCount++;
            Logger.Info($"=== ApplySettingsRealtime called (call #{_applySettingsCount}) ===");

            Logger.Debug($"Settings: Tempo={Settings.Tempo:F2}, Pitch={Settings.Pitch:F1}, Volume={Settings.Volume:F2}");
            Logger.Debug($"Effects: Reverb={Settings.Reverb.Enabled}, Echo={Settings.Echo.Enabled}, Filter={Settings.Filter.Enabled}");

            // Save current playback state - these are atomic operations
            var wasPlaying = IsPlaying || _audioEngine.IsPlaying;
            var currentPosition = _audioEngine.CurrentTime;
            var totalDuration = _audioEngine.TotalTime;

            Logger.Debug($"State: wasPlaying={wasPlaying}");
            Logger.Debug($"Position: {currentPosition} / {totalDuration}");

            // Calculate position as a percentage for better preservation across tempo changes
            double positionPercentage = 0.0;
            if (totalDuration.TotalSeconds > 0)
            {
                positionPercentage = currentPosition.TotalSeconds / totalDuration.TotalSeconds;
                positionPercentage = Math.Max(0.0, Math.Min(1.0, positionPercentage)); // Clamp to 0-1
            }

            Logger.Debug($"Position percentage: {positionPercentage:P2}");
            
            // Pause playback if playing (we need to recreate the player with new effects)
            if (wasPlaying)
            {
                Logger.Debug("Pausing playback to apply new settings");
                try
                {
                    _audioEngine.Pause();
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Error pausing playback: {ex.Message}");
                }
            }
            
            // Update settings in audio engine (this updates the effects list)
            // Use a deep copy so the engine doesn't share the same instance as the UI bindings.
            Settings.IsAISet = false; // User override
            var engineSettings = CloneSettings(Settings);
            _audioEngine.Settings = engineSettings;
            _lastAppliedSettingsSnapshot = engineSettings;
            IsAISet = false;
            
            // Resume playback if it was playing
            if (wasPlaying)
            {
                Logger.Debug($"Restarting playback after settings change from {positionPercentage:P2} position");
                try
                {
                    // Process audio and start playback from the position percentage
                    _audioEngine.ProcessAudioAndPlay(positionPercentage);
                    Logger.Debug($"ProcessAudioAndPlay completed. Engine.IsPlaying={_audioEngine.IsPlaying}");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to resume playback: {ex.Message}", ex);
                    IsPlaying = false;
                }
            }
            else
            {
                // If not playing, just process the audio with new settings
                // so it's ready when user clicks play
                Logger.Debug($"Processing audio with new settings");
                try
                {
                    _audioEngine.ProcessAudio();
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
            // Clear the applying effects indicator after a short delay to make it visible
            Task.Delay(300).ContinueWith(_ =>
            {
                Application.Current?.Dispatcher?.Invoke(() => IsApplyingEffects = false);
            });
        }
    }

    private bool SettingsEqual(AudioSettings current, AudioSettings newSettings)
    {
        if (current == null || newSettings == null)
            return false;

        // Compare basic settings
        if (Math.Abs(current.Tempo - newSettings.Tempo) > 0.01 ||
            Math.Abs(current.Pitch - newSettings.Pitch) > 0.01 ||
            Math.Abs(current.Volume - newSettings.Volume) > 0.01)
            return false;

        // Compare reverb settings
        if (current.Reverb.Enabled != newSettings.Reverb.Enabled ||
            Math.Abs(current.Reverb.RoomSize - newSettings.Reverb.RoomSize) > 0.01 ||
            Math.Abs(current.Reverb.Damping - newSettings.Reverb.Damping) > 0.01 ||
            Math.Abs(current.Reverb.WetLevel - newSettings.Reverb.WetLevel) > 0.01)
            return false;

        // Compare echo settings
        if (current.Echo.Enabled != newSettings.Echo.Enabled ||
            Math.Abs(current.Echo.Delay - newSettings.Echo.Delay) > 0.01 ||
            Math.Abs(current.Echo.Feedback - newSettings.Echo.Feedback) > 0.01 ||
            Math.Abs(current.Echo.WetLevel - newSettings.Echo.WetLevel) > 0.01)
            return false;

        // Compare filter settings
        if (current.Filter.Enabled != newSettings.Filter.Enabled ||
            Math.Abs(current.Filter.LowCut - newSettings.Filter.LowCut) > 1 ||
            Math.Abs(current.Filter.HighCut - newSettings.Filter.HighCut) > 1 ||
            Math.Abs(current.Filter.LowGain - newSettings.Filter.LowGain) > 0.1 ||
            Math.Abs(current.Filter.MidGain - newSettings.Filter.MidGain) > 0.1 ||
            Math.Abs(current.Filter.HighGain - newSettings.Filter.HighGain) > 0.1)
            return false;

        // Compare chorus settings
        if (current.Chorus.Enabled != newSettings.Chorus.Enabled ||
            Math.Abs(current.Chorus.Rate - newSettings.Chorus.Rate) > 0.01 ||
            Math.Abs(current.Chorus.Depth - newSettings.Chorus.Depth) > 0.01 ||
            Math.Abs(current.Chorus.Mix - newSettings.Chorus.Mix) > 0.01)
            return false;

        // Compare flanger settings
        if (current.Flanger.Enabled != newSettings.Flanger.Enabled ||
            Math.Abs(current.Flanger.Delay - newSettings.Flanger.Delay) > 0.001 ||
            Math.Abs(current.Flanger.Rate - newSettings.Flanger.Rate) > 0.01 ||
            Math.Abs(current.Flanger.Depth - newSettings.Flanger.Depth) > 0.01 ||
            Math.Abs(current.Flanger.Feedback - newSettings.Flanger.Feedback) > 0.01 ||
            Math.Abs(current.Flanger.Mix - newSettings.Flanger.Mix) > 0.01)
            return false;

        // Compare distortion settings
        if (current.Distortion.Enabled != newSettings.Distortion.Enabled ||
            Math.Abs(current.Distortion.Drive - newSettings.Distortion.Drive) > 0.01 ||
            Math.Abs(current.Distortion.Tone - newSettings.Distortion.Tone) > 0.01 ||
            Math.Abs(current.Distortion.Mix - newSettings.Distortion.Mix) > 0.01)
            return false;

        // Compare compressor settings
        if (current.Compressor.Enabled != newSettings.Compressor.Enabled ||
            Math.Abs(current.Compressor.Threshold - newSettings.Compressor.Threshold) > 0.1 ||
            Math.Abs(current.Compressor.Ratio - newSettings.Compressor.Ratio) > 0.1 ||
            Math.Abs(current.Compressor.Attack - newSettings.Compressor.Attack) > 0.1 ||
            Math.Abs(current.Compressor.Release - newSettings.Compressor.Release) > 1.0 ||
            Math.Abs(current.Compressor.MakeupGain - newSettings.Compressor.MakeupGain) > 0.1)
            return false;

        // Compare phaser settings
        if (current.Phaser.Enabled != newSettings.Phaser.Enabled ||
            Math.Abs(current.Phaser.Rate - newSettings.Phaser.Rate) > 0.01 ||
            Math.Abs(current.Phaser.Depth - newSettings.Phaser.Depth) > 0.01 ||
            Math.Abs(current.Phaser.Feedback - newSettings.Phaser.Feedback) > 0.01 ||
            Math.Abs(current.Phaser.Mix - newSettings.Phaser.Mix) > 0.01)
            return false;

        // Compare tremolo settings
        if (current.Tremolo.Enabled != newSettings.Tremolo.Enabled ||
            Math.Abs(current.Tremolo.Rate - newSettings.Tremolo.Rate) > 0.01 ||
            Math.Abs(current.Tremolo.Depth - newSettings.Tremolo.Depth) > 0.01)
            return false;

        // Compare bitcrusher settings
        if (current.Bitcrusher.Enabled != newSettings.Bitcrusher.Enabled ||
            Math.Abs(current.Bitcrusher.BitDepth - newSettings.Bitcrusher.BitDepth) > 0.1 ||
            Math.Abs(current.Bitcrusher.Downsample - newSettings.Bitcrusher.Downsample) > 0.1 ||
            Math.Abs(current.Bitcrusher.Mix - newSettings.Bitcrusher.Mix) > 0.01)
            return false;

        // Compare vibrato settings
        if (current.Vibrato.Enabled != newSettings.Vibrato.Enabled ||
            Math.Abs(current.Vibrato.Rate - newSettings.Vibrato.Rate) > 0.01 ||
            Math.Abs(current.Vibrato.Depth - newSettings.Vibrato.Depth) > 0.01 ||
            Math.Abs(current.Vibrato.Mix - newSettings.Vibrato.Mix) > 0.01)
            return false;

        // Compare saturation settings
        if (current.Saturation.Enabled != newSettings.Saturation.Enabled ||
            Math.Abs(current.Saturation.Drive - newSettings.Saturation.Drive) > 0.01 ||
            Math.Abs(current.Saturation.Tone - newSettings.Saturation.Tone) > 0.01 ||
            Math.Abs(current.Saturation.Mix - newSettings.Saturation.Mix) > 0.01)
            return false;

        // Compare gate settings
        if (current.Gate.Enabled != newSettings.Gate.Enabled ||
            Math.Abs(current.Gate.Threshold - newSettings.Gate.Threshold) > 0.01 ||
            Math.Abs(current.Gate.Ratio - newSettings.Gate.Ratio) > 0.1 ||
            Math.Abs(current.Gate.Attack - newSettings.Gate.Attack) > 0.1 ||
            Math.Abs(current.Gate.Release - newSettings.Gate.Release) > 1.0 ||
            Math.Abs(current.Gate.Floor - newSettings.Gate.Floor) > 0.01)
            return false;

        return true;
    }

    private AudioSettings CloneSettings(AudioSettings source)
    {
        // Deep copy: engine must not share the same instance as the UI
        return new AudioSettings
        {
            Tempo = source.Tempo,
            Pitch = source.Pitch,
            Volume = source.Volume,
            IsAISet = source.IsAISet,
            Reverb = new ReverbSettings
            {
                Enabled = source.Reverb.Enabled,
                RoomSize = source.Reverb.RoomSize,
                Damping = source.Reverb.Damping,
                WetLevel = source.Reverb.WetLevel
            },
            Echo = new EchoSettings
            {
                Enabled = source.Echo.Enabled,
                Delay = source.Echo.Delay,
                Feedback = source.Echo.Feedback,
                WetLevel = source.Echo.WetLevel
            },
            Filter = new FilterSettings
            {
                Enabled = source.Filter.Enabled,
                LowCut = source.Filter.LowCut,
                HighCut = source.Filter.HighCut,
                LowGain = source.Filter.LowGain,
                MidGain = source.Filter.MidGain,
                HighGain = source.Filter.HighGain
            },
            Chorus = new ChorusSettings
            {
                Enabled = source.Chorus.Enabled,
                Rate = source.Chorus.Rate,
                Depth = source.Chorus.Depth,
                Mix = source.Chorus.Mix
            },
            Flanger = new FlangerSettings
            {
                Enabled = source.Flanger.Enabled,
                Delay = source.Flanger.Delay,
                Rate = source.Flanger.Rate,
                Depth = source.Flanger.Depth,
                Feedback = source.Flanger.Feedback,
                Mix = source.Flanger.Mix
            },
            Distortion = new DistortionSettings
            {
                Enabled = source.Distortion.Enabled,
                Drive = source.Distortion.Drive,
                Tone = source.Distortion.Tone,
                Mix = source.Distortion.Mix
            },
            Compressor = new CompressorSettings
            {
                Enabled = source.Compressor.Enabled,
                Threshold = source.Compressor.Threshold,
                Ratio = source.Compressor.Ratio,
                Attack = source.Compressor.Attack,
                Release = source.Compressor.Release,
                MakeupGain = source.Compressor.MakeupGain
            },
            Phaser = new PhaserSettings
            {
                Enabled = source.Phaser.Enabled,
                Rate = source.Phaser.Rate,
                Depth = source.Phaser.Depth,
                Feedback = source.Phaser.Feedback,
                Mix = source.Phaser.Mix
            },
            Tremolo = new TremoloSettings
            {
                Enabled = source.Tremolo.Enabled,
                Rate = source.Tremolo.Rate,
                Depth = source.Tremolo.Depth
            },
            Bitcrusher = new BitcrusherSettings
            {
                Enabled = source.Bitcrusher.Enabled,
                BitDepth = source.Bitcrusher.BitDepth,
                Downsample = source.Bitcrusher.Downsample,
                Mix = source.Bitcrusher.Mix
            },
            Vibrato = new VibratoSettings
            {
                Enabled = source.Vibrato.Enabled,
                Rate = source.Vibrato.Rate,
                Depth = source.Vibrato.Depth,
                Mix = source.Vibrato.Mix
            },
            Saturation = new SaturationSettings
            {
                Enabled = source.Saturation.Enabled,
                Drive = source.Saturation.Drive,
                Tone = source.Saturation.Tone,
                Mix = source.Saturation.Mix
            },
            Gate = new GateSettings
            {
                Enabled = source.Gate.Enabled,
                Threshold = source.Gate.Threshold,
                Ratio = source.Gate.Ratio,
                Attack = source.Gate.Attack,
                Release = source.Gate.Release,
                Floor = source.Gate.Floor
            }
        };
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
        if (value)
        {
            _hasEverLoadedAudio = true;
        }

        // Notify that ShouldShowLoadAudioFirst might have changed
        OnPropertyChanged(nameof(ShouldShowLoadAudioFirst));

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
        if (_audioEngine == null)
        {
            Logger.Error("Play command called but _audioEngine is null");
            return;
        }

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
        if (_audioEngine == null)
        {
            Logger.Error("Pause command called but _audioEngine is null");
            return;
        }

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
        if (_audioEngine == null)
        {
            Logger.Error("Stop command called but _audioEngine is null");
            return;
        }

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

    [RelayCommand(CanExecute = nameof(CanSeek))]
    private void Seek(double sliderValue)
    {
        if (_audioEngine == null || !IsAudioLoaded)
        {
            Logger.Debug("Seek called but audio engine is null or no audio loaded");
            return;
        }

        try
        {
            var totalDuration = _audioEngine.TotalTime;
            var seekPosition = TimeSpan.FromSeconds((sliderValue / 100.0) * totalDuration.TotalSeconds);

            Logger.Debug($"Seeking to position: {seekPosition} (slider value: {sliderValue}%)");
            _audioEngine.Seek(seekPosition);
        }
        catch (Exception ex)
        {
            Logger.Error("Seek command failed", ex);
        }
    }

    private bool CanSeek() => IsAudioLoaded && !IsProcessing && !IsLoadingAudio;
    
    private bool CanStop() => IsAudioLoaded;
}


