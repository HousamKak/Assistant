// VoiceAssistant.Core/Services/AssistantService.cs
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using VoiceAssistant.Core.Interfaces;
using VoiceAssistant.Core.Models;

namespace VoiceAssistant.Core.Services
{
    /// <summary>
    /// Main assistant service that coordinates audio capture, wake word detection, and speech recognition.
    /// </summary>
    public class AssistantService : IAssistantService
    {
        private readonly ILogger<AssistantService> _logger;
        private readonly IAudioCaptureService _audioCaptureService;
        private readonly IWakeWordService _wakeWordService;
        private readonly ISpeechRecognitionService _speechRecognitionService;
        private readonly ICommandProcessorService _commandProcessorService;
        
        private MemoryStream _commandBuffer;
        private CancellationTokenSource _cts;
        private bool _disposed;

        /// <inheritdoc/>
        public event EventHandler<AssistantState> StateChanged;
        
        /// <inheritdoc/>
        public event EventHandler<CommandResult> CommandProcessed;

        /// <inheritdoc/>
        public AssistantState CurrentState { get; private set; }

        /// <summary>
        /// Initializes a new instance of the AssistantService class.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        /// <param name="audioCaptureService">The audio capture service.</param>
        /// <param name="wakeWordService">The wake word detection service.</param>
        /// <param name="speechRecognitionService">The speech recognition service.</param>
        /// <param name="commandProcessorService">The command processor service.</param>
        /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
        public AssistantService(
            ILogger<AssistantService> logger,
            IAudioCaptureService audioCaptureService,
            IWakeWordService wakeWordService,
            ISpeechRecognitionService speechRecognitionService,
            ICommandProcessorService commandProcessorService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _audioCaptureService = audioCaptureService ?? throw new ArgumentNullException(nameof(audioCaptureService));
            _wakeWordService = wakeWordService ?? throw new ArgumentNullException(nameof(wakeWordService));
            _speechRecognitionService = speechRecognitionService ?? throw new ArgumentNullException(nameof(speechRecognitionService));
            _commandProcessorService = commandProcessorService ?? throw new ArgumentNullException(nameof(commandProcessorService));
            
            CurrentState = new AssistantState();
        }

        /// <inheritdoc/>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (CurrentState.State != ListeningState.Idle)
            {
                _logger.LogWarning("Assistant is already running in state {State}", CurrentState.State);
                return;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            
            try
            {
                _logger.LogInformation("Initializing speech recognition service");
                await _speechRecognitionService.InitializeAsync(_cts.Token);
                
                _logger.LogInformation("Starting wake word service");
                await _wakeWordService.StartAsync(_cts.Token);
                
                _wakeWordService.WakeWordDetected += OnWakeWordDetected;
                _audioCaptureService.AudioDataCaptured += OnAudioDataCaptured;
                _audioCaptureService.PcmDataCaptured += OnPcmDataCaptured;
                
                UpdateState(ListeningState.WaitingForWakeWord);
                
                _logger.LogInformation("Starting audio capture");
                await _audioCaptureService.StartCaptureAsync(_cts.Token);
                
                _logger.LogInformation("Assistant started successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start assistant");
                
                // Cleanup on error
                await StopAsync();
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task StopAsync()
        {
            if (CurrentState.State == ListeningState.Idle)
            {
                return;
            }

            try
            {
                _cts?.Cancel();
                
                _wakeWordService.WakeWordDetected -= OnWakeWordDetected;
                _audioCaptureService.AudioDataCaptured -= OnAudioDataCaptured;
                _audioCaptureService.PcmDataCaptured -= OnPcmDataCaptured;
                
                if (_audioCaptureService.IsCapturing)
                {
                    await _audioCaptureService.StopCaptureAsync();
                }
                
                if (_wakeWordService.IsActive)
                {
                    await _wakeWordService.StopAsync();
                }
                
                _commandBuffer?.Dispose();
                _commandBuffer = null;
                
                UpdateState(ListeningState.Idle);
                _logger.LogInformation("Assistant stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping assistant");
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task TriggerListeningAsync()
        {
            if (CurrentState.State != ListeningState.WaitingForWakeWord || !CurrentState.IsEnabled)
            {
                _logger.LogWarning("Cannot trigger listening in current state {State} or when disabled", CurrentState.State);
                return;
            }
            
            _logger.LogInformation("Manually triggering listening mode");
            await BeginListeningForCommandAsync();
        }

        private void OnWakeWordDetected(object sender, string keyword)
        {
            if (CurrentState.State != ListeningState.WaitingForWakeWord || !CurrentState.IsEnabled)
            {
                return;
            }

            _logger.LogInformation("Wake word detected: {Keyword}", keyword);
            
            // Start capturing the command
            Task.Run(async () => await BeginListeningForCommandAsync());
        }

        private void OnPcmDataCaptured(object sender, short[] pcmData)
        {
            if (CurrentState.State != ListeningState.WaitingForWakeWord || !CurrentState.IsEnabled)
            {
                return;
            }

            // Process audio for wake word detection
            _wakeWordService.ProcessAudio(pcmData);
        }

        private void OnAudioDataCaptured(object sender, byte[] audioData)
        {
            if (CurrentState.State != ListeningState.Listening || !CurrentState.IsEnabled)
            {
                return;
            }

            // Buffer audio for command recognition
            _commandBuffer?.Write(audioData, 0, audioData.Length);
            
            // Check if we've captured enough audio (roughly 5 seconds)
            if (_commandBuffer.Length > _audioCaptureService.SampleRate * 2 * 5) // 16-bit samples * 5 seconds
            {
                _logger.LogDebug("Command buffer full, processing command");
                
                // Stop capturing command audio and process it
                Task.Run(async () => await ProcessCommandBufferAsync());
            }
        }

        private async Task BeginListeningForCommandAsync()
        {
            try
            {
                UpdateState(ListeningState.Listening);
                
                // Create a new buffer for the command audio
                _commandBuffer?.Dispose();
                _commandBuffer = new MemoryStream();
                
                _logger.LogInformation("Listening for command");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error beginning listening for command");
                UpdateState(ListeningState.WaitingForWakeWord);
            }
        }

        private async Task ProcessCommandBufferAsync()
        {
            try
            {
                UpdateState(ListeningState.Processing);
                
                // Get the audio data from the buffer
                _commandBuffer.Position = 0;
                var audioData = _commandBuffer.ToArray();
                
                // Convert raw PCM to WAV format for Whisper
                byte[] wavData;
                using (var wavStream = new MemoryStream())
                {
                    using (var writer = new WaveFileWriter(wavStream, new WaveFormat(_audioCaptureService.SampleRate, _audioCaptureService.Channels)))
                    {
                        writer.Write(audioData, 0, audioData.Length);
                    }
                    wavData = wavStream.ToArray();
                }
                
                _logger.LogDebug("Transcribing command audio of length {Length} bytes", wavData.Length);
                
                // Transcribe the audio
                string transcription = await _speechRecognitionService.TranscribeAsync(wavData, _cts.Token);
                
                if (string.IsNullOrWhiteSpace(transcription))
                {
                    _logger.LogWarning("Command transcription was empty");
                    UpdateState(ListeningState.WaitingForWakeWord);
                    return;
                }
                
                CurrentState.LastTranscription = transcription;
                _logger.LogInformation("Command transcription: {Transcription}", transcription);
                
                // Process the command
                var result = await _commandProcessorService.ProcessCommandAsync(transcription, _cts.Token);
                
                UpdateState(ListeningState.Responding);
                
                // Notify about command result
                CommandProcessed?.Invoke(this, result);
                
                _logger.LogInformation("Command processed: {Success} - {Message}", 
                    result.Success, result.Message);
                    
                // Wait a moment before going back to wake word detection
                await Task.Delay(1000, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Command processing canceled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing command audio");
            }
            finally
            {
                UpdateState(ListeningState.WaitingForWakeWord);
                
                // Clean up the command buffer
                _commandBuffer?.Dispose();
                _commandBuffer = null;
            }
        }

        private void UpdateState(ListeningState newState)
        {
            if (CurrentState.State == newState)
            {
                return;
            }

            CurrentState.State = newState;
            CurrentState.LastStateChange = DateTime.Now;
            
            _logger.LogDebug("Assistant state changed to {State}", newState);
            
            // Notify about state change
            StateChanged?.Invoke(this, CurrentState);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes resources used by the assistant service.
        /// </summary>
        /// <param name="disposing">True if disposing managed resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _cts?.Cancel();
                _cts?.Dispose();
                
                _commandBuffer?.Dispose();
                _commandBuffer = null;
            }

            _disposed = true;
        }
    }
}