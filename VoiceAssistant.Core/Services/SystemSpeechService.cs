// VoiceAssistant.Core/Services/SystemSpeechService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Speech.Synthesis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VoiceAssistant.Core.Interfaces;

namespace VoiceAssistant.Core.Services
{
    /// <summary>
    /// Implementation of text-to-speech using System.Speech for Windows.
    /// </summary>
    public class SystemSpeechService : ITextToSpeechService
    {
        private readonly ILogger<SystemSpeechService> _logger;
        private readonly SpeechSynthesizer _synthesizer;
        private bool _disposed;

        /// <inheritdoc/>
        public bool IsReady => _synthesizer != null && !_disposed;

        /// <summary>
        /// Initializes a new instance of the SystemSpeechService class.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        /// <exception cref="ArgumentNullException">Thrown when logger is null.</exception>
        public SystemSpeechService(ILogger<SystemSpeechService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            try
            {
                _synthesizer = new SpeechSynthesizer();
                _synthesizer.SetOutputToDefaultAudioDevice();
                _logger.LogInformation("Speech synthesizer initialized using default audio device");
                
                // Log available voices
                var voices = _synthesizer.GetInstalledVoices()
                    .Where(v => v.Enabled)
                    .Select(v => v.VoiceInfo.Name)
                    .ToList();
                    
                _logger.LogInformation("Available voices: {Voices}", string.Join(", ", voices));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize speech synthesizer");
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(text))
            {
                _logger.LogWarning("Empty text provided for speech synthesis");
                return;
            }

            if (!IsReady)
            {
                _logger.LogError("Speech synthesizer is not ready");
                throw new InvalidOperationException("Speech synthesizer is not ready");
            }

            try
            {
                _logger.LogDebug("Speaking text: \"{Text}\"", text);
                
                // Use Task.Run to make the speech non-blocking
                await Task.Run(() =>
                {
                    // Create a linked cancellation token to support cancellation
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    
                    // Handle cancellation
                    linkedCts.Token.Register(() =>
                    {
                        _logger.LogInformation("Speech synthesis cancelled");
                        _synthesizer.SpeakAsyncCancelAll();
                    });
                    
                    // Use a TaskCompletionSource to convert event-based pattern to Task
                    var tcs = new TaskCompletionSource<bool>();
                    
                    void SpeakCompleted(object sender, SpeakCompletedEventArgs args)
                    {
                        _synthesizer.SpeakCompleted -= SpeakCompleted;
                        
                        if (args.Cancelled)
                        {
                            tcs.SetCanceled();
                        }
                        else if (args.Error != null)
                        {
                            tcs.SetException(args.Error);
                        }
                        else
                        {
                            tcs.SetResult(true);
                        }
                    }
                    
                    _synthesizer.SpeakCompleted += SpeakCompleted;
                    
                    try
                    {
                        // Start the speech asynchronously
                        _synthesizer.SpeakAsync(text);
                        
                        // Wait for completion or cancellation
                        tcs.Task.Wait(linkedCts.Token);
                        
                        _logger.LogDebug("Text spoken successfully");
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation("Speech synthesis was cancelled");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error during speech synthesis");
                        throw;
                    }
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Speech synthesis operation cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error synthesizing speech for text: \"{Text}\"", text);
                throw;
            }
        }

        /// <inheritdoc/>
        public bool SetVoice(string voiceName)
        {
            if (!IsReady)
            {
                _logger.LogError("Speech synthesizer is not ready");
                return false;
            }

            try
            {
                var voices = _synthesizer.GetInstalledVoices()
                    .Where(v => v.Enabled)
                    .Select(v => v.VoiceInfo)
                    .ToList();
                    
                var voice = voices.FirstOrDefault(v => 
                    v.Name.Equals(voiceName, StringComparison.OrdinalIgnoreCase));
                    
                if (voice != null)
                {
                    _synthesizer.SelectVoice(voice.Name);
                    _logger.LogInformation("Voice set to {VoiceName}", voice.Name);
                    return true;
                }
                
                _logger.LogWarning("Voice {VoiceName} not found. Available voices: {Voices}", 
                    voiceName, string.Join(", ", voices.Select(v => v.Name)));
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting voice to {VoiceName}", voiceName);
                return false;
            }
        }

        /// <inheritdoc/>
        public void SetRate(int rate)
        {
            if (!IsReady)
            {
                _logger.LogError("Speech synthesizer is not ready");
                return;
            }

            try
            {
                // Clamp rate between -10 and 10
                int clampedRate = Math.Max(-10, Math.Min(10, rate));
                
                _synthesizer.Rate = clampedRate;
                _logger.LogInformation("Speech rate set to {Rate}", clampedRate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting speech rate to {Rate}", rate);
            }
        }

        /// <inheritdoc/>
        public void SetVolume(int volume)
        {
            if (!IsReady)
            {
                _logger.LogError("Speech synthesizer is not ready");
                return;
            }

            try
            {
                // Clamp volume between 0 and 100
                int clampedVolume = Math.Max(0, Math.Min(100, volume));
                
                _synthesizer.Volume = clampedVolume;
                _logger.LogInformation("Speech volume set to {Volume}", clampedVolume);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting speech volume to {Volume}", volume);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes resources used by the service.
        /// </summary>
        /// <param name="disposing">True if disposing managed resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _logger.LogInformation("Disposing SystemSpeechService");
                _synthesizer?.Dispose();
            }

            _disposed = true;
        }
    }
}