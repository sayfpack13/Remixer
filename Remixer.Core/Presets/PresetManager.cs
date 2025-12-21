using Remixer.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Remixer.Core.Presets;

public class PresetManager
{
    private readonly List<Preset> _presets = new();

    public PresetManager()
    {
        CreateDefaultPresets();
    }

    public IReadOnlyList<Preset> Presets => _presets.AsReadOnly();

    public void SavePreset(Preset preset)
    {
        preset.ModifiedAt = DateTime.Now;
        
        // Check if preset with same name exists
        var existing = _presets.FirstOrDefault(p => 
            p.Name.Equals(preset.Name, StringComparison.OrdinalIgnoreCase));
        
        if (existing != null)
        {
            // Update existing preset
            existing.Settings = preset.Settings;
            existing.Description = preset.Description;
            existing.Category = preset.Category;
            existing.ModifiedAt = preset.ModifiedAt;
        }
        else
        {
            // Add new preset
            _presets.Add(preset);
        }
    }

    public Preset? GetPreset(string presetId)
    {
        return _presets.FirstOrDefault(p => p.Id == presetId);
    }

    public Preset? GetPresetByName(string name)
    {
        return _presets.FirstOrDefault(p => 
            p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public List<Preset> GetPresetsByCategory(string category)
    {
        return _presets.Where(p => p.Category == category).ToList();
    }

    public void DeletePreset(string presetId)
    {
        var preset = _presets.FirstOrDefault(p => p.Id == presetId);
        if (preset != null)
            _presets.Remove(preset);
    }

    public void DeletePresetByName(string name)
    {
        var preset = _presets.FirstOrDefault(p => 
            p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (preset != null)
            _presets.Remove(preset);
    }

    public bool PresetNameExists(string name)
    {
        return _presets.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private void CreateDefaultPresets()
    {
        // Dance preset
        var dancePreset = new Preset
        {
            Name = "Dance",
            Category = "Genre",
            Description = "High energy dance remix",
            Settings = new AudioSettings
            {
                Tempo = 1.2,
                Pitch = 0.0,
                Reverb = new ReverbSettings { Enabled = true, RoomSize = 0.4, WetLevel = 0.2 },
                Filter = new FilterSettings { Enabled = true, LowGain = 3.0, HighGain = 2.0 }
            }
        };
        SavePreset(dancePreset);

        // Chill preset
        var chillPreset = new Preset
        {
            Name = "Chill",
            Category = "Genre",
            Description = "Relaxed, ambient remix",
            Settings = new AudioSettings
            {
                Tempo = 0.9,
                Pitch = -2.0,
                Reverb = new ReverbSettings { Enabled = true, RoomSize = 0.7, WetLevel = 0.4 },
                Echo = new EchoSettings { Enabled = true, Delay = 0.3, Feedback = 0.2 }
            }
        };
        SavePreset(chillPreset);

        // Energetic preset
        var energeticPreset = new Preset
        {
            Name = "Energetic",
            Category = "Genre",
            Description = "Fast-paced, high-energy remix",
            Settings = new AudioSettings
            {
                Tempo = 1.3,
                Pitch = 1.0,
                Filter = new FilterSettings { Enabled = true, LowGain = 4.0, MidGain = 2.0, HighGain = 3.0 }
            }
        };
        SavePreset(energeticPreset);
    }
}
