
// VoiceAssistant.Core/Models/IpcMessage.cs
using System;

namespace VoiceAssistant.Core.Models
{
    /// <summary>
    /// Types of messages exchanged between the service and UI.
    /// </summary>
    public enum MessageType
    {
        StateChange,
        Command,
        ServiceStatus,
        UiRequest,
        Configuration
    }

    /// <summary>
    /// Message container for IPC communication between service and UI.
    /// </summary>
    [Serializable]
    public class IpcMessage
    {
        /// <summary>
        /// Gets or sets the type of message.
        /// </summary>
        public MessageType Type { get; set; }
        
        /// <summary>
        /// Gets or sets the message content, typically serialized as JSON.
        /// </summary>
        public string Content { get; set; }
        
        /// <summary>
        /// Gets or sets the timestamp when the message was created.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>
        /// Initializes a new instance of the IpcMessage class.
        /// </summary>
        public IpcMessage()
        {
        }
        
        /// <summary>
        /// Initializes a new instance of the IpcMessage class with specified type and content.
        /// </summary>
        /// <param name="type">The type of message.</param>
        /// <param name="content">The message content.</param>
        public IpcMessage(MessageType type, string content)
        {
            Type = type;
            Content = content;
            Timestamp = DateTime.Now;
        }
    }
}