
// VoiceAssistant.Core/Models/CommandResult.cs
namespace VoiceAssistant.Core.Models
{
    /// <summary>
    /// Represents the result of a voice command processing.
    /// </summary>
    public class CommandResult
    {
        /// <summary>
        /// Gets or sets whether the command was successfully processed.
        /// </summary>
        public bool Success { get; set; }
        
        /// <summary>
        /// Gets or sets the response message for the command.
        /// </summary>
        public string Message { get; set; }
        
        /// <summary>
        /// Gets or sets the original command text that was processed.
        /// </summary>
        public string CommandText { get; set; }

        /// <summary>
        /// Initializes a new instance of the CommandResult class.
        /// </summary>
        /// <param name="success">Whether the command was successfully processed.</param>
        /// <param name="message">The response message.</param>
        /// <param name="commandText">The original command text.</param>
        public CommandResult(bool success, string message, string commandText)
        {
            Success = success;
            Message = message;
            CommandText = commandText;
        }
    }
}