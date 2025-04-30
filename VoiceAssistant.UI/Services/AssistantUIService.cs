// VoiceAssistant.UI/Services/AssistantUIService.cs
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VoiceAssistant.Core.Interfaces;
using VoiceAssistant.Core.Models;

namespace VoiceAssistant.UI.Services
{
    /// <summary>
    /// Implementation of the Assistant UI service.
    /// </summary>
    public class AssistantUIService : IAssistantUIService
    {
        private readonly ILogger<AssistantUIService> _logger;
        private readonly IIpcService _ipcService;
        private AssistantSettings _settings;
        private readonly string _settingsFilePath;

        /// <inheritdoc/>
        public event EventHandler<AssistantState> StateChanged;
        
        /// <inheritdoc/>
        public event EventHandler<CommandResult> CommandProcessed;

        /// <inheritdoc/>
        public AssistantState CurrentState { get; private set; } = new AssistantState();

        /// <summary>
        /// Initializes a new instance of the AssistantUIService class.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        /// <param name="ipcService">The IPC service.</param>
        /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
        public AssistantUIService(ILogger<AssistantUIService> logger, IIpcService ipcService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _ipcService = ipcService ?? throw new ArgumentNullException(nameof(ipcService));
            
            // Settings file path
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VoiceAssistant");
                
            // Create directory if it doesn't exist
            if (!Directory.Exists(appDataPath))
            {
                Directory.CreateDirectory(appDataPath);
            }
            
            _settingsFilePath = Path.Combine(appDataPath, "settings.json");
            
            // Load settings
            _settings = LoadSettingsFromFile();
            
            // Subscribe to IPC messages
            _ipcService.MessageReceived += OnIpcMessageReceived;
        }

        /// <inheritdoc/>
        public bool TriggerListening()
        {
            try
            {
                if (!_ipcService.IsConnected)
                {
                    _logger.LogWarning("Cannot trigger listening: IPC service is not connected");
                    return false;
                }
                
                var message = new IpcMessage
                {
                    Type = MessageType.UiRequest,
                    Content = "TriggerListening"
                };
                
                Task.Run(async () => await _ipcService.SendMessageAsync(message));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error triggering listening");
                return false;
            }
        }

        /// <inheritdoc/>
        public AssistantSettings GetSettings()
        {
            return _settings;
        }

        /// <inheritdoc/>
        public void SaveSettings(AssistantSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }
            
            try
            {
                _settings = settings;
                
                // Save settings to file
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                
                File.WriteAllText(_settingsFilePath, json);
                _logger.LogInformation("Settings saved to {FilePath}", _settingsFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving settings");
                throw;
            }
        }

        /// <inheritdoc/>
        public void ApplySettings(AssistantSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }
            
            try
            {
                // Apply sensitivity setting via IPC
                if (_ipcService.IsConnected)
                {
                    var message = new IpcMessage
                    {
                        Type = MessageType.Configuration,
                        Content = JsonSerializer.Serialize(new
                        {
                            WakeWordSensitivity = settings.WakeWordSensitivity
                        })
                    };
                    
                    Task.Run(async () => await _ipcService.SendMessageAsync(message));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying settings");
                throw;
            }
        }

        private AssistantSettings LoadSettingsFromFile()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    var settings = JsonSerializer.Deserialize<AssistantSettings>(json);
                    
                    if (settings != null)
                    {
                        _logger.LogInformation("Settings loaded from {FilePath}", _settingsFilePath);
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading settings from file");
            }
            
            // Return default settings if file doesn't exist or loading failed
            return new AssistantSettings();
        }

        private void OnIpcMessageReceived(object sender, IpcMessage message)
        {
            try
            {
                switch (message.Type)
                {
                    case MessageType.StateChange:
                        var state = JsonSerializer.Deserialize<AssistantState>(message.Content);
                        if (state != null)
                        {
                            CurrentState = state;
                            StateChanged?.Invoke(this, state);
                        }
                        break;
                        
                    case MessageType.Command:
                        var result = JsonSerializer.Deserialize<CommandResult>(message.Content);
                        if (result != null)
                        {
                            CommandProcessed?.Invoke(this, result);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling IPC message");
            }
        }
    }
}
