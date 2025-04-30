// VoiceAssistant.Core/Services/CommandProcessorService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VoiceAssistant.Core.Interfaces;
using VoiceAssistant.Core.Models;

namespace VoiceAssistant.Core.Services
{
    /// <summary>
    /// Service for processing voice commands.
    /// </summary>
    public class CommandProcessorService : ICommandProcessorService
    {
        private readonly ILogger<CommandProcessorService> _logger;
        private readonly Dictionary<string, (string Description, CommandHandler Handler)> _commandHandlers;
        
        /// <summary>
        /// Initializes a new instance of the CommandProcessorService class.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        /// <exception cref="ArgumentNullException">Thrown when logger is null.</exception>
        public CommandProcessorService(ILogger<CommandProcessorService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _commandHandlers = new Dictionary<string, (string, CommandHandler)>(StringComparer.OrdinalIgnoreCase);
            
            RegisterDefaultCommands();
        }

        /// <inheritdoc/>
        public void RegisterCommand(string commandTrigger, string description, CommandHandler handler)
        {
            if (string.IsNullOrWhiteSpace(commandTrigger))
                throw new ArgumentNullException(nameof(commandTrigger));
                
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
                
            _commandHandlers[commandTrigger.ToLowerInvariant()] = (description, handler);
            _logger.LogInformation("Registered command: {Command} - {Description}", commandTrigger, description);
        }

        /// <inheritdoc/>
        public async Task<CommandResult> ProcessCommandAsync(string commandText, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(commandText))
            {
                return new CommandResult(false, "No command text provided", string.Empty);
            }
            
            string normalizedCommand = commandText.ToLowerInvariant().Trim();
            _logger.LogInformation("Processing command: {Command}", normalizedCommand);
            
            try
            {
                // Find matching command trigger
                var matchingTrigger = _commandHandlers.Keys
                    .FirstOrDefault(trigger => normalizedCommand.Contains(trigger));
                    
                if (matchingTrigger != null)
                {
                    var (_, handler) = _commandHandlers[matchingTrigger];
                    _logger.LogDebug("Found command handler for trigger: {Trigger}", matchingTrigger);
                    return await handler(normalizedCommand, cancellationToken);
                }
                
                // No matching command found
                _logger.LogWarning("No command handler found for: {Command}", normalizedCommand);
                return new CommandResult(false, "I don't understand that command", normalizedCommand);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing command: {Command}", normalizedCommand);
                return new CommandResult(false, $"Error processing command: {ex.Message}", normalizedCommand);
            }
        }

        private void RegisterDefaultCommands()
        {
            // Register some default commands
            RegisterCommand("project mode", "Activates project mode", 
                (command, ct) => Task.FromResult(
                    new CommandResult(true, "Project mode activated", command)));
                    
            RegisterCommand("help", "Shows available commands", 
                (command, ct) =>
                {
                    string helpText = "Available commands:\n";
                    foreach (var cmd in _commandHandlers)
                    {
                        helpText += $"- {cmd.Key}: {cmd.Value.Description}\n";
                    }
                    return Task.FromResult(
                        new CommandResult(true, helpText, command));
                });
                
            RegisterCommand("time", "Tells the current time", 
                (command, ct) => Task.FromResult(
                    new CommandResult(true, $"The current time is {DateTime.Now:t}", command)));
                    
            RegisterCommand("date", "Tells the current date", 
                (command, ct) => Task.FromResult(
                    new CommandResult(true, $"Today is {DateTime.Now:D}", command)));
        }
    }
}