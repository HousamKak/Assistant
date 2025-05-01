// VoiceAssistant.Core/Interfaces/ITextToSpeechService.cs
using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceAssistant.Core.Interfaces
{
    /// <summary>
    /// Interface for text-to-speech services.
    /// </summary>
    public interface ITextToSpeechService : IDisposable
    {
        /// <summary>
        /// Gets a value indicating whether the service is ready to speak.
        /// </summary>
        bool IsReady { get; }
        
        /// <summary>
        /// Speaks the specified text asynchronously.
        /// </summary>
        /// <param name="text">The text to speak.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task SpeakAsync(string text, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Sets the voice used for speech synthesis.
        /// </summary>
        /// <param name="voiceName">The name of the voice to use.</param>
        /// <returns>True if the voice was set successfully, otherwise false.</returns>
        bool SetVoice(string voiceName);
        
        /// <summary>
        /// Sets the rate of speech.
        /// </summary>
        /// <param name="rate">The rate of speech, from -10 (slowest) to 10 (fastest).</param>
        void SetRate(int rate);
        
        /// <summary>
        /// Sets the volume of speech.
        /// </summary>
        /// <param name="volume">The volume, from 0 to 100.</param>
        void SetVolume(int volume);
    }
}