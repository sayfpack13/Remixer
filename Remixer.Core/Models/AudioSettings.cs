using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Remixer.Core.Models;

public class AudioSettings : INotifyPropertyChanged
{
    private double _tempo = 1.0;
    private double _pitch = 0.0;
    private double _volume = 1.0;
    private bool _isAISet = false;
    
    public double Tempo
    {
        get => _tempo;
        set
        {
            if (_tempo != value)
            {
                _tempo = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double Pitch
    {
        get => _pitch;
        set
        {
            if (_pitch != value)
            {
                _pitch = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double Volume
    {
        get => _volume;
        set
        {
            if (_volume != value)
            {
                _volume = value;
                OnPropertyChanged();
            }
        }
    }
    
    public ReverbSettings Reverb { get; set; } = new();
    public EchoSettings Echo { get; set; } = new();
    public FilterSettings Filter { get; set; } = new();
    
    public bool IsAISet
    {
        get => _isAISet;
        set
        {
            if (_isAISet != value)
            {
                _isAISet = value;
                OnPropertyChanged();
            }
        }
    }
    
    public event PropertyChangedEventHandler? PropertyChanged;
    
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class ReverbSettings : INotifyPropertyChanged
{
    private bool _enabled = false;
    private double _roomSize = 0.5;
    private double _damping = 0.5;
    private double _wetLevel = 0.3;
    
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled != value)
            {
                _enabled = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double RoomSize
    {
        get => _roomSize;
        set
        {
            if (_roomSize != value)
            {
                _roomSize = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double Damping
    {
        get => _damping;
        set
        {
            if (_damping != value)
            {
                _damping = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double WetLevel
    {
        get => _wetLevel;
        set
        {
            if (_wetLevel != value)
            {
                _wetLevel = value;
                OnPropertyChanged();
            }
        }
    }
    
    public event PropertyChangedEventHandler? PropertyChanged;
    
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class EchoSettings : INotifyPropertyChanged
{
    private bool _enabled = false;
    private double _delay = 0.2;
    private double _feedback = 0.3;
    private double _wetLevel = 0.3;
    
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled != value)
            {
                _enabled = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double Delay
    {
        get => _delay;
        set
        {
            if (_delay != value)
            {
                _delay = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double Feedback
    {
        get => _feedback;
        set
        {
            if (_feedback != value)
            {
                _feedback = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double WetLevel
    {
        get => _wetLevel;
        set
        {
            if (_wetLevel != value)
            {
                _wetLevel = value;
                OnPropertyChanged();
            }
        }
    }
    
    public event PropertyChangedEventHandler? PropertyChanged;
    
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class FilterSettings : INotifyPropertyChanged
{
    private bool _enabled = false;
    private double _lowCut = 20.0;
    private double _highCut = 20000.0;
    private double _lowGain = 0.0;
    private double _midGain = 0.0;
    private double _highGain = 0.0;
    
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled != value)
            {
                _enabled = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double LowCut
    {
        get => _lowCut;
        set
        {
            if (_lowCut != value)
            {
                _lowCut = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double HighCut
    {
        get => _highCut;
        set
        {
            if (_highCut != value)
            {
                _highCut = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double LowGain
    {
        get => _lowGain;
        set
        {
            if (_lowGain != value)
            {
                _lowGain = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double MidGain
    {
        get => _midGain;
        set
        {
            if (_midGain != value)
            {
                _midGain = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double HighGain
    {
        get => _highGain;
        set
        {
            if (_highGain != value)
            {
                _highGain = value;
                OnPropertyChanged();
            }
        }
    }
    
    public event PropertyChangedEventHandler? PropertyChanged;
    
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
