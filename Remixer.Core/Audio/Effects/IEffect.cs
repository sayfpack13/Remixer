using NAudio.Wave;

namespace Remixer.Core.Audio.Effects;

public interface IEffect
{
    ISampleProvider Apply(ISampleProvider input);
    bool IsEnabled { get; set; }
}

