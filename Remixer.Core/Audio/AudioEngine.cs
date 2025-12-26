using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Remixer.Core.Audio.Effects;
using Remixer.Core.Models;
using Remixer.Core.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Remixer.Core.Audio;

public class AudioEngine : IDisposable
{
    private WaveStream? _currentStream;
    private ISampleProvider? _processedProvider;
    private readonly List<IEffect> _effects = new();
    private AudioSettings _settings = new();
    private bool _disposed = false;
    private string? _currentFilePath;
    private IWavePlayer? _wavePlayer;
    private System.Timers.Timer? _positionTimer;
    private TimeSpan _initialPlaybackOffset = TimeSpan.Zero;
    private DateTime _playbackStartTime;
    private TimeSpan _playbackStartOffset = TimeSpan.Zero;
    private int _processAudioCallCount = 0;
    private int _playCallCount = 0;
    
    // Lock object for thread synchronization to prevent race conditions
    // when rapidly changing settings or applying effects
    private readonly object _audioLock = new();
    private volatile bool _isProcessing = false;

    public AudioSettings Settings
    {
        get => _settings;
        set
        {
            _settings = value;
            UpdateEffects();
        }
    }

    public bool IsPlaying { get; private set; }
    public TimeSpan CurrentTime { get; private set; }
    public TimeSpan TotalTime
    {
        get
        {
            if (_currentStream == null)
                return TimeSpan.Zero;
            
            var baseDuration = _currentStream.TotalTime;
            
            // Account for tempo changes: tempo affects duration inversely
            // Tempo 2.0x means audio plays 2x faster, so duration is 1/2
            // Tempo 0.5x means audio plays 0.5x slower, so duration is 2x
            var tempo = _settings.Tempo;
            if (Math.Abs(tempo - 1.0) > 0.01 && tempo > 0)
            {
                return TimeSpan.FromSeconds(baseDuration.TotalSeconds / tempo);
            }
            
            return baseDuration;
        }
    }
    public WaveFormat? CurrentFormat => _currentStream?.WaveFormat;
    public string? CurrentFilePath => _currentFilePath;
    
    public event EventHandler<PlaybackStateChangedEventArgs>? PlaybackStateChanged;
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler<ExportProgressEventArgs>? ExportProgress;

    public void LoadAudio(string filePath)
    {
        var sw = Stopwatch.StartNew();
        Logger.Info($"Loading audio file: {filePath}");
        
        try
        {
            Stop(); // Stop playback if playing
            DisposeCurrentStream();
            _currentFilePath = filePath;
            _currentStream = AudioFileHandler.LoadAudioFile(filePath);
            ProcessAudio();
            
            sw.Stop();
            Logger.LogPerformance($"Load audio: {System.IO.Path.GetFileName(filePath)}", sw.ElapsedMilliseconds);
            Logger.Info($"Audio loaded successfully. Format: {_currentStream.WaveFormat}, Duration: {TotalTime}");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to load audio file: {filePath}", ex);
            throw;
        }
    }

    public void ProcessAudio()
    {
        ProcessAudio(TimeSpan.Zero);
    }

    public void ProcessAudio(TimeSpan preservePosition)
    {
        ProcessAudioInternal(preservePosition);
    }

    /// <summary>
    /// Process audio and immediately start playback from the specified position percentage
    /// </summary>
    public void ProcessAudioAndPlay(double positionPercentage)
    {
        lock (_audioLock)
        {
            // Stop current playback if any
            if (_wavePlayer != null && IsPlaying)
            {
                _wavePlayer.PlaybackStopped -= OnPlaybackStopped;
                _wavePlayer.Stop();
                _wavePlayer.Dispose();
                _wavePlayer = null;
                IsPlaying = false;
                StopPositionTimer();
            }

            // Process audio (start from beginning, we'll skip in provider)
            ProcessAudioInternal(TimeSpan.Zero);

            // Calculate start position based on percentage
            var newTotalTime = TotalTime;
            var startPosition = TimeSpan.FromSeconds(positionPercentage * newTotalTime.TotalSeconds);

            // Ensure we don't start too close to the end (leave at least 2 seconds)
            var minEndDistance = TimeSpan.FromSeconds(2);
            if (newTotalTime > minEndDistance && startPosition > newTotalTime - minEndDistance)
            {
                startPosition = newTotalTime - minEndDistance;
            }

            // For very short audio, just start from beginning
            if (newTotalTime < TimeSpan.FromSeconds(5))
            {
                startPosition = TimeSpan.Zero;
            }

            // Set position for playback
            CurrentTime = startPosition;
            _playbackStartOffset = startPosition;
            _initialPlaybackOffset = startPosition;

            // Create and start playback
            try
            {
                if (_processedProvider == null)
                {
                    throw new InvalidOperationException("Processed provider is null after processing");
                }

                if (_processedProvider.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat)
                {
                    throw new InvalidOperationException($"Processed provider must be in IEEE Float format, but got {_processedProvider.WaveFormat.Encoding}");
                }

                // Create skipped provider from desired start position
                var startProvider = CreateSkippedProvider(_processedProvider, startPosition);

                // Create new player
                Logger.Debug("Creating new WaveOutEvent for ProcessAudioAndPlay");
                _wavePlayer = new NAudio.Wave.WaveOutEvent();
                _wavePlayer.PlaybackStopped += OnPlaybackStopped;

                var waveProvider = new SampleToWaveProvider(startProvider);
                Logger.Debug($"WaveProvider format: {waveProvider.WaveFormat}");

                _wavePlayer.Init(waveProvider);
                _wavePlayer.Play();

                IsPlaying = true;

                // Track playback start time and offset
                // Set start time to now, and offset to the desired position
                // This ensures CurrentTime = _playbackStartOffset + elapsed starts at the correct position
                _playbackStartTime = DateTime.Now;
                _playbackStartOffset = startPosition;
                _initialPlaybackOffset = startPosition;

                OnPlaybackStateChanged(PlaybackState.Playing);
                StartPositionTimer();

                Logger.Info($"Audio playback started from position: {startPosition}");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to start playback in ProcessAudioAndPlay", ex);
                IsPlaying = false;
                StopPositionTimer();
                throw;
            }
        }
    }

    private void ProcessAudioInternal(TimeSpan preservePosition)
    {
        lock (_audioLock)
        {
            if (_isProcessing)
            {
                Logger.Warning("ProcessAudio called while already processing - skipping");
                return;
            }
            _isProcessing = true;
        }

        try
        {
            _processAudioCallCount++;
            Logger.Info($"=== ProcessAudioInternal called (call #{_processAudioCallCount}) ===");
            Logger.Debug($"preservePosition: {preservePosition}");

            if (_currentStream == null)
            {
                Logger.Warning("Cannot process audio: _currentStream is null");
                _processedProvider = null;
                return;
            }

            Logger.Debug($"Stream state before processing: Length={_currentStream.Length}, Position={_currentStream.Position}, CanSeek={_currentStream.CanSeek}");

            // Verify stream is seekable
            if (!_currentStream.CanSeek)
            {
                Logger.Error("Stream is not seekable - cannot reprocess audio");
                throw new InvalidOperationException("Audio stream is not seekable");
            }

            // Clamp preservePosition to valid range
            var totalTime = TotalTime;
            if (preservePosition > totalTime)
            {
                Logger.Warning($"preservePosition ({preservePosition}) exceeds TotalTime ({totalTime}), clamping to 0");
                preservePosition = TimeSpan.Zero;
            }

            // Reset stream to beginning for fresh processing
            // This ensures we start from a clean state
            _currentStream.Position = 0;

            // Verify the position was actually reset
            if (_currentStream.Position != 0)
            {
                Logger.Error($"Failed to reset stream position! Position is still {_currentStream.Position}");
            }

            Logger.Debug($"Processing audio with format: {_currentStream.WaveFormat}");
            Logger.Debug($"Stream length: {_currentStream.Length}, Position after reset: {_currentStream.Position}");

            // Verify the format is compatible with WaveToSampleProvider
            if (_currentStream.WaveFormat.Encoding != WaveFormatEncoding.Pcm &&
                _currentStream.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat)
            {
                Logger.Error($"Unsupported audio format for processing: {_currentStream.WaveFormat.Encoding}");
                throw new InvalidOperationException($"Audio format {_currentStream.WaveFormat.Encoding} is not supported. Expected PCM or IEEE Float.");
            }

            // Create a fresh WaveToSampleProvider from the stream
            // This converts the wave stream to float samples
            ISampleProvider provider = new WaveToSampleProvider(_currentStream);
            Logger.Debug($"Created WaveToSampleProvider. WaveFormat: {provider.WaveFormat}");

            // Apply effects in chain
            foreach (var effect in _effects.Where(e => e.IsEnabled))
            {
                var effectName = effect.GetType().Name;
                Logger.Debug($"Applying effect: {effectName}");
                provider = effect.Apply(provider);
                Logger.Debug($"After {effectName}, WaveFormat: {provider.WaveFormat}");
            }

            // Verify the final provider is valid
            if (provider == null)
            {
                throw new InvalidOperationException("Processed provider is null after applying effects");
            }

            // For ProcessAudioInternal, we don't use OffsetSampleProvider anymore
            // Position preservation is handled at the playback level
            CurrentTime = preservePosition > TimeSpan.Zero ? preservePosition : TimeSpan.Zero;
            _initialPlaybackOffset = CurrentTime;

            _processedProvider = provider;

            Logger.Info($"ProcessAudioInternal #{_processAudioCallCount} completed. Final format: {_processedProvider.WaveFormat}");
            Logger.Debug($"Stream position after processing: {_currentStream.Position}");
            Logger.Debug($"CurrentTime: {CurrentTime}, InitialOffset: {_initialPlaybackOffset}");
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to process audio", ex);
            _processedProvider = null; // Clear invalid provider
            throw;
        }
        finally
        {
            lock (_audioLock)
            {
                _isProcessing = false;
            }
        }
    }

    private void UpdateEffects()
    {
        _effects.Clear();

        // Tempo adjuster
        var tempoAdjuster = new TempoAdjuster
        {
            IsEnabled = Math.Abs(_settings.Tempo - 1.0) > 0.01,
            Tempo = _settings.Tempo
        };
        _effects.Add(tempoAdjuster);

        // Pitch shifter
        var pitchShifter = new PitchShifter
        {
            IsEnabled = Math.Abs(_settings.Pitch) > 0.01,
            PitchShift = _settings.Pitch
        };
        _effects.Add(pitchShifter);

        // Tremolo (amplitude modulation before other effects)
        var tremolo = new TremoloEffect
        {
            IsEnabled = _settings.Tremolo.Enabled,
            Rate = _settings.Tremolo.Rate,
            Depth = _settings.Tremolo.Depth
        };
        _effects.Add(tremolo);

        // Vibrato (pitch modulation)
        var vibrato = new VibratoEffect
        {
            IsEnabled = _settings.Vibrato.Enabled,
            Rate = _settings.Vibrato.Rate,
            Depth = _settings.Vibrato.Depth,
            Mix = _settings.Vibrato.Mix
        };
        _effects.Add(vibrato);

        // Gate (rhythmic gating)
        var gate = new GateEffect
        {
            IsEnabled = _settings.Gate.Enabled,
            Threshold = _settings.Gate.Threshold,
            Ratio = _settings.Gate.Ratio,
            Attack = _settings.Gate.Attack,
            Release = _settings.Gate.Release,
            Floor = _settings.Gate.Floor
        };
        _effects.Add(gate);

        // Compressor (dynamic processing)
        var compressor = new CompressorEffect
        {
            IsEnabled = _settings.Compressor.Enabled,
            Threshold = _settings.Compressor.Threshold,
            Ratio = _settings.Compressor.Ratio,
            Attack = _settings.Compressor.Attack,
            Release = _settings.Compressor.Release,
            MakeupGain = _settings.Compressor.MakeupGain
        };
        _effects.Add(compressor);

        // Saturation (softer distortion)
        var saturation = new SaturationEffect
        {
            IsEnabled = _settings.Saturation.Enabled,
            Drive = _settings.Saturation.Drive,
            Tone = _settings.Saturation.Tone,
            Mix = _settings.Saturation.Mix
        };
        _effects.Add(saturation);

        // Distortion (non-linear processing)
        var distortion = new DistortionEffect
        {
            IsEnabled = _settings.Distortion.Enabled,
            Drive = _settings.Distortion.Drive,
            Tone = _settings.Distortion.Tone,
            Mix = _settings.Distortion.Mix
        };
        _effects.Add(distortion);

        // Bitcrusher (lo-fi effect)
        var bitcrusher = new BitcrusherEffect
        {
            IsEnabled = _settings.Bitcrusher.Enabled,
            BitDepth = _settings.Bitcrusher.BitDepth,
            Downsample = _settings.Bitcrusher.Downsample,
            Mix = _settings.Bitcrusher.Mix
        };
        _effects.Add(bitcrusher);

        // Chorus
        var chorus = new ChorusEffect
        {
            IsEnabled = _settings.Chorus.Enabled,
            Rate = _settings.Chorus.Rate,
            Depth = _settings.Chorus.Depth,
            Mix = _settings.Chorus.Mix
        };
        _effects.Add(chorus);

        // Flanger
        var flanger = new FlangerEffect
        {
            IsEnabled = _settings.Flanger.Enabled,
            Delay = _settings.Flanger.Delay,
            Rate = _settings.Flanger.Rate,
            Depth = _settings.Flanger.Depth,
            Feedback = _settings.Flanger.Feedback,
            Mix = _settings.Flanger.Mix
        };
        _effects.Add(flanger);

        // Phaser
        var phaser = new PhaserEffect
        {
            IsEnabled = _settings.Phaser.Enabled,
            Rate = _settings.Phaser.Rate,
            Depth = _settings.Phaser.Depth,
            Feedback = _settings.Phaser.Feedback,
            Mix = _settings.Phaser.Mix
        };
        _effects.Add(phaser);

        // Reverb
        var reverb = new ReverbEffect
        {
            IsEnabled = _settings.Reverb.Enabled,
            RoomSize = _settings.Reverb.RoomSize,
            Damping = _settings.Reverb.Damping,
            WetLevel = _settings.Reverb.WetLevel
        };
        _effects.Add(reverb);

        // Echo
        var echo = new EchoEffect
        {
            IsEnabled = _settings.Echo.Enabled,
            Delay = _settings.Echo.Delay,
            Feedback = _settings.Echo.Feedback,
            WetLevel = _settings.Echo.WetLevel
        };
        _effects.Add(echo);

        // Filter
        var filter = new FilterEffect
        {
            IsEnabled = _settings.Filter.Enabled,
            LowCut = _settings.Filter.LowCut,
            HighCut = _settings.Filter.HighCut,
            LowGain = _settings.Filter.LowGain,
            MidGain = _settings.Filter.MidGain,
            HighGain = _settings.Filter.HighGain
        };
        _effects.Add(filter);

        // Volume effect - apply at the end of the chain
        var volume = new VolumeEffect
        {
            IsEnabled = Math.Abs(_settings.Volume - 1.0) > 0.01,
            Volume = _settings.Volume
        };
        _effects.Add(volume);
        
        // Don't auto-process here - let the caller control when to process
        // This prevents double processing during settings updates
    }

    public IWaveProvider? GetProcessedStream()
    {
        if (_processedProvider == null)
            return null;

        return new SampleToWaveProvider(_processedProvider);
    }

    public ISampleProvider? GetProcessedProvider()
    {
        return _processedProvider;
    }

    public Task ExportAsync(string outputPath, int sampleRate = 44100, int channels = 2, int bitsPerSample = 16, IProgress<ExportProgressEventArgs>? progress = null)
    {
        return Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            Logger.Info($"Exporting audio to: {outputPath} (SR:{sampleRate}, Ch:{channels}, Bits:{bitsPerSample})");
            
            try
            {
                if (_currentFilePath == null)
                    throw new InvalidOperationException("No audio loaded");

                ReportProgress(progress, 0, "Loading audio file...");
                
                // For export, we need to reprocess the audio from the original file
                // This avoids issues with MediaFoundation COM objects that can't be reused after reading
                Logger.Debug("Reprocessing audio from original file for export");
                
                // Reload the original file fresh to avoid COM interface issues
                using var originalStream = AudioFileHandler.LoadAudioFile(_currentFilePath);
                var totalDuration = originalStream.TotalTime;
                
                ReportProgress(progress, 10, "Processing effects...");
                
                // Create sample provider from original stream
                ISampleProvider sourceProvider = new WaveToSampleProvider(originalStream);
                
                // Apply all effects in the same order as playback
                int effectIndex = 0;
                int totalEffects = _effects.Count(e => e.IsEnabled);
                foreach (var effect in _effects.Where(e => e.IsEnabled))
                {
                    sourceProvider = effect.Apply(sourceProvider);
                    effectIndex++;
                    var progressPercent = 10 + (int)((effectIndex / (double)Math.Max(1, totalEffects)) * 20);
                    ReportProgress(progress, progressPercent, $"Applying {effect.GetType().Name}...");
                }
                
                ReportProgress(progress, 30, "Converting format...");
                
                // Convert to target format
                var targetFormat = new WaveFormat(sampleRate, bitsPerSample, channels);
                var waveProvider = new SampleToWaveProvider(sourceProvider);
                
                // Resample if needed - use MediaFoundationResampler with fresh stream (avoids COM reuse issues)
                IWaveProvider finalProvider = waveProvider;
                if (waveProvider.WaveFormat.SampleRate != sampleRate || waveProvider.WaveFormat.Channels != channels || waveProvider.WaveFormat.BitsPerSample != bitsPerSample)
                {
                    Logger.Debug($"Format conversion required: {waveProvider.WaveFormat} -> {targetFormat}");
                    ReportProgress(progress, 40, "Resampling audio...");
                    
                    // Use MediaFoundationResampler - since we're using a fresh stream, this should work
                    finalProvider = new MediaFoundationResampler(waveProvider, targetFormat);
                }
                
                ReportProgress(progress, 50, "Writing audio file...");
                
                // Save with progress reporting
                AudioFileHandler.SaveAudioFileWithProgress(finalProvider, outputPath, sampleRate, channels, bitsPerSample, totalDuration, 
                    (percent, message) => ReportProgress(progress, 50 + (int)(percent * 0.5), message));
                
                // Dispose if it was a resampler
                if (finalProvider != waveProvider && finalProvider is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                
                sw.Stop();
                Logger.LogPerformance($"Export audio: {System.IO.Path.GetFileName(outputPath)}", sw.ElapsedMilliseconds);
                Logger.Info("Audio exported successfully");
                
                ReportProgress(progress, 100, "Export complete!");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to export audio to: {outputPath}", ex);
                ReportProgress(progress, -1, $"Error: {ex.Message}");
                throw;
            }
        });
    }
    
    private void ReportProgress(IProgress<ExportProgressEventArgs>? progress, int percent, string message)
    {
        if (progress != null)
        {
            progress.Report(new ExportProgressEventArgs(percent, message));
        }
        ExportProgress?.Invoke(this, new ExportProgressEventArgs(percent, message));
    }

    public void Play()
    {
        // Wait for any ongoing processing to complete (with timeout)
        int waitAttempts = 0;
        while (_isProcessing && waitAttempts < 50) // Max 500ms wait
        {
            System.Threading.Thread.Sleep(10);
            waitAttempts++;
        }
        
        lock (_audioLock)
        {
            _playCallCount++;
            Logger.Info($"=== Play called (call #{_playCallCount}) ===");
            Logger.Debug($"_processedProvider is null: {_processedProvider == null}");
            Logger.Debug($"IsPlaying: {IsPlaying}");
            Logger.Debug($"_wavePlayer is null: {_wavePlayer == null}");
            Logger.Debug($"CurrentTime: {CurrentTime}, InitialOffset: {_initialPlaybackOffset}");
            Logger.Debug($"_isProcessing: {_isProcessing}");
            
            try
            {
                if (_currentStream == null)
                {
                    Logger.Error("Cannot play: _currentStream is null");
                    throw new InvalidOperationException("No audio loaded");
                }

                if (IsPlaying)
                {
                    Logger.Warning("Play called but already playing");
                    return;
                }

                // Always dispose existing player if it exists
                // This ensures we use the latest processed audio provider
                if (_wavePlayer != null)
                {
                    Logger.Debug("Disposing existing player to create new one");
                    try
                    {
                        // Unsubscribe from events BEFORE stopping to prevent OnPlaybackStopped from firing
                        _wavePlayer.PlaybackStopped -= OnPlaybackStopped;
                        _wavePlayer.Stop();
                        _wavePlayer.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"Error disposing old player: {ex.Message}");
                    }
                    _wavePlayer = null;
                }

                // Always reprocess audio to ensure we have a fresh provider chain
                // This fixes issues where the provider chain was consumed or in an invalid state
                Logger.Debug($"Reprocessing audio for fresh provider chain. CurrentTime: {CurrentTime}");
                ProcessAudioInternal(CurrentTime); // Preserve current time when processing

                // Verify processed provider is valid
                if (_processedProvider == null)
                {
                    throw new InvalidOperationException("Processed provider is null after processing");
                }

                // Verify the provider format is correct (should be float samples)
                Logger.Debug($"Processed provider WaveFormat: {_processedProvider.WaveFormat}");
                if (_processedProvider.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat)
                {
                    Logger.Error($"Processed provider is not in IEEE Float format: {_processedProvider.WaveFormat.Encoding}");
                    throw new InvalidOperationException($"Processed provider must be in IEEE Float format, but got {_processedProvider.WaveFormat.Encoding}");
                }

                // Create new player
                Logger.Debug("Creating new WaveOutEvent");
                _wavePlayer = new NAudio.Wave.WaveOutEvent();
                _wavePlayer.PlaybackStopped += OnPlaybackStopped;

                // Initialize with processed audio starting at CurrentTime
                Logger.Debug("Creating SampleToWaveProvider from processed provider with seek");
                var startProvider = CreateSkippedProvider(_processedProvider, CurrentTime);
                var waveProvider = new SampleToWaveProvider(startProvider);
                Logger.Debug($"WaveProvider format: {waveProvider.WaveFormat}");
                
                Logger.Debug("Initializing WaveOutEvent");
                _wavePlayer.Init(waveProvider);
                
                Logger.Debug("Starting playback");
                _wavePlayer.Play();

                IsPlaying = true;

                // Track playback start time and offset for position tracking
                _playbackStartTime = DateTime.Now;
                _playbackStartOffset = CurrentTime;
                _initialPlaybackOffset = CurrentTime;
                
                OnPlaybackStateChanged(PlaybackState.Playing);

                // Start position timer
                StartPositionTimer();
                
                Logger.Info("Audio playback started successfully");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to start audio playback", ex);
                IsPlaying = false;
                _processedProvider = null; // Clear potentially invalid provider
                StopPositionTimer();
                throw;
            }
        } // end lock
    }

    public void Pause()
    {
        lock (_audioLock)
        {
            Logger.Info($"Pause called. _wavePlayer null: {_wavePlayer == null}, IsPlaying: {IsPlaying}");
            
            if (_wavePlayer == null)
            {
                Logger.Warning("Cannot pause: wave player is null");
                IsPlaying = false;
                return;
            }

            try
            {
                // Unsubscribe from events to prevent OnPlaybackStopped from interfering
                _wavePlayer.PlaybackStopped -= OnPlaybackStopped;
                _wavePlayer.Pause();
                IsPlaying = false;
                StopPositionTimer();
                OnPlaybackStateChanged(PlaybackState.Paused);
                // Re-subscribe for future events
                _wavePlayer.PlaybackStopped += OnPlaybackStopped;
                Logger.Info("Audio paused successfully");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to pause audio", ex);
                IsPlaying = false;
                StopPositionTimer();
                // Re-subscribe even on error
                if (_wavePlayer != null)
                {
                    _wavePlayer.PlaybackStopped += OnPlaybackStopped;
                }
                throw;
            }
        }
    }

    public void Stop()
    {
        lock (_audioLock)
        {
            Logger.Info($"Stop called. _wavePlayer null: {_wavePlayer == null}");
            
            try
            {
                StopPositionTimer();
                
                if (_wavePlayer != null)
                {
                    // Unsubscribe from events to prevent OnPlaybackStopped from interfering
                    _wavePlayer.PlaybackStopped -= OnPlaybackStopped;
                    try
                    {
                        _wavePlayer.Stop();
                        _wavePlayer.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"Error stopping/disposing player: {ex.Message}");
                    }
                    _wavePlayer = null;
                }
                
                IsPlaying = false;
                
                // Reset stream position to beginning
                if (_currentStream != null && _currentStream.CanSeek)
                {
                    try
                    {
                        _currentStream.Position = 0;
                        Logger.Debug($"Stream position reset to 0");
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"Error resetting stream position: {ex.Message}");
                    }
                }
                
                // Clear the processed provider so it will be recreated fresh on next play
                _processedProvider = null;

                CurrentTime = TimeSpan.Zero;
                _initialPlaybackOffset = TimeSpan.Zero;
                _playbackStartOffset = TimeSpan.Zero;
                _playbackStartTime = DateTime.MinValue;
                
                OnPlaybackStateChanged(PlaybackState.Stopped);
                Logger.Info("Audio stopped successfully");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to stop audio", ex);
                IsPlaying = false;
                _processedProvider = null;
                CurrentTime = TimeSpan.Zero;
            }
        }
    }

    public void Seek(TimeSpan position)
    {
        lock (_audioLock)
        {
            if (_currentStream == null)
                return;

            // Clamp position to valid range
            if (position < TimeSpan.Zero)
                position = TimeSpan.Zero;
            if (position > TotalTime)
                position = TotalTime;

            Logger.Debug($"Seek called with position: {position}");

            // Update current time
            CurrentTime = position;
            _initialPlaybackOffset = position;
            _playbackStartOffset = position;

            // If currently playing, restart playback from the new position
            // Keep IsPlaying = true throughout to maintain playing state
            if (IsPlaying && _wavePlayer != null)
            {
                Logger.Debug("Seeking during playback - restarting from new position");

                // Stop current playback but maintain playing state
                _wavePlayer.PlaybackStopped -= OnPlaybackStopped;
                _wavePlayer.Stop();
                _wavePlayer.Dispose();
                _wavePlayer = null;
                // Don't set IsPlaying = false - keep it true to maintain playing state
                StopPositionTimer();

                // Start playback from the new position
                try
                {
                    if (_processedProvider == null)
                    {
                        Logger.Error("Cannot seek - processed provider is null");
                        // Only set IsPlaying = false if we can't continue
                        IsPlaying = false;
                        OnPlaybackStateChanged(PlaybackState.Stopped);
                        return;
                    }

                    if (_processedProvider.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat)
                    {
                        Logger.Error($"Cannot seek - processed provider is not in IEEE Float format: {_processedProvider.WaveFormat.Encoding}");
                        // Only set IsPlaying = false if we can't continue
                        IsPlaying = false;
                        OnPlaybackStateChanged(PlaybackState.Stopped);
                        return;
                    }

                    var startProvider = CreateSkippedProvider(_processedProvider, position);

                    // Create new player
                    Logger.Debug("Creating new WaveOutEvent for seek during playback");
                    _wavePlayer = new NAudio.Wave.WaveOutEvent();
                    _wavePlayer.PlaybackStopped += OnPlaybackStopped;

                    var waveProvider = new SampleToWaveProvider(startProvider);
                    Logger.Debug($"WaveProvider format: {waveProvider.WaveFormat}");

                    _wavePlayer.Init(waveProvider);
                    _wavePlayer.Play();

                    // IsPlaying is already true, no need to set it again
                    // Track playback start time and offset
                    // Set start time to now, and offset to the desired position
                    // This ensures CurrentTime = _playbackStartOffset + elapsed starts at the correct position
                    _playbackStartTime = DateTime.Now;
                    _playbackStartOffset = position;
                    _initialPlaybackOffset = position;

                    // Don't fire PlaybackStateChanged event since we're maintaining playing state
                    StartPositionTimer();

                    Logger.Info($"Audio playback restarted from seek position: {position}");
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to restart playback after seek", ex);
                    IsPlaying = false;
                    StopPositionTimer();
                }
            }
            else
            {
                // Not playing, just update the position for when playback starts
                Logger.Debug("Seek called while not playing - position updated for next playback");
            }

            OnPositionChanged(CurrentTime);
            Logger.Debug($"Seek completed. CurrentTime: {CurrentTime}");
        }
    }

    /// <summary>
    /// Build a provider that starts playback at the requested position by skipping samples.
    /// </summary>
    private ISampleProvider CreateSkippedProvider(ISampleProvider source, TimeSpan position)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));

        if (position <= TimeSpan.Zero)
            return source;

        // Clamp to just before the end to avoid end-of-stream skip warnings
        var total = TotalTime;
        if (total > TimeSpan.Zero && position >= total)
        {
            // If position is at or beyond total, don't skip - just return source
            // This prevents trying to skip beyond the end of the stream
            Logger.Debug($"CreateSkippedProvider: position {position} >= total {total}, returning source without skipping");
            return source;
        }

        // Calculate samples to skip, but ensure we don't exceed available samples
        var samplesToSkip = (long)(position.TotalSeconds * source.WaveFormat.SampleRate * source.WaveFormat.Channels);
        
        // Clamp to a reasonable maximum to prevent overflow
        if (samplesToSkip < 0)
            samplesToSkip = 0;
        if (samplesToSkip > int.MaxValue)
            samplesToSkip = int.MaxValue;
            
        Logger.Debug($"CreateSkippedProvider: position={position}, samplesToSkip={samplesToSkip}");
        return new OffsetSampleProvider(source, (int)samplesToSkip);
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        Logger.Warning($"=== OnPlaybackStopped fired! Exception: {e.Exception?.Message ?? "none"} ===");
        
        if (e.Exception != null)
        {
            Logger.Error($"Playback stopped with error: {e.Exception.Message}", e.Exception);
        }
        
        IsPlaying = false;
        OnPlaybackStateChanged(PlaybackState.Stopped);
        StopPositionTimer();
        
        Logger.Debug($"OnPlaybackStopped completed. IsPlaying={IsPlaying}");
    }

    private void StartPositionTimer()
    {
        try
        {
            StopPositionTimer(); // Ensure old timer is stopped
            
            _positionTimer = new System.Timers.Timer(100); // Update every 100ms
            _positionTimer.Elapsed += UpdatePosition;
            _positionTimer.AutoReset = true;
            _positionTimer.Start();
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to start position timer", ex);
        }
    }

    private void StopPositionTimer()
    {
        try
        {
            if (_positionTimer != null)
            {
                _positionTimer.Stop();
                _positionTimer.Elapsed -= UpdatePosition;
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"Error stopping position timer: {ex.Message}");
        }
    }

    private void UpdatePosition(object? sender, System.Timers.ElapsedEventArgs e)
    {
        try
        {
            if (_wavePlayer != null && IsPlaying && _playbackStartTime != DateTime.MinValue)
            {
                // Track position based on elapsed time from playback start
                var elapsed = DateTime.Now - _playbackStartTime;
                CurrentTime = _playbackStartOffset + elapsed;

                // Ensure we don't exceed total time
                if (CurrentTime > TotalTime)
                {
                    CurrentTime = TotalTime;
                }

                OnPositionChanged(CurrentTime);
            }
        }
        catch (Exception ex)
        {
            // Don't let position update errors crash the app
            Logger.Debug($"Error updating position: {ex.Message}");
        }
    }

    protected virtual void OnPlaybackStateChanged(PlaybackState state)
    {
        PlaybackStateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(state));
    }

    protected virtual void OnPositionChanged(TimeSpan position)
    {
        PositionChanged?.Invoke(this, position);
    }

    private void DisposeCurrentStream()
    {
        Stop();
        _wavePlayer?.Dispose();
        _wavePlayer = null;
        _currentStream?.Dispose();
        _currentStream = null;
        _processedProvider = null;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            StopPositionTimer();
            _positionTimer?.Dispose();
            DisposeCurrentStream();
            _disposed = true;
        }
    }
}

/// <summary>
/// Wraps an ISampleProvider to skip a specified number of samples
/// </summary>
internal class OffsetSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _samplesToSkip;
    private int _samplesSkipped = 0;
    private int _readCallCount = 0;
    private int _totalSamplesRead = 0;
    private static int _instanceCount = 0;
    private readonly int _instanceId;

    public OffsetSampleProvider(ISampleProvider source, int samplesToSkip)
    {
        _source = source;
        _samplesToSkip = samplesToSkip;
        WaveFormat = source.WaveFormat;
        _instanceId = ++_instanceCount;
        Logging.Logger.Debug($"OffsetSampleProvider #{_instanceId} created: samplesToSkip={samplesToSkip}, format={source.WaveFormat}");
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        _readCallCount++;

        // Log every 100th call to avoid spam
        if (_readCallCount <= 5 || _readCallCount % 100 == 0)
        {
            Logging.Logger.Debug($"OffsetSampleProvider #{_instanceId} Read #{_readCallCount}: count={count}, skipped={_samplesSkipped}/{_samplesToSkip}, totalRead={_totalSamplesRead}");
        }

        // Skip samples first if we haven't skipped enough yet
        while (_samplesSkipped < _samplesToSkip)
        {
            int remainingToSkip = _samplesToSkip - _samplesSkipped;
            
            // If we're very close to the target (within 1%), just mark as done
            // This prevents trying to skip the exact last sample which can cause warnings
            if (remainingToSkip <= 1 || (_samplesToSkip > 0 && _samplesSkipped >= _samplesToSkip * 0.99))
            {
                _samplesSkipped = _samplesToSkip;
                break;
            }
            
            int samplesToReadNow = Math.Min(remainingToSkip, 4096); // Read in chunks

            // Read and discard these samples
            float[] skipBuffer = new float[samplesToReadNow];
            int actuallyRead = _source.Read(skipBuffer, 0, samplesToReadNow);

            if (actuallyRead == 0)
            {
                // End of stream reached while skipping - this can happen if position calculation is slightly off
                // Just mark as done and continue with playback from current position
                Logging.Logger.Debug($"OffsetSampleProvider #{_instanceId}: End of stream while skipping (skipped={_samplesSkipped}/{_samplesToSkip}). Starting from current position.");
                _samplesSkipped = _samplesToSkip; // Mark as done to avoid infinite loop
                break;
            }

            _samplesSkipped += actuallyRead;
        }

        // Now read normally into the output buffer
        int samplesRead = _source.Read(buffer, offset, count);
        _totalSamplesRead += samplesRead;

        if (samplesRead == 0 && _readCallCount <= 10)
        {
            Logging.Logger.Warning($"OffsetSampleProvider #{_instanceId}: Source returned 0 samples on Read #{_readCallCount}");
        }

        return samplesRead;
    }
}

public enum PlaybackState
{
    Stopped,
    Playing,
    Paused
}

public class PlaybackStateChangedEventArgs : EventArgs
{
    public PlaybackState State { get; }

    public PlaybackStateChangedEventArgs(PlaybackState state)
    {
        State = state;
    }
}

public class ExportProgressEventArgs : EventArgs
{
    public int ProgressPercent { get; }
    public string Message { get; }

    public ExportProgressEventArgs(int progressPercent, string message)
    {
        ProgressPercent = progressPercent;
        Message = message;
    }
}

