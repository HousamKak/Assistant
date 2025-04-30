// VoiceAssistant.Core/Models/AssistantState.cs
using System;

namespace VoiceAssistant.Core.Models
{
    /// <summary>
    /// Represents the different operational states of the voice assistant.
    /// </summary>
    public enum ListeningState
    {
        Idle,
        WaitingForWakeWord,
        WakeWordDetected,
        Listening,
        Processing,
        Responding
    }

    /// <summary>
    /// Encapsulates the current state of the voice assistant.
    /// </summary>
    public class AssistantState
    {
        /// <summary>
        /// Gets or sets the current listening state.
        /// </summary>
        public ListeningState State { get; set; } = ListeningState.Idle;
        
        /// <summary>
        /// Gets or sets the timestamp of the last state change.
        /// </summary>
        public DateTime LastStateChange { get; set; } = DateTime.Now;
        
        /// <summary>
        /// Gets or sets the last transcribed text from speech recognition.
        /// </summary>
        public string LastTranscription { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets whether the assistant is currently enabled.
        /// </summary>
        public bool IsEnabled { get; set; } = true;
    }
}