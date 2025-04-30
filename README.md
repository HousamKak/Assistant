# Voice Assistant

A Windows-based voice assistant with wake word detection, offline speech recognition, and visual feedback.

![Voice Assistant UI](docs/images/voice-assistant-ui.png)

## Overview

Voice Assistant is a C# application that provides a hands-free interface through voice commands. It consists of a Windows Service that runs in the background and a WPF UI application that provides visual feedback. The assistant is always listening for a wake word ("wake up"), after which it captures and processes voice commands.

## Features

- **Wake Word Detection**: Uses Picovoice Porcupine for efficient, accurate wake word recognition
- **Offline Speech Recognition**: Implements Whisper.NET for local speech-to-text without internet
- **Responsive UI**: Visual feedback with animations that change based on state
- **Always Running**: Windows Service ensures continuous operation
- **System Tray Integration**: Minimizes to tray for unobtrusive operation
- **Extensible Commands**: Easily add new voice commands and actions
- **Resource Efficient**: Optimized for minimal CPU/memory impact

## Requirements

- Windows 10 or later
- .NET 7.0 SDK or later
- Visual Studio 2022 (recommended) or Visual Studio Code
- Administrator rights (for service installation)
- Microphone

## Quick Start

### Installation

1. Clone the repository:
```bash
git clone https://github.com/yourusername/voice-assistant.git
cd voice-assistant
```

2. Build the solution:
```bash
dotnet build
```

3. Install the Windows Service (as administrator):
```bash
cd VoiceAssistant.Service/bin/Debug/net7.0
VoiceAssistant.Service.exe install
VoiceAssistant.Service.exe start
```

4. Launch the UI application:
```bash
cd VoiceAssistant.UI/bin/Debug/net7.0
VoiceAssistant.UI.exe
```

### First Use

1. When you first start the application, it will download the required speech recognition model (if not already present)
2. The assistant will appear in the bottom right corner of your screen
3. Say "wake up" to activate the assistant
4. After hearing a confirmation sound, speak your command
5. The recognized command and response will appear in a bubble

## Voice Commands

The following commands are supported by default:

| Command | Description |
|---------|-------------|
| "project mode" | Activates project mode |
| "help" | Shows a list of available commands |
| "time" | Tells the current time |
| "date" | Tells the current date |

## Configuration

### Wake Word Sensitivity

Adjust the wake word detection sensitivity in the settings panel (accessible via the system tray icon).

### Starting with Windows

Enable "Start with Windows" in the settings panel to launch the UI application at system startup.

### Custom Commands

Add custom commands by modifying the `CommandProcessorService.cs` file and rebuilding the application.

## Project Structure

```
VoiceAssistant/
├── VoiceAssistant.Core/          # Core functionality shared between projects
│   ├── Interfaces/               # Interfaces for all services
│   ├── Models/                   # Data models
│   └── Services/                 # Implementation of core services
├── VoiceAssistant.Service/       # Windows Service for background operation
│   └── Services/                 # Service-specific implementations
└── VoiceAssistant.UI/            # WPF User Interface
    ├── Resources/                # Icons and other resources
    └── Services/                 # UI-specific service implementations
```

## Troubleshooting

### Service Won't Start

- Ensure you're running the command prompt as Administrator
- Check Windows Event Viewer for error details
- Verify the wake word model file exists in `Keywords/wake_up.ppn`

### Wake Word Not Detected

- Increase the sensitivity in settings
- Check if your microphone is correctly configured as the default recording device
- Try speaking louder or closer to the microphone

### High CPU Usage

- Use a smaller Whisper model (tiny instead of base) by modifying the model type in ServiceManager.cs
- Adjust the maximum command duration in the configuration

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add some amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Acknowledgments

- [Picovoice Porcupine](https://picovoice.ai/platform/porcupine/) for wake word detection
- [Whisper.NET](https://github.com/sandrohanea/whisper.net) for offline speech recognition
- [NAudio](https://github.com/naudio/NAudio) for audio processing
- [MaterialDesignThemes](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit) for UI components
- [Topshelf](https://github.com/Topshelf/Topshelf) for Windows Service functionality