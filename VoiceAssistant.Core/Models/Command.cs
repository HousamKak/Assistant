
// VoiceAssistant.Core/Models/Command.cs
using System;
using System.Collections.Generic;

namespace VoiceAssistant.Core.Models
{
    /// <summary>
    /// Represents a voice command with its metadata.
    /// </summary>
    public class Command
    {
        /// <summary>
        /// Gets or sets the unique identifier for the command.
        /// </summary>
        public Guid Id { get; set; }
        
        /// <summary>
        /// Gets or sets the trigger phrase that activates this command.
        /// </summary>
        public string Trigger { get; set; }
        
        /// <summary>
        /// Gets or sets the description of what the command does.
        /// </summary>
        public string Description { get; set; }
        
        /// <summary>
        /// Gets or sets the full text of the command as transcribed.
        /// </summary>
        public string FullText { get; set; }
        
        /// <summary>
        /// Gets or sets the timestamp when the command was received.
        /// </summary>
        public DateTime Timestamp { get; set; }
        
        /// <summary>
        /// Gets or sets the command parameters extracted from the full text.
        /// </summary>
        public Dictionary<string, string> Parameters { get; set; }

        /// <summary>
        /// Initializes a new instance of the Command class.
        /// </summary>
        public Command()
        {
            Id = Guid.NewGuid();
            Parameters = new Dictionary<string, string>();
            Timestamp = DateTime.Now;
        }
        
        /// <summary>
        /// Initializes a new instance of the Command class with specified trigger and description.
        /// </summary>
        /// <param name="trigger">The trigger phrase for this command.</param>
        /// <param name="description">The description of what the command does.</param>
        public Command(string trigger, string description) : this()
        {
            Trigger = trigger ?? throw new ArgumentNullException(nameof(trigger));
            Description = description;
        }
    }
}