// VoiceAssistant.Service/Services/ServiceManager.cs

using System.Reflection;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VoiceAssistant.Core.Interfaces;
using VoiceAssistant.Core.Models;
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
            try
            {
                _logger.LogInformation("Voice Assistant Service starting");
                _cts = new CancellationTokenSource();

                // Start IPC service
                Task.Run(async () =>
                {
                    try
                    {
                        // Verify existing model path
                        VerifyWhisperModelPath();

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
                        _logger.LogError(ex, "Error starting services");
                    }
                });

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start Voice Assistant Service");
                return false;
            }
        }

        /// <summary>
        /// Stops the service.
        /// </summary>
        /// <returns>True if the service stopped successfully.</returns>
        public bool Stop()
        {
            try
            {
                _logger.LogInformation("Voice Assistant Service stopping");

                // Cancel any ongoing operations
                _cts?.Cancel();

                // Unsubscribe from events
                _assistantService.StateChanged -= OnAssistantStateChanged;
                _assistantService.CommandProcessed -= OnCommandProcessed;
                _ipcService.MessageReceived -= OnIpcMessageReceived;

                // Stop services
                Task.Run(async () =>
                {
                    await _assistantService.StopAsync();
                    await _ipcService.StopAsync();
                }).Wait();

                _logger.LogInformation("Voice Assistant Service stopped");
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
                (_serviceProvider as IDisposable)?.Dispose();
            }
        }

        private void OnAssistantStateChanged(object sender, AssistantState state)
        {
            // Send state update to UI via IPC
            if (_ipcService.IsConnected)
            {
                var message = new IpcMessage
                {
                    Type = MessageType.StateChange,
                    Content = JsonSerializer.Serialize(state)
                };

                Task.Run(async () => await _ipcService.SendMessageAsync(message));
            }
        }

        private void OnCommandProcessed(object sender, CommandResult result)
        {
            // Send command result to UI via IPC
            if (_ipcService.IsConnected)
            {
                var message = new IpcMessage
                {
                    Type = MessageType.Command,
                    Content = JsonSerializer.Serialize(result)
                };

                Task.Run(async () => await _ipcService.SendMessageAsync(message));
            }
        }

        private void OnIpcMessageReceived(object sender, IpcMessage message)
        {
            _logger.LogDebug("Received IPC message of type {MessageType}", message.Type);

            try
            {
                switch (message.Type)
                {
                    case MessageType.UiRequest when message.Content == "TriggerListening":
                        Task.Run(async () => await _assistantService.TriggerListeningAsync());
                        break;

                    case MessageType.Configuration:
                        // Handle configuration changes
                        // Deserialize and apply configuration
                        break;

                    case MessageType.ServiceStatus:
                        // Send current state back to UI
                        var stateMessage = new IpcMessage
                        {
                            Type = MessageType.StateChange,
                            Content = JsonSerializer.Serialize(_assistantService.CurrentState)
                        };
                        Task.Run(async () => await _ipcService.SendMessageAsync(stateMessage));
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling IPC message of type {MessageType}", message.Type);
            }
        }

        private void VerifyWhisperModelPath()
        {
            try
            {
                string basePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string modelPath = Path.Combine(basePath, "Models", "ggml-base.bin");
                
                if (File.Exists(modelPath))
                {
                    _logger.LogInformation("Found Whisper model at {ModelPath}", modelPath);
                }
                else
                {
                    _logger.LogWarning("Whisper model not found at {ModelPath}. Speech recognition may not work.", modelPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying Whisper model path");
            }
        }

        private void ConfigureServices()
        {
            var services = new ServiceCollection();

            // Add logging
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // Base path for files
            string basePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string keywordPath = Path.Combine(basePath, "Keywords", "wake_up.ppn");
            string modelPath = Path.Combine(basePath, "Models", "ggml-base.bin");

            // Register services
            services.AddSingleton<IAudioCaptureService, AudioCaptureService>();
            
            // Fixed: Added Porcupine access key parameter (an empty string uses the free tier)
            services.AddSingleton<IWakeWordService>(sp => 
                new PorcupineWakeWordService(
                    sp.GetRequiredService<ILogger<PorcupineWakeWordService>>(),
                    keywordPath,
                    string.Empty)); // Access key - empty string for free tier
                    
            // Fixed: Changed WhisperModelType.Base to GgmlType.Base
            services.AddSingleton<ISpeechRecognitionService>(sp => 
                new WhisperSpeechRecognitionService(
                    sp.GetRequiredService<ILogger<WhisperSpeechRecognitionService>>(),
                    modelPath,
                    GgmlType.Base));
                    
            services.AddSingleton<ICommandProcessorService, CommandProcessorService>();
            services.AddSingleton<IAssistantService, AssistantService>();
            services.AddSingleton<IIpcService, NamedPipeIpcService>();

            _serviceProvider = services.BuildServiceProvider();

            // Get required services
            _logger = _serviceProvider.GetRequiredService<ILogger<ServiceManager>>();
            _assistantService = _serviceProvider.GetRequiredService<IAssistantService>();
            _ipcService = _serviceProvider.GetRequiredService<IIpcService>();
        }
    }
}