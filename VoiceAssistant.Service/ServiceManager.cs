// VoiceAssistant.Service/Services/ServiceManager.cs


using System.Reflection;
using System.Text.Json;


using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using VoiceAssistant.Core.Interfaces;
using VoiceAssistant.Core.Models;
using VoiceAssistant.Core.Services;

// Whisper imports
using Whisper.net;
using Whisper.net.Ggml;

namespace VoiceAssistant.Service.Services
{
    /// <summary>
    /// Manages the voice assistant service lifecycle.
    /// </summary>
    public class ServiceManager
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ServiceManager> _logger;
        private readonly IAssistantService _assistantService;
        private readonly IIpcService _ipcService;
        private readonly IConfiguration _configuration;
        private CancellationTokenSource _cts;
        private bool _isStarted = false;
        private int _startAttempts = 0;
        private const int MAX_START_ATTEMPTS = 3;

        /// <summary>
        /// Initializes a new instance of the ServiceManager class.
        /// </summary>
        public ServiceManager()
        {
            // Setup configuration before anything else
            _configuration = CreateConfiguration();
            
            // Configure Serilog from configuration
            ConfigureSerilog(_configuration);
            
            // Configure and build service provider
            _serviceProvider = ConfigureServices(_configuration);
            
            // Get required services
            _logger = _serviceProvider.GetRequiredService<ILogger<ServiceManager>>();
            _assistantService = _serviceProvider.GetRequiredService<IAssistantService>();
            _ipcService = _serviceProvider.GetRequiredService<IIpcService>();
            
            _logger.LogInformation("ServiceManager initialized successfully");
        }
        
        /// <summary>
        /// Creates the configuration from appsettings.json
        /// </summary>
        private static IConfiguration CreateConfiguration()
        {
            string basePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            
            return new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
                .Build();
        }
        
        /// <summary>
        /// Configures Serilog from configuration
        /// </summary>
        private static void ConfigureSerilog(IConfiguration configuration)
        {
            var logConfig = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration);
                
            Log.Logger = logConfig.CreateLogger();
            Log.Information("Logging configured. Starting Voice Assistant...");
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

                // Verify models before starting
                VerifyAndDownloadModelsAsync(_cts.Token).Wait();

                // Start the IPC service asynchronously without waiting for client connection
                Task.Run(async () =>
                {
                    try
                    {
                        // Start IPC service
                        await _ipcService.StartAsync(true, _cts.Token);
                        _ipcService.MessageReceived += OnIpcMessageReceived;
                        _logger.LogInformation("IPC service started as server");

                        // Subscribe to assistant events for IPC notifications
                        _assistantService.StateChanged += OnAssistantStateChanged;
                        _assistantService.CommandProcessed += OnCommandProcessed;

                        // Start the assistant service
                        await _assistantService.StartAsync(_cts.Token);
                        _logger.LogInformation("Assistant service started successfully");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in background service initialization");
                        // Don't rethrow - let the service keep running and retry later
                    }
                });

                // Return true immediately to tell Windows the service started successfully
                _logger.LogInformation("Voice Assistant Service started successfully");
                _isStarted = true;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start Voice Assistant Service");
                
                // Cleanup after failed start
                CleanupAfterFailedStart();
                
                // Retry if we haven't exceeded max attempts
                if (_startAttempts < MAX_START_ATTEMPTS)
                {
                    _logger.LogInformation("Retrying service start in 5 seconds...");
                    Thread.Sleep(5000);
                    return Start();
                }
                
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
                
                // Reset logger before disposing to avoid NullReferenceException in finally block
                var logger = _logger;
                
                // Dispose service provider to clean up all services
                try
                {
                    (_serviceProvider as IDisposable)?.Dispose();
                    logger?.LogDebug("Service provider disposed");
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Error disposing service provider");
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
                    
                    // When wake word is detected, log more information
                    if (state.State == ListeningState.WakeWordDetected)
                    {
                        _logger.LogInformation("Wake word detected! Transitioning to listening state.");
                    }
                    
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
            else
            {
                _logger.LogWarning("Cannot notify UI of state change to {State}: IPC service is not connected", state.State);
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
                
                // Get file paths from configuration
                string speechRecognitionModelRelPath = _configuration.GetValue<string>("VoiceAssistant:SpeechRecognition:ModelPath");
                string wakeWordModelRelPath = _configuration.GetValue<string>("VoiceAssistant:WakeWord:ModelPath");
                
                // Ensure paths are absolute
                string modelPath = Path.IsPathRooted(speechRecognitionModelRelPath) 
                    ? speechRecognitionModelRelPath 
                    : Path.Combine(basePath, speechRecognitionModelRelPath);
                    
                string keywordPath = Path.IsPathRooted(wakeWordModelRelPath)
                    ? wakeWordModelRelPath
                    : Path.Combine(basePath, wakeWordModelRelPath);
                
                // Check Whisper model
                if (File.Exists(modelPath))
                {
                    _logger.LogInformation("Whisper model found at {ModelPath}", modelPath);
                }
                else
                {
                    _logger.LogWarning("Whisper model not found at {ModelPath}", modelPath);
                    return false;
                }
                
                // Check wake word model
                if (File.Exists(keywordPath))
                {
                    _logger.LogInformation("Wake word model found at {KeywordPath}", keywordPath);
                }
                else
                {
                    _logger.LogWarning("Wake word model not found at {KeywordPath}", keywordPath);
                    return false;
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

        /// <summary>
        /// Configures the service collection and builds the service provider
        /// </summary>
        private IServiceProvider ConfigureServices(IConfiguration configuration)
        {
            try
            {
                var services = new ServiceCollection();

                // Add configuration
                services.AddSingleton(configuration);

                // Add logging
                services.AddLogging(builder =>
                {
                    builder.AddSerilog(dispose: true);
                });

                // Ensure required directories exist
                EnsureDirectoriesExist();

                // Get file paths from configuration
                string basePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                
                string speechRecognitionModelRelPath = configuration.GetValue<string>("VoiceAssistant:SpeechRecognition:ModelPath");
                string wakeWordModelRelPath = configuration.GetValue<string>("VoiceAssistant:WakeWord:ModelPath");
                
                // Ensure paths are absolute
                string modelPath = Path.IsPathRooted(speechRecognitionModelRelPath) 
                    ? speechRecognitionModelRelPath 
                    : Path.Combine(basePath, speechRecognitionModelRelPath);
                    
                string keywordPath = Path.IsPathRooted(wakeWordModelRelPath)
                    ? wakeWordModelRelPath
                    : Path.Combine(basePath, wakeWordModelRelPath);

                // Get other configuration values
                string picovoiceApiKey = configuration.GetValue<string>("VoiceAssistant:WakeWord:AccessKey") ?? "";
                float wakeWordSensitivity = configuration.GetValue<float>("VoiceAssistant:WakeWord:Sensitivity", 0.7f);
                string whisperModelType = configuration.GetValue<string>("VoiceAssistant:SpeechRecognition:ModelType", "Base");
                string ipcPipeName = configuration.GetValue<string>("VoiceAssistant:IPC:PipeName", "VoiceAssistantPipe");
                int sampleRate = configuration.GetValue<int>("VoiceAssistant:AudioCapture:SampleRate", 16000);
                int channels = configuration.GetValue<int>("VoiceAssistant:AudioCapture:Channels", 1);
                int deviceNumber = configuration.GetValue<int>("VoiceAssistant:AudioCapture:DeviceNumber", 0);
                int bufferMs = configuration.GetValue<int>("VoiceAssistant:AudioCapture:BufferMilliseconds", 50);

                // Register services with configuration
                services.AddSingleton<IAudioCaptureService, AudioCaptureService>();

                // Wake word service with configuration
                services.AddSingleton<IWakeWordService>(sp => 
                {
                    var logger = sp.GetRequiredService<ILogger<PorcupineWakeWordService>>();
                    
                    logger.LogInformation("PorcupineWakeWordService created with keyword path: {KeywordPath}, sensitivity: {Sensitivity}", 
                        keywordPath, wakeWordSensitivity);
                        
                    var service = new PorcupineWakeWordService(
                        logger,
                        picovoiceApiKey,
                        keywordPath);
                        
                    service.SetSensitivity(wakeWordSensitivity);
                    return service;
                });
                    
                // Speech recognition service with configuration
                services.AddSingleton<ISpeechRecognitionService>(sp => 
                {
                    var logger = sp.GetRequiredService<ILogger<WhisperSpeechRecognitionService>>();
                    
                    // Convert string model type to enum
                    GgmlType ggmlType = whisperModelType.ToLowerInvariant() switch
                    {
                        "tiny" => GgmlType.Tiny,
                        "base" => GgmlType.Base,
                        "small" => GgmlType.Small,
                        "medium" => GgmlType.Medium,
                        // Removed the "large" case as it is not valid for this version
                        _ => GgmlType.Base
                    };
                    
                    logger.LogInformation("WhisperSpeechRecognitionService created with model type {ModelType} at path {ModelPath}", 
                        ggmlType, modelPath);
                        
                    return new WhisperSpeechRecognitionService(
                        logger,
                        modelPath,
                        ggmlType);
                });

                // Command processor service
                services.AddSingleton<ICommandProcessorService, CommandProcessorService>();
                
                // Main assistant service
                services.AddSingleton<IAssistantService, AssistantService>();
                
                // IPC service with configured TCP parameters
                services.AddSingleton<IIpcService>(sp => 
                {
                    var logger = sp.GetRequiredService<ILogger<TcpIpcService>>();
                    string host = configuration.GetValue<string>("VoiceAssistant:IPC:Host", "127.0.0.1");
                    int port = configuration.GetValue<int>("VoiceAssistant:IPC:Port", 5000);
                    logger.LogInformation("Creating TcpIpcService with host: {Host}, port: {Port}", host, port);
                    return new TcpIpcService(logger, host, port);
                });

                // Build and return the service provider
                return services.BuildServiceProvider();
            }
            catch (Exception ex)
            {
                // We may not have the logger set up yet if this fails, so use Serilog directly
                Log.Error(ex, "Failed to configure services");
                throw;
            }
        }
    }
}