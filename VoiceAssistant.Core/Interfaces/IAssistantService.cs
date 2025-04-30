// VoiceAssistant.Core/Interfaces/IAssistantService.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceAssistant.Core.Models;

namespace VoiceAssistant.Core.Interfaces
{
    /// <summary>
    /// Interface for the main assistant service.
    /// </summary>
    public interface IAssistantService : IDisposable
    {
        /// <summary>
        /// Event triggered when the assistant state changes.
        /// </summary>
        event EventHandler<AssistantState> StateChanged;
        
        /// <summary>
        /// Event triggered when a command result is available.
        /// </summary>
        event EventHandler<CommandResult> CommandProcessed;
        
        /// <summary>
        /// Gets the current state of the assistant.
        /// </summary>
        AssistantState CurrentState { get; }
        
        /// <summary>
        /// Starts the assistant service.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task StartAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Stops the assistant service.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task StopAsync();
        
        /// <summary>
        /// Manually triggers the assistant to listen for a command.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task TriggerListeningAsync();
    }
}