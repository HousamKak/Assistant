// VoiceAssistant.Core/Models/Configuration.cs
using System;
using System.Collections.Generic;

namespace VoiceAssistant.Core.Models
{
    /// <summary>
    /// Represents the configuration settings for the voice assistant.
    /// </summary>
    public class Configuration
    {
        /// <summary>
        /// Gets or sets the wake word detection settings.
        /// </summary>
        public WakeWordSettings WakeWord { get; set; }
        
        /// <summary>
        /// Gets or sets the speech recognition settings.
        /// </summary>
        public SpeechRecognitionSettings SpeechRecognition { get; set; }
        
        /// <summary>
        /// Gets or sets the audio capture settings.
        /// </summary>
        public AudioCaptureSettings AudioCapture { get; set; }
        
        /// <summary>
        /// Gets or sets the UI settings.
        /// </summary>
        public UiSettings Ui { get; set; }
        
        /// <summary>
        /// Gets or sets the command settings.
        /// </summary>
        public CommandSettings Commands { get; set; }

        /// <summary>
        /// Gets or sets the text-to-speech settings.
        /// </summary>
        public TextToSpeechSettings TextToSpeech { get; set; }
        
        /// <summary>
        /// Initializes a new instance of the Configuration class with default settings.
        /// </summary>
        public Configuration()
        {
            WakeWord = new WakeWordSettings();
            SpeechRecognition = new SpeechRecognitionSettings();
            AudioCapture = new AudioCaptureSettings();
            Ui = new UiSettings();
            Commands = new CommandSettings();
            TextToSpeech = new TextToSpeechSettings();
        }
    }

    /// <summary>
    /// Settings for wake word detection.
    /// </summary>
    public class WakeWordSettings
    {
        /// <summary>
        /// Gets or sets the path to the wake word model file.
        /// </summary>
        public string ModelPath { get; set; } = "Keywords/wake_up.ppn";
        
        /// <summary>
        /// Gets or sets the sensitivity for wake word detection (0.0 to 1.0).
        /// </summary>
        public float Sensitivity { get; set; } = 0.7f;
        
        /// <summary>
        /// Gets or sets the mapping of keyword indices to names.
        /// </summary>
        public Dictionary<int, string> KeywordMapping { get; set; } = new Dictionary<int, string> { { 0, "wake up" } };

        /// <summary>
        /// Gets or sets the response phrase spoken when the wake word is detected.
        /// </summary>
        public string ResponsePhrase { get; set; } = "";
    }

    /// <summary>
    /// Settings for speech recognition.
    /// </summary>
    public class SpeechRecognitionSettings
    {
        /// <summary>
        /// Gets or sets the path to the speech recognition model file.
        /// </summary>
        public string ModelPath { get; set; } = "Models/ggml-base.bin";
        
        /// <summary>
        /// Gets or sets the type of model to use.
        /// </summary>
        public string ModelType { get; set; } = "Base";
        
        /// <summary>
        /// Gets or sets the language to use for recognition.
        /// </summary>
        public string Language { get; set; } = "en";
        
        /// <summary>
        /// Gets or sets the maximum duration of audio to capture for a command (in seconds).
        /// </summary>
        public int MaxCommandDuration { get; set; } = 5;
    }

    /// <summary>
    /// Settings for audio capture.
    /// </summary>
    public class AudioCaptureSettings
    {
        /// <summary>
        /// Gets or sets the sample rate for audio capture.
        /// </summary>
        public int SampleRate { get; set; } = 16000;
        
        /// <summary>
        /// Gets or sets the number of channels for audio capture.
        /// </summary>
        public int Channels { get; set; } = 1;
        
        /// <summary>
        /// Gets or sets the device number to use for audio capture.
        /// </summary>
        public int DeviceNumber { get; set; } = 0;
        
        /// <summary>
        /// Gets or sets the buffer size in milliseconds for audio capture.
        /// </summary>
        public int BufferMilliseconds { get; set; } = 50;
    }

    /// <summary>
    /// Settings for the user interface.
    /// </summary>
    public class UiSettings
    {
        /// <summary>
        /// Gets or sets a value indicating whether to minimize to tray when closing.
        /// </summary>
        public bool MinimizeToTray { get; set; } = true;
        
        /// <summary>
        /// Gets or sets a value indicating whether to enable window transparency.
        /// </summary>
        public bool EnableTransparency { get; set; } = true;
        
        /// <summary>
        /// Gets or sets the display mode.
        /// </summary>
        public string DisplayMode { get; set; } = "AlwaysVisible";
        
        /// <summary>
        /// Gets or sets a value indicating whether to start with Windows.
        /// </summary>
        public bool StartWithWindows { get; set; } = false;
    }

    /// <summary>
    /// Settings for text-to-speech.
    /// </summary>
    public class TextToSpeechSettings
    {
        /// <summary>
        /// Gets or sets a value indicating whether text-to-speech is enabled.
        /// </summary>
        public bool Enabled { get; set; } = true;
        
        /// <summary>
        /// Gets or sets the name of the voice to use.
        /// </summary>
        public string VoiceName { get; set; } = "";
        
        /// <summary>
        /// Gets or sets the speech rate, from -10 (slowest) to 10 (fastest).
        /// </summary>
        public int Rate { get; set; } = 0;
        
        /// <summary>
        /// Gets or sets the speech volume, from 0 to 100.
        /// </summary>
        public int Volume { get; set; } = 100;
    }

    /// <summary>
    /// Settings for command processing.
    /// </summary>
    public class CommandSettings
    {
        /// <summary>
        /// Gets or sets a value indicating whether to enable default commands.
        /// </summary>
        public bool EnableDefaultCommands { get; set; } = true;
        
        /// <summary>
        /// Gets or sets the path to a custom commands file.
        /// </summary>
        public string CustomCommandsPath { get; set; } = "commands.json";
    }
}