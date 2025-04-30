// VoiceAssistant.Core/Services/PorcupineWakeWordService.cs

using Microsoft.Extensions.Logging;
using Pv;
using VoiceAssistant.Core.Interfaces;
using VoiceAssistant.Core.Models.Exceptions;

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
        private SemaphoreSlim _initLock = new SemaphoreSlim(1, 1);
        private int _audioProcessedCount = 0;
        private DateTime _lastWakeWordTime = DateTime.MinValue;
        private readonly TimeSpan _cooldownPeriod = TimeSpan.FromSeconds(2);
        
        // Add field for frame buffering
        private readonly List<short> _bufferedPcm = new List<short>();
        private int _frameLength;

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
            
            if (!File.Exists(_keywordPath))
            {
                var errorMessage = $"Keyword file not found at: {_keywordPath}";
                _logger.LogError(errorMessage);
                throw new FileNotFoundException(errorMessage, _keywordPath);
            }
            
            Sensitivity = 0.7f; // Default sensitivity
            _logger.LogInformation("PorcupineWakeWordService created with keyword path: {KeywordPath}, sensitivity: {Sensitivity}", 
                _keywordPath, Sensitivity);
        }

        private void InitializePorcupine()
        {
            try
            {
                _logger.LogDebug("Initializing Porcupine with sensitivity {Sensitivity}", Sensitivity);
                
                if (string.IsNullOrEmpty(_accessKey))
                {
                    _logger.LogWarning("Access key is empty. Porcupine may run in demo mode with limitations.");
                }
                
                if (!File.Exists(_keywordPath))
                {
                    var errorMessage = $"Keyword file not found at: {_keywordPath}";
                    _logger.LogError(errorMessage);
                    throw new WakeWordException(errorMessage);
                }
                
                // Validate sensitivity range
                if (Sensitivity < 0f || Sensitivity > 1f)
                {
                    var errorMessage = $"Invalid sensitivity value: {Sensitivity}. Must be between 0 and 1.";
                    _logger.LogError(errorMessage);
                    throw new ArgumentOutOfRangeException(nameof(Sensitivity), errorMessage);
                }
                
                _porcupine = Porcupine.FromKeywordPaths(
                    _accessKey,
                    new[] { _keywordPath },
                    modelPath: null,
                    sensitivities: new[] { Sensitivity }
                );
                
                // Store the required frame length
                _frameLength = _porcupine.FrameLength;
                _logger.LogInformation("Porcupine initialized successfully with frame length: {FrameLength}", _frameLength);
                
                // Clear any previously buffered data
                _bufferedPcm.Clear();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Porcupine: {ErrorMessage}", ex.Message);
                throw new WakeWordException("Failed to initialize Porcupine", ex);
            }
        }

        /// <inheritdoc/>
        public void SetSensitivity(float sensitivity)
        {
            if (sensitivity < 0 || sensitivity > 1)
            {
                var errorMessage = $"Sensitivity must be between 0 and 1. Provided value: {sensitivity}";
                _logger.LogError(errorMessage);
                throw new ArgumentOutOfRangeException(nameof(sensitivity), errorMessage);
            }
                
            _logger.LogInformation("Setting wake word sensitivity from {OldSensitivity} to {NewSensitivity}", 
                Sensitivity, sensitivity);
                
            Sensitivity = sensitivity;
            
            // If already active, we need to reinitialize with new sensitivity
            if (IsActive)
            {
                _logger.LogInformation("Reinitializing Porcupine with new sensitivity");
                _initLock.Wait();
                try
                {
                    _porcupine?.Dispose();
                    InitializePorcupine();
                }
                finally
                {
                    _initLock.Release();
                }
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

            await _initLock.WaitAsync(cancellationToken);
            try
            {
                _logger.LogInformation("Starting wake word detection service");
                
                if (_porcupine != null)
                {
                    _logger.LogDebug("Disposing existing Porcupine instance before initialization");
                    _porcupine.Dispose();
                    _porcupine = null;
                }
                
                InitializePorcupine();
                _isActive = true;
                _audioProcessedCount = 0;
                _logger.LogInformation("Wake word detection service started with sensitivity {Sensitivity}", Sensitivity);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Wake word service start was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start wake word service: {ErrorMessage}", ex.Message);
                throw new WakeWordException("Failed to start wake word service", ex);
            }
            finally
            {
                _initLock.Release();
            }
        }

        /// <inheritdoc/>
        public async Task StopAsync()
        {
            if (!_isActive)
            {
                _logger.LogDebug("Stop requested but wake word service is not active");
                return;
            }

            await _initLock.WaitAsync();
            try
            {
                _logger.LogInformation("Stopping wake word detection service");
                _isActive = false;
                
                if (_porcupine != null)
                {
                    _porcupine.Dispose();
                    _porcupine = null;
                    _logger.LogDebug("Porcupine instance disposed");
                }
                
                // Clear buffered audio data
                _bufferedPcm.Clear();
                
                _logger.LogInformation("Wake word detection service stopped after processing {AudioProcessedCount} audio frames", 
                    _audioProcessedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping wake word service: {ErrorMessage}", ex.Message);
                throw new WakeWordException("Error stopping wake word service", ex);
            }
            finally
            {
                _initLock.Release();
            }
        }

        /// <inheritdoc/>
        public bool ProcessAudio(short[] pcmData)
        {
            if (pcmData == null)
            {
                _logger.LogWarning("Null PCM data received for processing");
                return false;
            }
            
            if (pcmData.Length == 0)
            {
                _logger.LogWarning("Empty PCM data received for processing");
                return false;
            }

            if (!_isActive || _porcupine == null)
            {
                _logger.LogDebug("Cannot process audio: wake word service is not active or not initialized");
                return false;
            }

            try
            {
                // Add incoming audio data to buffer
                _bufferedPcm.AddRange(pcmData);
                
                bool wakeWordDetected = false;
                
                // Process complete frames while we have enough data
                while (_bufferedPcm.Count >= _frameLength)
                {
                    // Extract exactly one frame of audio
                    short[] frame = _bufferedPcm.GetRange(0, _frameLength).ToArray();
                    _bufferedPcm.RemoveRange(0, _frameLength);
                    
                    // Process the frame
                    _audioProcessedCount++;
                    
                    // Log occasionally to avoid flooding the logs
                    if (_audioProcessedCount % 1000 == 0)
                    {
                        _logger.LogDebug("Processed {Count} audio frames", _audioProcessedCount);
                    }
                    
                    int keywordIndex = _porcupine.Process(frame);
                    
                    if (keywordIndex >= 0)
                    {
                        // Check for cooldown period to avoid multiple detections in quick succession
                        DateTime now = DateTime.Now;
                        if (now - _lastWakeWordTime < _cooldownPeriod)
                        {
                            _logger.LogDebug("Wake word detected but within cooldown period ({TimeSinceLastDetection}ms). Ignoring.", 
                                (now - _lastWakeWordTime).TotalMilliseconds);
                            continue;
                        }
                        
                        string keyword = "wake up"; // Default keyword name
                        _lastWakeWordTime = now;
                        
                        _logger.LogInformation("Wake word detected: {Keyword} (keywordIndex: {KeywordIndex})", 
                            keyword, keywordIndex);
                            
                        try
                        {
                            WakeWordDetected?.Invoke(this, keyword);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error in wake word detection event handler: {ErrorMessage}", ex.Message);
                        }
                        
                        wakeWordDetected = true;
                    }
                }
                
                return wakeWordDetected;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing audio for wake word detection: {ErrorMessage}", ex.Message);
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
                _logger.LogInformation("Disposing PorcupineWakeWordService");
                try
                {
                    if (_isActive)
                    {
                        _isActive = false;
                        _logger.LogDebug("Marking service as inactive during disposal");
                    }
                    
                    _porcupine?.Dispose();
                    _porcupine = null;
                    
                    _initLock?.Dispose();
                    _initLock = null;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing wake word service: {ErrorMessage}", ex.Message);
                }
            }

            _disposed = true;
        }
    }
}