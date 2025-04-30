// VoiceAssistant.Core/Services/WhisperSpeechRecognitionService.cs

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using VoiceAssistant.Core.Interfaces;
using VoiceAssistant.Core.Models.Exceptions;
using Whisper.net;
using Whisper.net.Ggml;

namespace VoiceAssistant.Core.Services
{
    /// <summary>
    /// Implementation of speech recognition using OpenAI's Whisper via Whisper.NET.
    /// </summary>
    public class WhisperSpeechRecognitionService : ISpeechRecognitionService, IDisposable
    {
        private readonly ILogger<WhisperSpeechRecognitionService> _logger;
        private WhisperFactory _whisperFactory;
        private bool _disposed;
        private readonly string _modelPath;
        private readonly GgmlType _modelType;
        private volatile bool _isInitializing = false;
        private SemaphoreSlim _initializationLock = new SemaphoreSlim(1, 1);

        public event EventHandler<string> SpeechRecognized;

        public bool IsReady => _whisperFactory != null && !_isInitializing;

        public WhisperSpeechRecognitionService(
            ILogger<WhisperSpeechRecognitionService> logger,
            string modelPath,
            GgmlType modelType = GgmlType.Base)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (string.IsNullOrWhiteSpace(modelPath))
                throw new ArgumentNullException(nameof(modelPath));

            _modelPath = modelPath;
            _modelType = modelType;

            _logger.LogInformation("WhisperSpeechRecognitionService created with model type {ModelType} at path {ModelPath}", 
                _modelType, _modelPath);
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (IsReady)
            {
                _logger.LogInformation("Whisper already initialized");
                return;
            }

            if (_isInitializing)
            {
                _logger.LogInformation("Whisper initialization already in progress. Waiting...");
                
                // Wait for initialization to complete
                while (_isInitializing && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(100, cancellationToken);
                }
                
                if (IsReady)
                {
                    _logger.LogInformation("Whisper initialization completed by another thread");
                    return;
                }
                
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Whisper initialization wait cancelled");
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            // Prevent multiple threads from initializing simultaneously
            await _initializationLock.WaitAsync(cancellationToken);
            try
            {
                if (IsReady)
                {
                    _logger.LogInformation("Whisper already initialized (check after lock)");
                    return;
                }

                _isInitializing = true;
                _logger.LogInformation("Starting Whisper initialization");

                await Task.Run(() =>
                {
                    _logger.LogInformation("Loading Whisper model {ModelType} from {ModelPath}",
                        _modelType, _modelPath);

                    if (!File.Exists(_modelPath))
                    {
                        string errorMessage = $"Whisper model not found at {_modelPath}";
                        _logger.LogError(errorMessage);
                        throw new SpeechRecognitionException(errorMessage);
                    }

                    try
                    {
                        _whisperFactory = WhisperFactory.FromPath(_modelPath);
                        _logger.LogInformation("Whisper model loaded successfully");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to load Whisper model: {ErrorMessage}", ex.Message);
                        throw new SpeechRecognitionException("Failed to load Whisper model", ex);
                    }
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Whisper initialization was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Whisper: {ErrorMessage}", ex.Message);
                throw new SpeechRecognitionException("Initialization error", ex);
            }
            finally
            {
                _isInitializing = false;
                _initializationLock.Release();
            }
        }

        public async Task<string> TranscribeAsync(byte[] audioData, CancellationToken cancellationToken = default)
        {
            if (audioData == null || audioData.Length == 0)
            {
                _logger.LogError("Cannot transcribe null or empty audio data");
                throw new ArgumentException("Audio data cannot be null or empty", nameof(audioData));
            }

            if (!IsReady)
            {
                _logger.LogWarning("Transcribe called but service is not initialized. Attempting initialization...");
                await InitializeAsync(cancellationToken);
            }

            _logger.LogDebug("Transcribing audio data of length {Length} bytes", audioData.Length);
            return await Task.Run(async () =>
            {
                using var memoryStream = new MemoryStream(audioData);

                try
                {
                    // Verify that the data is a valid WAV file
                    if (!IsValidWavHeader(audioData))
                    {
                        _logger.LogWarning("Audio data does not appear to be a valid WAV file. Attempting to process anyway.");
                    }

                    using var wav = new WaveFileReader(memoryStream);
                    _logger.LogDebug("WAV format: {SampleRate}Hz, {BitsPerSample}-bit, {Channels} channel(s)",
                        wav.WaveFormat.SampleRate, wav.WaveFormat.BitsPerSample, wav.WaveFormat.Channels);

                    // ensure correct format for Whisper (16kHz, 16-bit, mono)
                    if (wav.WaveFormat.SampleRate != 16000 || 
                        wav.WaveFormat.BitsPerSample != 16 || 
                        wav.WaveFormat.Channels != 1)
                    {
                        _logger.LogInformation("Converting audio to 16kHz, 16-bit, mono (from {CurrentFormat})",
                            $"{wav.WaveFormat.SampleRate}Hz, {wav.WaveFormat.BitsPerSample}-bit, {wav.WaveFormat.Channels} channel(s)");
                        
                        using var converted = new MemoryStream();
                        using var resampler = new MediaFoundationResampler(wav, new WaveFormat(16000, 16, 1));
                        WaveFileWriter.WriteWavFileToStream(converted, resampler);
                        converted.Position = 0;
                        return await TranscribeWavStreamAsync(converted, cancellationToken);
                    }

                    memoryStream.Position = 0;
                    return await TranscribeWavStreamAsync(memoryStream, cancellationToken);
                }
                catch (FormatException fx)
                {
                    _logger.LogError(fx, "Invalid WAV format: {ErrorMessage}", fx.Message);
                    throw new SpeechRecognitionException("Invalid WAV format", fx);
                }
                catch (EndOfStreamException esx)
                {
                    _logger.LogError(esx, "Unexpected end of audio stream: {ErrorMessage}", esx.Message);
                    throw new SpeechRecognitionException("Unexpected end of audio stream", esx);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error transcribing audio: {ErrorMessage}", ex.Message);
                    throw new SpeechRecognitionException("Error transcribing audio", ex);
                }
            }, cancellationToken);
        }

        private async Task<string> TranscribeWavStreamAsync(Stream wavStream, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Processing WAV stream for transcription");
            var sb = new StringBuilder();
            wavStream.Position = 0;

            try
            {
                // Create a fresh processor for each call with language settings
                _logger.LogDebug("Creating Whisper processor");
                using var processor = _whisperFactory.CreateBuilder()
                    .WithLanguage("en")
                    // If you want translation, uncomment the next line:
                    // .WithTranslate()
                    .Build();

                int segmentCount = 0;
                _logger.LogDebug("Starting transcription");
                await foreach (var segment in processor.ProcessAsync(wavStream).WithCancellation(cancellationToken))
                {
                    segmentCount++;
                    _logger.LogDebug("Received segment {SegmentNumber}: {Text}", segmentCount, segment.Text);
                    sb.Append(segment.Text);
                }
                _logger.LogDebug("Transcription completed with {SegmentCount} segments", segmentCount);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Transcription was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Whisper processing: {ErrorMessage}", ex.Message);
                throw new SpeechRecognitionException("Error during speech processing", ex);
            }

            var transcription = sb.ToString().Trim();
            if (!string.IsNullOrEmpty(transcription))
            {
                _logger.LogInformation("Transcription result: \"{Text}\"", transcription);
                SpeechRecognized?.Invoke(this, transcription);
            }
            else
            {
                _logger.LogWarning("No speech detected in audio");
            }

            return transcription;
        }

        private bool IsValidWavHeader(byte[] data)
        {
            // Check for minimum WAV header size
            if (data.Length < 44)
                return false;

            // Check for "RIFF" magic number
            if (data[0] != 'R' || data[1] != 'I' || data[2] != 'F' || data[3] != 'F')
                return false;

            // Check for "WAVE" format
            if (data[8] != 'W' || data[9] != 'A' || data[10] != 'V' || data[11] != 'E')
                return false;

            // Check for "fmt " chunk
            if (data[12] != 'f' || data[13] != 'm' || data[14] != 't' || data[15] != ' ')
                return false;

            return true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                _logger.LogInformation("Disposing WhisperSpeechRecognitionService");
                try
                {
                    _whisperFactory?.Dispose();
                    _initializationLock?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing Whisper resources");
                }
            }

            _disposed = true;
        }
    }
}