using Remixer.Core.Models;
using System;

namespace Remixer.Core.Presets;

public class Preset
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Custom";
    public string Description { get; set; } = string.Empty;
    public AudioSettings Settings { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime ModifiedAt { get; set; } = DateTime.Now;
    public string Version { get; set; } = "1.0";
}

