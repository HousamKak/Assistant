// VoiceAssistant.Core/Interfaces/IAudioCaptureService.cs
using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceAssistant.Core.Interfaces
{
    /// <summary>
    /// Interface for audio capture services.
    /// </summary>
    public interface IAudioCaptureService : IDisposable
    {
        /// <summary>
        /// Event triggered when audio data is captured.
        /// </summary>
        event EventHandler<byte[]> AudioDataCaptured;
        
        /// <summary>
        /// Event triggered when PCM audio samples are captured.
        /// </summary>
        event EventHandler<short[]> PcmDataCaptured;
        
        /// <summary>
        /// Gets the sample rate of the captured audio.
        /// </summary>
        int SampleRate { get; }
        
        /// <summary>
        /// Gets the number of channels in the captured audio.
        /// </summary>
        int Channels { get; }
        
        /// <summary>
        /// Gets a value indicating whether the service is currently capturing audio.
        /// </summary>
        bool IsCapturing { get; }
        
        /// <summary>
        /// Starts audio capture.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task StartCaptureAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Stops audio capture.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task StopCaptureAsync();
    }
}
