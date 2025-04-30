// VoiceAssistant.Core/Services/WhisperSpeechRecognitionService.cs


using System.Text;
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

        public event EventHandler<string> SpeechRecognized;
        public bool IsReady => _whisperFactory != null;

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
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (IsReady)
            {
                _logger.LogWarning("Whisper already initialized.");
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    _logger.LogInformation("Loading Whisper model {ModelType} from {ModelPath}",
                        _modelType, _modelPath);

                    if (!File.Exists(_modelPath))
                        throw new SpeechRecognitionException($"Model not found at {_modelPath}");

                    _whisperFactory = WhisperFactory.FromPath(_modelPath);
                    _logger.LogInformation("Whisper model loaded successfully.");
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Whisper");
                throw new SpeechRecognitionException("Initialization error", ex);
            }
        }

        public async Task<string> TranscribeAsync(byte[] audioData, CancellationToken cancellationToken = default)
        {
            if (!IsReady)
                throw new InvalidOperationException("Service not initialized. Call InitializeAsync first.");

            return await Task.Run(async () =>
            {
                using var memoryStream = new MemoryStream(audioData);

                try
                {
                    using var wav = new WaveFileReader(memoryStream);

                    // ensure correct format
                    if (wav.WaveFormat.SampleRate != 16000
                        || wav.WaveFormat.BitsPerSample != 16
                        || wav.WaveFormat.Channels != 1)
                    {
                        _logger.LogWarning("Converting audio to 16kHz, 16-bit, mono");
                        using var converted = new MemoryStream();
                        using var resampler = new MediaFoundationResampler(wav, new WaveFormat(16000, 16, 1));
                        WaveFileWriter.WriteWavFileToStream(converted, resampler);
                        converted.Position = 0;
                        return await TranscribeWavStreamAsync(converted);
                    }

                    memoryStream.Position = 0;
                    return await TranscribeWavStreamAsync(memoryStream);
                }
                catch (FormatException fx)
                {
                    _logger.LogError(fx, "Invalid WAV format");
                    throw new SpeechRecognitionException("Invalid WAV format", fx);
                }
            }, cancellationToken);
        }

        private async Task<string> TranscribeWavStreamAsync(Stream wavStream)
        {
            var sb = new StringBuilder();
            wavStream.Position = 0;

            // create a fresh processor for each call
            using var processor = _whisperFactory.CreateBuilder()
                .WithLanguage("en")
                // If you want translation, uncomment the next line:
                // .WithTranslate()
                .Build();

            await foreach (var segment in processor.ProcessAsync(wavStream))
            {
                sb.Append(segment.Text);
            }

            var transcription = sb.ToString().Trim();
            if (!string.IsNullOrEmpty(transcription))
            {
                _logger.LogInformation("Transcription: {Text}", transcription);
                SpeechRecognized?.Invoke(this, transcription);
            }
            else
            {
                _logger.LogWarning("No speech detected.");
            }

            return transcription;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _whisperFactory?.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
