// VoiceAssistant.Service/Services/ServiceManager.cs

using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog.Extensions.Logging;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using VoiceAssistant.Core.Interfaces;
using VoiceAssistant.Core.Models;
using VoiceAssistant.Core.Models.Exceptions;
using VoiceAssistant.Core.Services;

// Fixed: changed from Whisper.NET.Native to proper namespaces
using Whisper.net;
using Whisper.net.Ggml;

namespace VoiceAssistant.Service.Services
{
    /// <summary>
    /// Manages the voice assistant service lifecycle.
    /// </summary>
    public class ServiceManager
    {
        private IServiceProvider _serviceProvider;
        private ILogger<ServiceManager> _logger;
        private IAssistantService _assistantService;
        private IIpcService _ipcService;
        private CancellationTokenSource _cts;
        private bool _isStarted = false;
        private int _startAttempts = 0;
        private const int MAX_START_ATTEMPTS = 3;

        /// <summary>
        /// Initializes a new instance of the ServiceManager class.
        /// </summary>
        public ServiceManager()
        {
            ConfigureServices();
        }

        /// <summary>
        /// Starts the service.
        /// </summary>
        /// <returns>True if the service started successfully.</returns>
        public bool Start()
        {
            if (_isStarted)
            {
                _logger.LogWarning("Start requested but service is already running.");
                return true;
            }

            try
            {
                _startAttempts++;
                _logger.LogInformation("Voice Assistant Service starting (Attempt {Attempt}/{MaxAttempts})", 
                    _startAttempts, MAX_START_ATTEMPTS);
                
                _cts = new CancellationTokenSource();

                // First, ensure required directories exist
                EnsureDirectoriesExist();

                // Create a task to start services asynchronously
                var startTask = Task.Run(async () =>
                {
                    try
                    {
                        // Verify model paths and download if needed
                        bool modelsReady = await VerifyAndDownloadModelsAsync(_cts.Token);
                        if (!modelsReady)
                        {
                            _logger.LogError("Required models are missing and could not be downloaded");
                            return false;
                        }

                        _logger.LogInformation("Starting IPC service as server");
                        await _ipcService.StartAsync(true, _cts.Token);
                        
                        _ipcService.MessageReceived += OnIpcMessageReceived;
                        _logger.LogInformation("IPC service started successfully");

                        // Subscribe to assistant events for IPC notifications
                        _assistantService.StateChanged += OnAssistantStateChanged;
                        _assistantService.CommandProcessed += OnCommandProcessed;

                        // Start the assistant service
                        _logger.LogInformation("Starting assistant service");
                        await _assistantService.StartAsync(_cts.Token);
                        _logger.LogInformation("Assistant service started successfully");
                        
                        return true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error starting services");
                        return false;
                    }
                });

                // Wait for the services to start with a timeout
                if (startTask.Wait(TimeSpan.FromSeconds(30)))
                {
                    bool result = startTask.Result;
                    
                    if (result)
                    {
                        _isStarted = true;
                        _logger.LogInformation("Voice Assistant Service started successfully");
                        return true;
                    }
                    else
                    {
                        _logger.LogError("Failed to start Voice Assistant Service");
                        // Try to clean up if possible
                        CleanupAfterFailedStart();
                        
                        // If we've tried enough times, give up
                        if (_startAttempts >= MAX_START_ATTEMPTS)
                        {
                            _logger.LogError("Maximum start attempts reached. Giving up.");
                            return false;
                        }
                        
                        // Otherwise try again
                        _logger.LogInformation("Retrying service start...");
                        return Start();
                    }
                }
                else
                {
                    _logger.LogError("Timeout waiting for service to start");
                    CleanupAfterFailedStart();
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start Voice Assistant Service");
                CleanupAfterFailedStart();
                return false;
            }
        }

        /// <summary>
        /// Stops the service.
        /// </summary>
        /// <returns>True if the service stopped successfully.</returns>
        public bool Stop()
        {
            if (!_isStarted)
            {
                _logger.LogWarning("Stop requested but service is not running.");
                return true;
            }

            _logger.LogInformation("Voice Assistant Service stopping");
            
            try
            {
                // Cancel any ongoing operations
                _cts?.Cancel();

                // Unsubscribe from events
                if (_assistantService != null)
                {
                    _assistantService.StateChanged -= OnAssistantStateChanged;
                    _assistantService.CommandProcessed -= OnCommandProcessed;
                }
                
                if (_ipcService != null)
                {
                    _ipcService.MessageReceived -= OnIpcMessageReceived;
                }

                // Create a task to stop services asynchronously
                var stopTask = Task.Run(async () =>
                {
                    try
                    {
                        if (_assistantService != null)
                        {
                            _logger.LogInformation("Stopping assistant service");
                            await _assistantService.StopAsync();
                            _logger.LogInformation("Assistant service stopped successfully");
                        }
                        
                        if (_ipcService != null && _ipcService.IsConnected)
                        {
                            _logger.LogInformation("Stopping IPC service");
                            await _ipcService.StopAsync();
                            _logger.LogInformation("IPC service stopped successfully");
                        }
                        
                        return true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error stopping services");
                        return false;
                    }
                });

                // Wait for the services to stop with a timeout
                if (stopTask.Wait(TimeSpan.FromSeconds(10)))
                {
                    bool result = stopTask.Result;
                    
                    if (result)
                    {
                        _logger.LogInformation("Voice Assistant Service stopped successfully");
                    }
                    else
                    {
                        _logger.LogError("Failed to stop Voice Assistant Service cleanly");
                    }
                }
                else
                {
                    _logger.LogError("Timeout waiting for service to stop");
                }

                _isStarted = false;
                _startAttempts = 0;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping Voice Assistant Service");
                return false;
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                
                // Dispose service provider to clean up all services
                try
                {
                    (_serviceProvider as IDisposable)?.Dispose();
                    _logger.LogDebug("Service provider disposed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing service provider");
                }
            }
        }

        private void OnAssistantStateChanged(object sender, AssistantState state)
        {
            // Send state update to UI via IPC
            if (_ipcService != null && _ipcService.IsConnected)
            {
                try
                {
                    _logger.LogDebug("Assistant state changed to {State}. Notifying UI", state.State);
                    var message = new IpcMessage
                    {
                        Type = MessageType.StateChange,
                        Content = JsonSerializer.Serialize(state)
                    };

                    Task.Run(async () => await _ipcService.SendMessageAsync(message));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending state change notification");
                }
            }
        }

        private void OnCommandProcessed(object sender, CommandResult result)
        {
            // Send command result to UI via IPC
            if (_ipcService != null && _ipcService.IsConnected)
            {
                try
                {
                    _logger.LogDebug("Command processed: '{CommandText}'. Success: {Success}. Notifying UI", 
                        result.CommandText, result.Success);
                    
                    var message = new IpcMessage
                    {
                        Type = MessageType.Command,
                        Content = JsonSerializer.Serialize(result)
                    };

                    Task.Run(async () => await _ipcService.SendMessageAsync(message));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending command result notification");
                }
            }
        }

        private void OnIpcMessageReceived(object sender, IpcMessage message)
        {
            if (message == null)
            {
                _logger.LogWarning("Received null IPC message");
                return;
            }

            _logger.LogDebug("Received IPC message of type {MessageType}", message.Type);

            try
            {
                switch (message.Type)
                {
                    case MessageType.UiRequest when message.Content == "TriggerListening":
                        _logger.LogInformation("Manual listening trigger requested by UI");
                        Task.Run(async () => 
                        {
                            try
                            {
                                await _assistantService.TriggerListeningAsync();
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error triggering listening");
                            }
                        });
                        break;

                    case MessageType.Configuration:
                        _logger.LogInformation("Configuration update received: {Content}", message.Content);
                        // Parse and apply configuration changes
                        try
                        {
                            // Example for handling wake word sensitivity
                            var config = JsonSerializer.Deserialize<JsonElement>(message.Content);
                            if (config.TryGetProperty("WakeWordSensitivity", out JsonElement sensitivityElement))
                            {
                                if (sensitivityElement.ValueKind == JsonValueKind.Number)
                                {
                                    float sensitivity = sensitivityElement.GetSingle();
                                    _logger.LogInformation("Setting wake word sensitivity to {Sensitivity}", sensitivity);
                                    
                                    // Get wake word service and set sensitivity
                                    var wakeWordService = _serviceProvider.GetService<IWakeWordService>();
                                    if (wakeWordService != null)
                                    {
                                        wakeWordService.SetSensitivity(sensitivity);
                                    }
                                    else
                                    {
                                        _logger.LogWarning("Wake word service not available");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error parsing configuration: {Content}", message.Content);
                        }
                        break;

                    case MessageType.ServiceStatus:
                        _logger.LogInformation("Service status request received");
                        // Send current state back to UI
                        if (_assistantService != null)
                        {
                            var stateMessage = new IpcMessage
                            {
                                Type = MessageType.StateChange,
                                Content = JsonSerializer.Serialize(_assistantService.CurrentState)
                            };
                            Task.Run(async () => await _ipcService.SendMessageAsync(stateMessage));
                        }
                        else
                        {
                            _logger.LogWarning("Cannot send state - assistant service is null");
                        }
                        break;
                        
                    default:
                        _logger.LogWarning("Unhandled message type: {MessageType}", message.Type);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling IPC message of type {MessageType}", message.Type);
            }
        }

        private void EnsureDirectoriesExist()
        {
            try
            {
                string basePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                
                // Create Models directory
                string modelsPath = Path.Combine(basePath, "Models");
                if (!Directory.Exists(modelsPath))
                {
                    _logger.LogInformation("Creating Models directory at {ModelsPath}", modelsPath);
                    Directory.CreateDirectory(modelsPath);
                }
                
                // Create Keywords directory
                string keywordsPath = Path.Combine(basePath, "Keywords");
                if (!Directory.Exists(keywordsPath))
                {
                    _logger.LogInformation("Creating Keywords directory at {KeywordsPath}", keywordsPath);
                    Directory.CreateDirectory(keywordsPath);
                }
                
                // Create logs directory
                string logsPath = Path.Combine(basePath, "logs");
                if (!Directory.Exists(logsPath))
                {
                    _logger.LogInformation("Creating logs directory at {LogsPath}", logsPath);
                    Directory.CreateDirectory(logsPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ensuring directories exist");
                throw;
            }
        }

        private async Task<bool> VerifyAndDownloadModelsAsync(CancellationToken cancellationToken)
        {
            try
            {
                string basePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                
                // Check Whisper model
                string modelPath = Path.Combine(basePath, "Models", "ggml-base.bin");
                if (File.Exists(modelPath))
                {
                    _logger.LogInformation("Whisper model found at {ModelPath}", modelPath);
                }
                else
                {
                    _logger.LogWarning("Whisper model not found at {ModelPath}. Attempting to download...", modelPath);
                    
                    try
                    {
                        // This is where you would actually download and save the model
                        // For safety, we're not implementing the actual download here
                        // Instead, we'll log a message and provide instructions
                        
                        _logger.LogError("Automatic downloading of models is not implemented.");
                        _logger.LogError("Please download the Whisper model manually and place it at: {ModelPath}", modelPath);
                        _logger.LogError("You can download the ggml-base.bin model from: https://huggingface.co/ggerganov/whisper.cpp");
                        
                        return false;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error downloading Whisper model");
                        return false;
                    }
                }
                
                // Check wake word model
                string keywordPath = Path.Combine(basePath, "Keywords", "wake_up.ppn");
                if (File.Exists(keywordPath))
                {
                    _logger.LogInformation("Wake word model found at {KeywordPath}", keywordPath);
                }
                else
                {
                    _logger.LogWarning("Wake word model not found at {KeywordPath}. Attempting to download...", keywordPath);
                    
                    try
                    {
                        // This is where you would actually download and save the model
                        // For safety, we're not implementing the actual download here
                        
                        _logger.LogError("Automatic downloading of wake word models is not implemented.");
                        _logger.LogError("Please obtain a wake_up.ppn file from Picovoice (https://picovoice.ai) and place it at: {KeywordPath}", keywordPath);
                        
                        return false;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error downloading wake word model");
                        return false;
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying models");
                return false;
            }
        }

        private void CleanupAfterFailedStart()
        {
            try
            {
                _logger.LogInformation("Cleaning up after failed start");
                
                // Unsubscribe from events
                if (_assistantService != null)
                {
                    _assistantService.StateChanged -= OnAssistantStateChanged;
                    _assistantService.CommandProcessed -= OnCommandProcessed;
                }
                
                if (_ipcService != null)
                {
                    _ipcService.MessageReceived -= OnIpcMessageReceived;
                }
                
                // Cancel any ongoing operations
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cleanup after failed start");
            }
        }

        private void ConfigureServices()
        {
            try
            {
                var services = new ServiceCollection();

                // Add logging
                services.AddLogging(builder =>
                {
                    builder.AddSerilog(dispose: true);
                });

                // Base path for files
                string basePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string keywordPath = Path.Combine(basePath, "Keywords", "wake_up.ppn");
                string modelPath = Path.Combine(basePath, "Models", "ggml-base.bin");

                // Register services
                services.AddSingleton<IAudioCaptureService, AudioCaptureService>();
                
                // Wake word service
                services.AddSingleton<IWakeWordService>(sp => 
                {
                    var logger = sp.GetRequiredService<ILogger<PorcupineWakeWordService>>();
                    
                    if (!File.Exists(keywordPath))
                    {
                        logger.LogError("Wake word model file not found at: {KeywordPath}", keywordPath);
                        throw new FileNotFoundException($"Wake word model file not found", keywordPath);
                    }
                    
                    return new PorcupineWakeWordService(
                        logger,
                        "YOUR_PICOVOICE_API_KEY_HERE", // Replace with your API key or empty string for demo mode
                        keywordPath);
                });
                    
                // Speech recognition service
                services.AddSingleton<ISpeechRecognitionService>(sp => 
                {
                    var logger = sp.GetRequiredService<ILogger<WhisperSpeechRecognitionService>>();
                    
                    if (!File.Exists(modelPath))
                    {
                        logger.LogError("Whisper model file not found at: {ModelPath}", modelPath);
                        throw new FileNotFoundException($"Whisper model file not found", modelPath);
                    }
                    
                    return new WhisperSpeechRecognitionService(
                        logger,
                        modelPath,
                        GgmlType.Base);
                });
                    
                services.AddSingleton<ICommandProcessorService, CommandProcessorService>();
                services.AddSingleton<IAssistantService, AssistantService>();
                
                // IPC service with pipe name
                services.AddSingleton<IIpcService>(sp => 
                {
                    var logger = sp.GetRequiredService<ILogger<NamedPipeIpcService>>();
                    return new NamedPipeIpcService(logger, "VoiceAssistantPipe");
                });

                _serviceProvider = services.BuildServiceProvider();

                // Get required services
                _logger = _serviceProvider.GetRequiredService<ILogger<ServiceManager>>();
                _assistantService = _serviceProvider.GetRequiredService<IAssistantService>();
                _ipcService = _serviceProvider.GetRequiredService<IIpcService>();
                
                _logger.LogInformation("Services configured successfully");
            }
            catch (Exception ex)
            {
                // We don't have the logger set up yet if this fails, so use Serilog directly
                Log.Error(ex, "Failed to configure services");
                throw;
            }
        }
    }
}