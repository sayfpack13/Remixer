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
    public ChorusSettings Chorus { get; set; } = new();
    public FlangerSettings Flanger { get; set; } = new();
    public DistortionSettings Distortion { get; set; } = new();
    public CompressorSettings Compressor { get; set; } = new();
    public PhaserSettings Phaser { get; set; } = new();
    public TremoloSettings Tremolo { get; set; } = new();
    public BitcrusherSettings Bitcrusher { get; set; } = new();
    public VibratoSettings Vibrato { get; set; } = new();
    public SaturationSettings Saturation { get; set; } = new();
    public GateSettings Gate { get; set; } = new();
    
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

public class ChorusSettings : INotifyPropertyChanged
{
    private bool _enabled = false;
    private double _rate = 1.0;
    private double _depth = 0.5;
    private double _mix = 0.5;
    
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
    
    public double Rate
    {
        get => _rate;
        set
        {
            if (_rate != value)
            {
                _rate = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double Depth
    {
        get => _depth;
        set
        {
            if (_depth != value)
            {
                _depth = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double Mix
    {
        get => _mix;
        set
        {
            if (_mix != value)
            {
                _mix = value;
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

public class FlangerSettings : INotifyPropertyChanged
{
    private bool _enabled = false;
    private double _delay = 0.005;
    private double _rate = 0.5;
    private double _depth = 0.8;
    private double _feedback = 0.3;
    private double _mix = 0.5;
    
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
    
    public double Rate
    {
        get => _rate;
        set
        {
            if (_rate != value)
            {
                _rate = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double Depth
    {
        get => _depth;
        set
        {
            if (_depth != value)
            {
                _depth = value;
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
    
    public double Mix
    {
        get => _mix;
        set
        {
            if (_mix != value)
            {
                _mix = value;
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

public class DistortionSettings : INotifyPropertyChanged
{
    private bool _enabled = false;
    private double _drive = 0.5;
    private double _tone = 0.5;
    private double _mix = 0.5;
    
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
    
    public double Drive
    {
        get => _drive;
        set
        {
            if (_drive != value)
            {
                _drive = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double Tone
    {
        get => _tone;
        set
        {
            if (_tone != value)
            {
                _tone = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double Mix
    {
        get => _mix;
        set
        {
            if (_mix != value)
            {
                _mix = value;
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

public class CompressorSettings : INotifyPropertyChanged
{
    private bool _enabled = false;
    private double _threshold = -12.0;
    private double _ratio = 4.0;
    private double _attack = 10.0;
    private double _release = 100.0;
    private double _makeupGain = 0.0;
    
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
    
    public double Threshold
    {
        get => _threshold;
        set
        {
            if (_threshold != value)
            {
                _threshold = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double Ratio
    {
        get => _ratio;
        set
        {
            if (_ratio != value)
            {
                _ratio = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double Attack
    {
        get => _attack;
        set
        {
            if (_attack != value)
            {
                _attack = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double Release
    {
        get => _release;
        set
        {
            if (_release != value)
            {
                _release = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double MakeupGain
    {
        get => _makeupGain;
        set
        {
            if (_makeupGain != value)
            {
                _makeupGain = value;
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

public class PhaserSettings : INotifyPropertyChanged
{
    private bool _enabled = false;
    private double _rate = 0.5;
    private double _depth = 0.8;
    private double _feedback = 0.3;
    private double _mix = 0.5;
    
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
    
    public double Rate
    {
        get => _rate;
        set
        {
            if (_rate != value)
            {
                _rate = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double Depth
    {
        get => _depth;
        set
        {
            if (_depth != value)
            {
                _depth = value;
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
    
    public double Mix
    {
        get => _mix;
        set
        {
            if (_mix != value)
            {
                _mix = value;
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

public class TremoloSettings : INotifyPropertyChanged
{
    private bool _enabled = false;
    private double _rate = 3.0;
    private double _depth = 0.5;
    
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
    
    public double Rate
    {
        get => _rate;
        set
        {
            if (_rate != value)
            {
                _rate = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double Depth
    {
        get => _depth;
        set
        {
            if (_depth != value)
            {
                _depth = value;
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

public class BitcrusherSettings : INotifyPropertyChanged
{
    private bool _enabled = false;
    private double _bitDepth = 8.0;
    private double _downsample = 2.0;
    private double _mix = 0.5;
    
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
    
    public double BitDepth
    {
        get => _bitDepth;
        set
        {
            if (_bitDepth != value)
            {
                _bitDepth = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double Downsample
    {
        get => _downsample;
        set
        {
            if (_downsample != value)
            {
                _downsample = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double Mix
    {
        get => _mix;
        set
        {
            if (_mix != value)
            {
                _mix = value;
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

public class VibratoSettings : INotifyPropertyChanged
{
    private bool _enabled = false;
    private double _rate = 5.0;
    private double _depth = 0.1;
    private double _mix = 1.0;
    
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
    
    public double Rate
    {
        get => _rate;
        set
        {
            if (_rate != value)
            {
                _rate = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double Depth
    {
        get => _depth;
        set
        {
            if (_depth != value)
            {
                _depth = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double Mix
    {
        get => _mix;
        set
        {
            if (_mix != value)
            {
                _mix = value;
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

public class SaturationSettings : INotifyPropertyChanged
{
    private bool _enabled = false;
    private double _drive = 0.5;
    private double _tone = 0.5;
    private double _mix = 0.5;
    
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
    
    public double Drive
    {
        get => _drive;
        set
        {
            if (_drive != value)
            {
                _drive = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double Tone
    {
        get => _tone;
        set
        {
            if (_tone != value)
            {
                _tone = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double Mix
    {
        get => _mix;
        set
        {
            if (_mix != value)
            {
                _mix = value;
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

public class GateSettings : INotifyPropertyChanged
{
    private bool _enabled = false;
    private double _threshold = 0.1;
    private double _ratio = 10.0;
    private double _attack = 1.0;
    private double _release = 50.0;
    private double _floor = 0.0;
    
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
    
    public double Threshold
    {
        get => _threshold;
        set
        {
            if (_threshold != value)
            {
                _threshold = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double Ratio
    {
        get => _ratio;
        set
        {
            if (_ratio != value)
            {
                _ratio = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double Attack
    {
        get => _attack;
        set
        {
            if (_attack != value)
            {
                _attack = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double Release
    {
        get => _release;
        set
        {
            if (_release != value)
            {
                _release = value;
                OnPropertyChanged();
            }
        }
    }
    
    public double Floor
    {
        get => _floor;
        set
        {
            if (_floor != value)
            {
                _floor = value;
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
