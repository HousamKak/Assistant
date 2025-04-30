// VoiceAssistant.Core/Interfaces/ISpeechRecognitionService.cs

namespace VoiceAssistant.Core.Interfaces
{
    /// <summary>
    /// Interface for speech recognition services.
    /// </summary>
    public interface ISpeechRecognitionService : IDisposable
    {
        /// <summary>
        /// Event triggered when speech recognition results are available.
        /// </summary>
        event EventHandler<string> SpeechRecognized;
        
        /// <summary>
        /// Gets a value indicating whether the service is currently active.
        /// </summary>
        bool IsReady { get; }
        
        /// <summary>
        /// Initializes the speech recognition service.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task InitializeAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Transcribes speech from audio data.
        /// </summary>
        /// <param name="audioData">The audio data to transcribe.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>A task resulting in the transcribed text.</returns>
        Task<string> TranscribeAsync(byte[] audioData, CancellationToken cancellationToken = default);
    }
}