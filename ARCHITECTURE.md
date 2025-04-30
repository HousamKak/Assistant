# Voice Assistant Architecture

This document provides a detailed overview of the Voice Assistant architecture, explaining the system design, component interactions, and key technical decisions.

## System Overview

The Voice Assistant is designed as a hybrid application consisting of two main components:

1. **Windows Service (VoiceAssistant.Service)**: 
   - Runs continuously in the background
   - Handles audio capture, wake word detection, and speech recognition
   - Processes voice commands
   - Operates even when no user is logged in

2. **WPF UI Application (VoiceAssistant.UI)**:
   - Provides visual feedback to the user
   - Displays state changes and command results
   - Offers configuration options via settings panel
   - Integrates with the Windows system tray

These components communicate via Named Pipes IPC (Inter-Process Communication) and share core functionality through a common library.

![Architecture Diagram](docs/images/architecture-diagram.png)

## Core Components (VoiceAssistant.Core)

The shared library contains these main components:

### Interfaces

- **IAudioCaptureService**: Audio input handling
- **IWakeWordService**: Wake word detection
- **ISpeechRecognitionService**: Speech-to-text conversion
- **ICommandProcessorService**: Command parsing and execution
- **IAssistantService**: Main orchestration service
- **IIpcService**: Inter-process communication

### Services

- **AudioCaptureService**: Uses NAudio to capture microphone input
- **PorcupineWakeWordService**: Implements Picovoice Porcupine for wake word detection
- **WhisperSpeechRecognitionService**: Uses Whisper.NET for offline speech recognition
- **CommandProcessorService**: Parses and executes voice commands
- **AssistantService**: Coordinates the flow between services
- **NamedPipeIpcService**: Handles communication between service and UI

### Models

- **AssistantState**: Tracks operational state (idle, listening, processing, etc.)
- **Command**: Represents a parsed voice command
- **CommandResult**: Contains the result of a processed command
- **IpcMessage**: Messages passed between service and UI
- **Configuration**: Application settings
- **AudioData**: Audio capture data structures
- **CommandHistory**: Record of processed commands

## Windows Service Architecture

### ServiceManager

The ServiceManager class is the central coordinator of the Windows Service:

1. Configures dependency injection
2. Initializes and starts all services
3. Handles IPC messages from the UI
4. Manages the service lifecycle

### Operational Flow

1. **Service Startup**:
   - Download/verify model files if needed
   - Initialize IPC service as server
   - Start audio capture and wake word detection

2. **Wake Word Detection**:
   - Continuously process audio through Porcupine
   - When "wake up" is detected, transition to listening state

3. **Command Processing**:
   - Capture 5 seconds of audio after wake word
   - Transcribe speech with Whisper.NET
   - Pass text to CommandProcessorService
   - Send results to UI via IPC

4. **Service Shutdown**:
   - Stop all services cleanly
   - Release resources
   - Dispose service provider

## UI Architecture

### MainWindow

The main UI window implements:

1. **Visual Feedback**:
   - Pulsing ellipse that changes color based on state
   - Status icon that changes based on state
   - Command bubble showing recognized text

2. **System Tray Integration**:
   - Minimize to tray icon
   - Context menu with options
   - Notification support

### AssistantUIService

This service:

1. Handles IPC communication with the Windows Service
2. Updates UI based on state changes and command results
3. Manages settings persistence
4. Provides commands to the service (e.g., manual trigger listening)

## IPC Communication

The IPC communication uses named pipes with the following message types:

- **StateChange**: Updates about assistant state changes
- **Command**: Results of processed commands
- **ServiceStatus**: Service status queries and responses
- **UiRequest**: Requests from UI to service (e.g., trigger listening)
- **Configuration**: Configuration changes

## Error Handling and Resilience

The system implements several resilience strategies:

1. **Exception Handling**:
   - Custom exception hierarchy for different component failures
   - Graceful degradation when non-critical components fail

2. **Service Recovery**:
   - Windows Service automatic restart
   - Resource cleanup during failures

3. **Component Independence**:
   - Services can operate independently
   - UI functions even if service is not running

## Technical Decisions

### Why Offline Speech Recognition?

Whisper.NET was chosen for speech recognition because:
- Privacy: No audio sent to external services
- Reliability: Works without internet connection
- Performance: Low latency for command processing
- Customizability: Can be fine-tuned for specific vocabularies

### Why a Windows Service?

A Windows Service was chosen as the background component because:
- Runs without active user sessions
- Starts automatically with Windows
- Restarts after crashes
- Proper lifecycle management

### Why WPF for UI?

WPF was selected for the UI because:
- Rich animation capabilities
- Transparency and visual effects support
- System tray integration
- Mature technology with strong tooling

### Why Named Pipes for IPC?

Named pipes were chosen for IPC because:
- Efficient local communication
- Built-in security
- Support for bidirectional communication
- Native .NET support

## Performance Considerations

### Audio Processing

- 16kHz mono audio capture for optimal wake word detection
- Buffering system to handle audio efficiently
- Separate thread for audio processing

### Memory Management

- Proper disposal of native resources
- Streaming approach to audio processing
- Minimal copying of audio data

### CPU Usage

- Wake word detection optimized for low CPU usage
- Speech recognition only triggered when needed
- Configurable model sizes for different performance targets

## Extension Points

The system is designed to be extensible in several ways:

1. **New Commands**:
   - Add new command handlers to CommandProcessorService
   - Define trigger phrases and actions

2. **Alternative Models**:
   - Switch wake word models for different trigger phrases
   - Use different Whisper model sizes for performance/accuracy trade-offs

3. **Additional Services**:
   - Implement new services via the interface abstractions
   - Add to dependency injection in ServiceManager

## Future Architecture Evolution

Planned architectural improvements:

1. **Plugin System**:
   - Dynamic loading of command plugins
   - Third-party extension support

2. **Distributed Architecture**:
   - Optional cloud services integration
   - Multi-device synchronization

3. **Advanced UI**:
   - 3D avatar visualization
   - Emotion recognition and response
   - Multi-modal interaction (voice + gesture)