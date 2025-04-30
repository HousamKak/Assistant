// VoiceAssistant.Core/Services/PorcupineWakeWordService.cs

using Microsoft.Extensions.Logging;
using Pv;
using VoiceAssistant.Core.Interfaces;

namespace VoiceAssistant.Core.Services
{
    /// <summary>
    /// Implementation of wake word detection using Picovoice Porcupine.
    /// </summary>
    public class PorcupineWakeWordService : IWakeWordService
    {
        private readonly ILogger<PorcupineWakeWordService> _logger;
        private Porcupine _porcupine;
        private readonly string _accessKey;
        private readonly string _keywordPath;
        private bool _disposed;
        private bool _isActive;

        /// <inheritdoc/>
        public event EventHandler<string> WakeWordDetected;

        /// <inheritdoc/>
        public bool IsActive => _isActive;

        /// <inheritdoc/>
        public float Sensitivity { get; private set; }

        /// <summary>
        /// Initializes a new instance of the PorcupineWakeWordService class.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        /// <param name="accessKey">The access key for Picovoice API.</param>
        /// <param name="keywordPath">Path to the keyword file.</param>
        /// <exception cref="ArgumentNullException">Thrown when logger or keywordPath is null.</exception>
        /// <exception cref="FileNotFoundException">Thrown when the keyword file is not found.</exception>
        public PorcupineWakeWordService(
            ILogger<PorcupineWakeWordService> logger,
            string accessKey,
            string keywordPath)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _accessKey = accessKey ?? throw new ArgumentNullException(nameof(accessKey));
            if (string.IsNullOrEmpty(keywordPath))
                throw new ArgumentNullException(nameof(keywordPath));
            _keywordPath = keywordPath;
            Sensitivity = 0.7f; // Default sensitivity
        }

        private void InitializePorcupine()
        {
            try
            {
                _porcupine = Porcupine.FromKeywordPaths(
                    _accessKey,
                    new[] { _keywordPath },
                    modelPath: null,
                    sensitivities: new[] { Sensitivity }
                );
                _logger.LogDebug("Porcupine initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Porcupine");
                throw;
            }
        }

        /// <inheritdoc/>
        public void SetSensitivity(float sensitivity)
        {
            if (sensitivity < 0 || sensitivity > 1)
                throw new ArgumentOutOfRangeException(nameof(sensitivity), "Sensitivity must be between 0 and 1");
                
            Sensitivity = sensitivity;
            
            // If already active, we need to reinitialize with new sensitivity
            if (IsActive)
            {
                _porcupine?.Dispose();
                InitializePorcupine();
            }
        }

        /// <inheritdoc/>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (_isActive)
            {
                _logger.LogWarning("Attempted to start wake word service when already active");
                return;
            }

            await Task.Run(() =>
            {
                InitializePorcupine();
                _isActive = true;
                _logger.LogInformation("Wake word detection service started with sensitivity {Sensitivity}", Sensitivity);
            }, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task StopAsync()
        {
            if (!_isActive)
            {
                return;
            }

            await Task.Run(() =>
            {
                _isActive = false;
                _porcupine?.Dispose();
                _porcupine = null;
                _logger.LogInformation("Wake word detection service stopped");
            });
        }

        /// <inheritdoc/>
        public bool ProcessAudio(short[] pcmData)
        {
            if (!_isActive || _porcupine == null)
                return false;

            try
            {
                int keywordIndex = _porcupine.Process(pcmData);
                
                if (keywordIndex >= 0)
                {
                    string keyword = "awaken imperium";
                    _logger.LogInformation("Wake word detected: {Keyword}", keyword);
                    WakeWordDetected?.Invoke(this, keyword);
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing audio for wake word detection");
                return false;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes resources used by the wake word service.
        /// </summary>
        /// <param name="disposing">True if disposing managed resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                if (_isActive)
                {
                    _isActive = false;
                }
                
                _porcupine?.Dispose();
                _porcupine = null;
            }

            _disposed = true;
        }
    }
}