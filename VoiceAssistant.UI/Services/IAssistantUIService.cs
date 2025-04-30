// VoiceAssistant.UI/Services/IAssistantUIService.cs
using System;
using VoiceAssistant.Core.Models;

namespace VoiceAssistant.UI.Services
{
    /// <summary>
    /// Interface for the Assistant UI service.
    /// </summary>
    public interface IAssistantUIService
    {
        /// <summary>
        /// Event triggered when the assistant state changes.
        /// </summary>
        event EventHandler<AssistantState> StateChanged;
        
        /// <summary>
        /// Event triggered when a command result is available.
        /// </summary>
        event EventHandler<CommandResult> CommandProcessed;
        
        /// <summary>
        /// Gets the current state of the assistant.
        /// </summary>
        AssistantState CurrentState { get; }
        
        /// <summary>
        /// Manually triggers the assistant to listen for a command.
        /// </summary>
        /// <returns>True if the request was sent successfully.</returns>
        bool TriggerListening();
        
        /// <summary>
        /// Gets the assistant settings.
        /// </summary>
        /// <returns>The current assistant settings.</returns>
        AssistantSettings GetSettings();
        
        /// <summary>
        /// Saves the assistant settings.
        /// </summary>
        /// <param name="settings">The settings to save.</param>
        void SaveSettings(AssistantSettings settings);
        
        /// <summary>
        /// Applies the specified settings to the assistant.
        /// </summary>
        /// <param name="settings">The settings to apply.</param>
        void ApplySettings(AssistantSettings settings);
    }
    
    /// <summary>
    /// Assistant UI settings.
    /// </summary>
    public class AssistantSettings
    {
        /// <summary>
        /// Gets or sets a value indicating whether to minimize to tray when closing.
        /// </summary>
        public bool MinimizeToTray { get; set; } = true;
        
        /// <summary>
        /// Gets or sets the wake word detection sensitivity.
        /// </summary>
        public double WakeWordSensitivity { get; set; } = 0.7;
        
        /// <summary>
        /// Gets or sets a value indicating whether to enable window transparency.
        /// </summary>
        public bool EnableTransparency { get; set; } = true;
        
        /// <summary>
        /// Gets or sets the display mode.
        /// </summary>
        public string DisplayMode { get; set; } = "AlwaysVisible";
    }
}