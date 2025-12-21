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
    public TimeSpan TotalTime => _currentStream?.TotalTime ?? TimeSpan.Zero;
    public WaveFormat? CurrentFormat => _currentStream?.WaveFormat;
    public string? CurrentFilePath => _currentFilePath;
    
    public event EventHandler<PlaybackStateChangedEventArgs>? PlaybackStateChanged;
    public event EventHandler<TimeSpan>? PositionChanged;

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
            ProcessAudioInternal(preservePosition);
        }
        finally
        {
            lock (_audioLock)
            {
                _isProcessing = false;
            }
        }
    }
    
    /// <summary>
    /// Internal method to process audio - can be called from within a lock context
    /// </summary>
    private void ProcessAudioInternal(TimeSpan preservePosition)
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

        // If we need to preserve position, wrap the provider to skip samples
        if (preservePosition > TimeSpan.Zero)
        {
            var sampleRate = provider.WaveFormat.SampleRate;
            var channels = provider.WaveFormat.Channels;
            
            // Account for tempo when calculating samples to skip
            // The actual audio position in samples depends on the tempo
            var tempo = _settings.Tempo;
            var adjustedTime = preservePosition.TotalSeconds;
            
            // Clamp to valid range (0 to totalTime)
            adjustedTime = Math.Max(0, Math.Min(adjustedTime, totalTime.TotalSeconds - 0.1));
            
            var samplesToSkip = (int)(adjustedTime * sampleRate * channels);
            samplesToSkip = Math.Max(0, samplesToSkip); // Ensure non-negative
            
            Logger.Debug($"Wrapping provider to skip {samplesToSkip} samples (position: {preservePosition}, adjustedTime: {adjustedTime}s)");
            provider = new OffsetSampleProvider(provider, samplesToSkip);
            CurrentTime = preservePosition;
            // Store the initial offset for position tracking
            _initialPlaybackOffset = preservePosition;
        }
        else
        {
            CurrentTime = TimeSpan.Zero;
            _initialPlaybackOffset = TimeSpan.Zero;
        }
        
        _processedProvider = provider;
        
        Logger.Info($"ProcessAudioInternal #{_processAudioCallCount} completed. Final format: {_processedProvider.WaveFormat}");
        Logger.Debug($"Stream position after processing: {_currentStream.Position}");
        Logger.Debug($"CurrentTime: {CurrentTime}, InitialOffset: {_initialPlaybackOffset}");
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

    public void Export(string outputPath, int sampleRate = 44100, int channels = 2, int bitsPerSample = 16)
    {
        var sw = Stopwatch.StartNew();
        Logger.Info($"Exporting audio to: {outputPath} (SR:{sampleRate}, Ch:{channels}, Bits:{bitsPerSample})");
        
        try
        {
            if (_processedProvider == null)
                throw new InvalidOperationException("No audio loaded");

            // Convert to target format
            var format = new WaveFormat(sampleRate, bitsPerSample, channels);
            var waveProvider = new SampleToWaveProvider(_processedProvider);
            
            // Resample if needed
            if (waveProvider.WaveFormat.SampleRate != sampleRate || waveProvider.WaveFormat.Channels != channels)
            {
                Logger.Debug($"Resampling required: {waveProvider.WaveFormat.SampleRate}Hz -> {sampleRate}Hz");
                var resampler = new MediaFoundationResampler(waveProvider, format);
                AudioFileHandler.SaveAudioFile(resampler, outputPath, sampleRate, channels, bitsPerSample);
                resampler.Dispose();
            }
            else
            {
                AudioFileHandler.SaveAudioFile(waveProvider, outputPath, sampleRate, channels, bitsPerSample);
            }
            
            sw.Stop();
            Logger.LogPerformance($"Export audio: {System.IO.Path.GetFileName(outputPath)}", sw.ElapsedMilliseconds);
            Logger.Info("Audio exported successfully");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to export audio to: {outputPath}", ex);
            throw;
        }
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
                ProcessAudioInternal(CurrentTime);

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

                // Initialize with processed audio
                // SampleToWaveProvider requires the input to already be in float format
                Logger.Debug("Creating SampleToWaveProvider from processed provider");
                var waveProvider = new SampleToWaveProvider(_processedProvider);
                Logger.Debug($"WaveProvider format: {waveProvider.WaveFormat}");
                
                Logger.Debug("Initializing WaveOutEvent");
                _wavePlayer.Init(waveProvider);
                
                Logger.Debug("Starting playback");
                _wavePlayer.Play();

                IsPlaying = true;
                
                // Track playback start time and offset for position tracking
                _playbackStartTime = DateTime.Now;
                _playbackStartOffset = CurrentTime;
                
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
            
            // Update current time - this will be used when Play() is called
            CurrentTime = position;
            _initialPlaybackOffset = position;
            _playbackStartOffset = position;
            
            // Clear the processed provider so it will be recreated with the new position
            _processedProvider = null;
            
            OnPositionChanged(CurrentTime);
            Logger.Debug($"Seek completed. CurrentTime: {CurrentTime}");
        }
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
            if (_wavePlayer != null && IsPlaying)
            {
                // If we have an initial offset (from OffsetSampleProvider), track position based on elapsed time
                if (_initialPlaybackOffset > TimeSpan.Zero)
                {
                    var elapsed = DateTime.Now - _playbackStartTime;
                    CurrentTime = _playbackStartOffset + elapsed;
                }
                else if (_currentStream != null)
                {
                    // Calculate position based on stream position and sample rate
                    var bytesPerSecond = _currentStream.WaveFormat.AverageBytesPerSecond;
                    if (bytesPerSecond > 0)
                    {
                        CurrentTime = TimeSpan.FromSeconds((double)_currentStream.Position / bytesPerSecond);
                    }
                }
                
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
            int samplesToReadNow = Math.Min(remainingToSkip, 4096); // Read in chunks
            
            // Read and discard these samples
            float[] skipBuffer = new float[samplesToReadNow];
            int actuallyRead = _source.Read(skipBuffer, 0, samplesToReadNow);
            
            if (actuallyRead == 0)
            {
                // End of stream reached while skipping
                Logging.Logger.Warning($"OffsetSampleProvider #{_instanceId}: End of stream while skipping! skipped={_samplesSkipped}/{_samplesToSkip}");
                return 0;
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

