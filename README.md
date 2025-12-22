# Audio Remixer

A Windows desktop application built with .NET and WPF that enables users to remix audio files with comprehensive customization options, preset management, and AI-powered automatic remixing.

## Features

- **Audio Processing**: Load and process audio files in multiple formats (MP3, WAV, FLAC, OGG, M4A, AAC)
- **Audio Effects**:
  - Tempo adjustment (0.5x to 2.0x)
  - Pitch shifting (-12 to +12 semitones)
  - Reverb with customizable room size, damping, and wet level
  - Echo/Delay effects
  - Filter/EQ with low/high cut and gain controls
- **Preset System**: Save and load effect configurations as presets
- **AI-Powered Remixing**: Automatically analyze audio and apply optimal remix parameters using AI
- **Export**: Export remixed audio to WAV format

## Requirements

- .NET 8.0 Runtime
- Windows 10/11

## Setup

1. Build the solution:
   ```bash
   dotnet build Remixer.sln
   ```

2. (Optional) Configure AI Service:
   - Copy `appsettings.json.example` to `%AppData%\Remixer\appsettings.json`
   - Add your AI API endpoint and key:
     ```json
     {
       "AIEndpoint": "https://api.openai.com/v1/chat/completions",
       "AIKey": "your-api-key-here"
     }
     ```

## Usage

1. **Load Audio**: Click "Load Audio" button and select an audio file
2. **Apply Effects**: Adjust tempo, pitch, reverb, echo, and filter settings using the sliders
3. **Use Presets**: Select a preset from the list and click "Apply Preset"
4. **AI Remix**: Click "AI Remix" to automatically analyze and apply optimal remix parameters
5. **Export**: Click "Export" to save your remixed audio

## AI Remixing

The AI remix feature:
- Analyzes audio characteristics (BPM, energy, spectral features)
- Sends features to AI API (not raw audio, preserving privacy)
- Receives optimal remix parameter suggestions
- Automatically applies the parameters
- Allows you to override any AI-applied settings

## Project Structure

- `Remixer.Core/`: Core audio processing, effects, AI integration, and preset management
- `Remixer.WPF/`: WPF user interface with ViewModels

## Dependencies

- **NAudio**: Audio processing library
- **CommunityToolkit.Mvvm**: MVVM helpers
- **Microsoft.Extensions.Configuration**: Configuration management

## Notes

- Audio processing uses CPU; heavy operations run in background threads
- AI analysis is async to avoid UI blocking
- Presets are stored in `%AppData%\Remixer\Presets\`
- Configuration is stored in `%AppData%\Remixer\appsettings.json`

## License

This project is provided as-is for educational and personal use.

