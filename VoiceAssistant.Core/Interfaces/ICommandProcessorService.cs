
// VoiceAssistant.Core/Interfaces/ICommandProcessorService.cs

using VoiceAssistant.Core.Models;

namespace VoiceAssistant.Core.Interfaces
{
    /// <summary>
    /// Interface for voice command processing services.
    /// </summary>
    public interface ICommandProcessorService
    {
        /// <summary>
        /// Processes a voice command.
        /// </summary>
        /// <param name="commandText">The voice command text to process.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>A task resulting in the command processing result.</returns>
        Task<CommandResult> ProcessCommandAsync(string commandText, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Registers a new command handler.
        /// </summary>
        /// <param name="commandTrigger">The text that triggers this command.</param>
        /// <param name="description">Description of what the command does.</param>
        /// <param name="handler">The function to execute when the command is triggered.</param>
        void RegisterCommand(string commandTrigger, string description, CommandHandler handler);
    }
    
    /// <summary>
    /// Delegate for command handling functions.
    /// </summary>
    /// <param name="commandText">The full text of the command.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task resulting in the command processing result.</returns>
    public delegate Task<CommandResult> CommandHandler(string commandText, CancellationToken cancellationToken);
}
