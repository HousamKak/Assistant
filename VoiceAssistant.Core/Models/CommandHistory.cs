// VoiceAssistant.Core/Models/CommandHistory.cs
using System;
using System.Collections.Generic;

namespace VoiceAssistant.Core.Models
{
    /// <summary>
    /// Represents the history of processed commands.
    /// </summary>
    public class CommandHistory
    {
        /// <summary>
        /// Gets the maximum number of commands to keep in history.
        /// </summary>
        public int MaxHistorySize { get; }
        
        /// <summary>
        /// Gets the list of command results in chronological order.
        /// </summary>
        public List<CommandHistoryItem> Items { get; }
        
        /// <summary>
        /// Initializes a new instance of the CommandHistory class.
        /// </summary>
        /// <param name="maxHistorySize">The maximum number of commands to keep in history.</param>
        public CommandHistory(int maxHistorySize = 100)
        {
            MaxHistorySize = maxHistorySize > 0 ? maxHistorySize : throw new ArgumentOutOfRangeException(nameof(maxHistorySize));
            Items = new List<CommandHistoryItem>();
        }
        
        /// <summary>
        /// Adds a command result to the history.
        /// </summary>
        /// <param name="commandText">The original command text.</param>
        /// <param name="result">The command processing result.</param>
        public void AddCommand(string commandText, CommandResult result)
        {
            if (string.IsNullOrEmpty(commandText))
                throw new ArgumentNullException(nameof(commandText));
                
            if (result == null)
                throw new ArgumentNullException(nameof(result));
                
            Items.Add(new CommandHistoryItem
            {
                CommandText = commandText,
                Result = result,
                Timestamp = DateTime.Now
            });
            
            // Trim the history if it exceeds the maximum size
            if (Items.Count > MaxHistorySize)
            {
                Items.RemoveAt(0);
            }
        }
        
        /// <summary>
        /// Clears the command history.
        /// </summary>
        public void Clear()
        {
            Items.Clear();
        }
    }

    /// <summary>
    /// Represents a single command history item.
    /// </summary>
    public class CommandHistoryItem
    {
        /// <summary>
        /// Gets or sets the original command text.
        /// </summary>
        public string CommandText { get; set; }
        
        /// <summary>
        /// Gets or sets the result of command processing.
        /// </summary>
        public CommandResult Result { get; set; }
        
        /// <summary>
        /// Gets or sets the timestamp when the command was processed.
        /// </summary>
        public DateTime Timestamp { get; set; }
    }
}
