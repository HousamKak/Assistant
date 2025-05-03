
// VoiceAssistant.Core/Interfaces/IWakeWordService.cs


namespace VoiceAssistant.Core.Interfaces
{
    /// <summary>
    /// Interface for wake word detection services.
    /// </summary>
    public interface IWakeWordService : IDisposable
    {
        /// <summary>
        /// Event triggered when a wake word is detected.
        /// </summary>
        event EventHandler<string> WakeWordDetected;
        
        /// <summary>
        /// Gets a value indicating whether the service is currently active.
        /// </summary>
        bool IsActive { get; }
        
        /// <summary>
        /// Gets the sensitivity level for wake word detection.
        /// </summary>
        float Sensitivity { get; }
        
        /// <summary>
        /// Sets the sensitivity level for wake word detection.
        /// </summary>
        /// <param name="sensitivity">The sensitivity value between 0 and 1.</param>
        void SetSensitivity(float sensitivity);
        
        /// <summary>
        /// Starts the wake word detection service.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task StartAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Stops the wake word detection service.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task StopAsync();
        
        /// <summary>
        /// Processes audio data to detect wake words.
        /// </summary>
        /// <param name="pcmData">The PCM audio data to process.</param>
        /// <returns>True if a wake word was detected, otherwise false.</returns>
        bool ProcessAudio(short[] pcmData);
    }
}

