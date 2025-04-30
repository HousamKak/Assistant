// VoiceAssistant.Core/Services/WhisperSpeechRecognitionService.cs
using System;
using System.IO;
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
    public class WhisperSpeechRecognitionService : ISpeechRecognitionService
    {
        private readonly ILogger<WhisperSpeechRecognitionService> _logger;
        private WhisperFactory _whisperFactory;
        private IWhisperProcessor _whisperProcessor;
        private bool _disposed;
        private readonly string _modelPath;
        private readonly GgmlType _modelType;

        /// <inheritdoc/>
        public event EventHandler<string> SpeechRecognized;

        /// <inheritdoc/>
        public bool IsReady => _whisperProcessor != null;

        /// <summary>
        /// Initializes a new instance of the WhisperSpeechRecognitionService class.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        /// <param name="modelPath">Path to the Whisper model file.</param>
        /// <param name="modelType">Type of Whisper model to use.</param>
        /// <exception cref="ArgumentNullException">Thrown when logger or modelPath is null.</exception>
        public WhisperSpeechRecognitionService(
            ILogger<WhisperSpeechRecognitionService> logger,
            string modelPath,
            GgmlType modelType = GgmlType.Base)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            if (string.IsNullOrEmpty(modelPath))
                throw new ArgumentNullException(nameof(modelPath));
                
            _modelPath = modelPath;
            _modelType = modelType;
        }

        /// <inheritdoc/>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (IsReady)
            {
                _logger.LogWarning("Attempted to initialize speech recognition service when already initialized");
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    _logger.LogInformation("Initializing Whisper with model {ModelType} from {ModelPath}", 
                        _modelType, _modelPath);
                    
                    // Check if model file exists
                    if (!File.Exists(_modelPath))
                    {
                        throw new SpeechRecognitionException($"Whisper model file not found at path: {_modelPath}");
                    }
                        
                    _whisperFactory = WhisperFactory.FromPath(_modelPath);
                    _whisperProcessor = _whisperFactory.CreateBuilder()
                        .WithLanguage("en")
                        .WithTranslate(false)
                        .Build();
                        
                    _logger.LogInformation("Whisper speech recognition service initialized");
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Whisper speech recognition");
                throw new SpeechRecognitionException("Failed to initialize speech recognition service", ex);
            }
        }

        /// <inheritdoc/>
        public async Task<string> TranscribeAsync(byte[] audioData, CancellationToken cancellationToken = default)
        {
            if (!IsReady)
            {
                throw new InvalidOperationException("Speech recognition service is not initialized. Call InitializeAsync first.");
            }

            try
            {
                string transcription = await Task.Run(() =>
                {
                    using var memoryStream = new MemoryStream(audioData);
                    
                    try
                    {
                        using var wavStream = new WaveFileReader(memoryStream);
                        
                        // Ensure audio is in the correct format (16kHz, 16-bit, mono)
                        var format = wavStream.WaveFormat;
                        if (format.SampleRate != 16000 || format.BitsPerSample != 16 || format.Channels != 1)
                        {
                            _logger.LogWarning("Audio format conversion required: {CurrentFormat} to 16kHz, 16-bit, mono", format);
                            
                            using var convertedStream = new MemoryStream();
                            using var resampler = new MediaFoundationResampler(wavStream, new WaveFormat(16000, 16, 1));
                            WaveFileWriter.WriteWavFileToStream(convertedStream, resampler);
                            convertedStream.Position = 0;
                            
                            return TranscribeWavStream(convertedStream);
                        }
                        
                        // Already in correct format
                        memoryStream.Position = 0;
                        return TranscribeWavStream(memoryStream);
                    }
                    catch (FormatException ex)
                    {
                        _logger.LogError(ex, "Invalid audio format");
                        throw new SpeechRecognitionException("Invalid audio format. Ensure the audio is in WAV format.", ex);
                    }
                }, cancellationToken);
                
                if (!string.IsNullOrWhiteSpace(transcription))
                {
                    _logger.LogInformation("Transcription completed: {Transcription}", transcription);
                    SpeechRecognized?.Invoke(this, transcription);
                }
                else
                {
                    _logger.LogWarning("No transcription result");
                }
                
                return transcription;
            }
            catch (Exception ex) when (
                !(ex is SpeechRecognitionException || ex is OperationCanceledException))
            {
                _logger.LogError(ex, "Error transcribing audio");
                throw new SpeechRecognitionException("Error during speech transcription", ex);
            }
        }

        private string TranscribeWavStream(Stream wavStream)
        {
            string result = string.Empty;
            wavStream.Position = 0; // Ensure we're at the start of the stream
            
            _whisperProcessor.Process(wavStream, segment =>
            {
                result += segment.Text;
                return true; // Continue processing
            });
            
            // Clean up the result
            return result.Trim();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes resources used by the speech recognition service.
        /// </summary>
        /// <param name="disposing">True if disposing managed resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _whisperProcessor?.Dispose();
                _whisperProcessor = null;
                
                _whisperFactory?.Dispose();
                _whisperFactory = null;
            }

            _disposed = true;
        }
    }
}