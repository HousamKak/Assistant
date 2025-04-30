// VoiceAssistant.Core/Models/Exceptions/AssistantException.cs
using System;

namespace VoiceAssistant.Core.Models.Exceptions
{
    /// <summary>
    /// Base exception class for voice assistant-related exceptions.
    /// </summary>
    public class AssistantException : Exception
    {
        /// <summary>
        /// Gets the component where the exception occurred.
        /// </summary>
        public string Component { get; }
        
        /// <summary>
        /// Initializes a new instance of the AssistantException class.
        /// </summary>
        /// <param name="message">The exception message.</param>
        public AssistantException(string message) : base(message)
        {
            Component = "General";
        }
        
        /// <summary>
        /// Initializes a new instance of the AssistantException class.
        /// </summary>
        /// <param name="message">The exception message.</param>
        /// <param name="component">The component where the exception occurred.</param>
        public AssistantException(string message, string component) : base(message)
        {
            Component = component;
        }
        
        /// <summary>
        /// Initializes a new instance of the AssistantException class.
        /// </summary>
        /// <param name="message">The exception message.</param>
        /// <param name="component">The component where the exception occurred.</param>
        /// <param name="innerException">The inner exception.</param>
        public AssistantException(string message, string component, Exception innerException) 
            : base(message, innerException)
        {
            Component = component;
        }
    }
    
    /// <summary>
    /// Exception thrown when there is an error with audio capture.
    /// </summary>
    public class AudioCaptureException : AssistantException
    {
        /// <summary>
        /// Initializes a new instance of the AudioCaptureException class.
        /// </summary>
        /// <param name="message">The exception message.</param>
        public AudioCaptureException(string message) : base(message, "AudioCapture")
        {
        }
        
        /// <summary>
        /// Initializes a new instance of the AudioCaptureException class.
        /// </summary>
        /// <param name="message">The exception message.</param>
        /// <param name="innerException">The inner exception.</param>
        public AudioCaptureException(string message, Exception innerException) 
            : base(message, "AudioCapture", innerException)
        {
        }
    }
    
    /// <summary>
    /// Exception thrown when there is an error with wake word detection.
    /// </summary>
    public class WakeWordException : AssistantException
    {
        /// <summary>
        /// Initializes a new instance of the WakeWordException class.
        /// </summary>
        /// <param name="message">The exception message.</param>
        public WakeWordException(string message) : base(message, "WakeWord")
        {
        }
        
        /// <summary>
        /// Initializes a new instance of the WakeWordException class.
        /// </summary>
        /// <param name="message">The exception message.</param>
        /// <param name="innerException">The inner exception.</param>
        public WakeWordException(string message, Exception innerException) 
            : base(message, "WakeWord", innerException)
        {
        }
    }
    
    /// <summary>
    /// Exception thrown when there is an error with speech recognition.
    /// </summary>
    public class SpeechRecognitionException : AssistantException
    {
        /// <summary>
        /// Initializes a new instance of the SpeechRecognitionException class.
        /// </summary>
        /// <param name="message">The exception message.</param>
        public SpeechRecognitionException(string message) : base(message, "SpeechRecognition")
        {
        }
        
        /// <summary>
        /// Initializes a new instance of the SpeechRecognitionException class.
        /// </summary>
        /// <param name="message">The exception message.</param>
        /// <param name="innerException">The inner exception.</param>
        public SpeechRecognitionException(string message, Exception innerException) 
            : base(message, "SpeechRecognition", innerException)
        {
        }
    }
    
    /// <summary>
    /// Exception thrown when there is an error with command processing.
    /// </summary>
    public class CommandException : AssistantException
    {
        /// <summary>
        /// Initializes a new instance of the CommandException class.
        /// </summary>
        /// <param name="message">The exception message.</param>
        public CommandException(string message) : base(message, "Command")
        {
        }
        
        /// <summary>
        /// Initializes a new instance of the CommandException class.
        /// </summary>
        /// <param name="message">The exception message.</param>
        /// <param name="innerException">The inner exception.</param>
        public CommandException(string message, Exception innerException) 
            : base(message, "Command", innerException)
        {
        }
    }
}