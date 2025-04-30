
// VoiceAssistant.Core/Interfaces/IIpcService.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using VoiceAssistant.Core.Models;

namespace VoiceAssistant.Core.Interfaces
{
    /// <summary>
    /// Interface for IPC (Inter-Process Communication) services.
    /// </summary>
    public interface IIpcService : IDisposable
    {
        /// <summary>
        /// Event triggered when a message is received.
        /// </summary>
        event EventHandler<IpcMessage> MessageReceived;
        
        /// <summary>
        /// Gets a value indicating whether the service is connected.
        /// </summary>
        bool IsConnected { get; }
        
        /// <summary>
        /// Starts the IPC service.
        /// </summary>
        /// <param name="isServer">True if this instance should act as the server, false for client.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task StartAsync(bool isServer, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Stops the IPC service.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task StopAsync();
        
        /// <summary>
        /// Sends a message through the IPC channel.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task SendMessageAsync(IpcMessage message);
    }
}