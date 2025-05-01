// VoiceAssistant.UI/App.xaml.cs
using System;
using System.IO;
using System.Reflection;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using VoiceAssistant.Core.Interfaces;
using VoiceAssistant.Core.Services;
using VoiceAssistant.UI.Services;

namespace VoiceAssistant.UI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private readonly IServiceProvider _serviceProvider;

        public App()
        {
            // Set up a simple console logger for immediate feedback during startup
            Console.WriteLine("Initializing Voice Assistant UI...");
            
            // Configure services
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();
            
            Console.WriteLine("Service provider built successfully");
        }

        // Add this public property to expose the service provider
        public IServiceProvider ServiceProvider => _serviceProvider;

        private void ConfigureServices(IServiceCollection services)
        {
            Console.WriteLine("Configuring services...");
            
            // Add logging with enhanced console output
            services.AddLogging(builder =>
            {
                // Add Debug logger
                builder.AddDebug();
                
                // Configure console logger with custom settings for better readability
                builder.AddConsole(options =>
                {
                    options.IncludeScopes = true;
                    options.TimestampFormat = "[HH:mm:ss.fff] ";
                    options.FormatterName = ConsoleFormatterNames.Simple;
                });
                
                // Set minimum level to Debug to capture all relevant information
                builder.SetMinimumLevel(LogLevel.Debug);
            });

            // Register services
            services.AddSingleton<IIpcService>(provider => {
                var logger = provider.GetRequiredService<ILogger<TcpIpcService>>();
                Console.WriteLine("Creating TcpIpcService with host: 127.0.0.1, port: 5000");
                return new TcpIpcService(logger, "127.0.0.1", 5000);
            });
            services.AddSingleton<IAssistantUIService, AssistantUIService>();
            services.AddSingleton<IStartupService, StartupService>();

            // Add text-to-speech service
            services.AddSingleton<ITextToSpeechService, SystemSpeechService>();

            // Register windows
            services.AddTransient<MainWindow>();
            services.AddTransient<SettingsWindow>();
            
            Console.WriteLine("Services configured successfully");
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            Console.WriteLine("======= VOICE ASSISTANT UI STARTING =======");
            
            try
            {
                // Get the logger
                var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
                logger.LogInformation("Voice Assistant UI starting...");
                
                // Create the main window
                var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
                
                // Get the startup service
                var startupService = _serviceProvider.GetRequiredService<IStartupService>();
                
                // Check if the voice assistant service is installed
                Console.WriteLine("Checking if Voice Assistant service is installed...");
                if (!startupService.IsServiceInstalled())
                {
                    logger.LogError("The Voice Assistant Service is not installed");
                    Console.WriteLine("ERROR: The Voice Assistant Service is not installed");
                    MessageBox.Show(
                        "The Voice Assistant Service is not installed. Some features may not work correctly.\n\n" +
                        "Please run the installer or install the service manually.",
                        "Service Not Installed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                // Check if service is running, start it if not
                else if (!startupService.IsServiceRunning())
                {
                    logger.LogWarning("Voice Assistant service is not running");
                    Console.WriteLine("WARNING: Voice Assistant service is not running");
                    
                    var result = MessageBox.Show(
                        "The Voice Assistant Service is not running. Would you like to start it now?",
                        "Service Not Running",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        Console.WriteLine("Attempting to start Voice Assistant service...");
                        logger.LogInformation("Starting Voice Assistant service");
                        if (startupService.StartService())
                        {
                            logger.LogInformation("Voice Assistant service started successfully");
                            Console.WriteLine("SUCCESS: Voice Assistant service started successfully");
                            MessageBox.Show(
                                "Voice Assistant service started successfully.\n\n" +
                                "It may take a few seconds to initialize before responding to voice commands.",
                                "Service Started",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        }
                        else
                        {
                            logger.LogError("Failed to start Voice Assistant service");
                            Console.WriteLine("ERROR: Failed to start Voice Assistant service");
                            MessageBox.Show(
                                "Failed to start Voice Assistant service. The application will continue, " +
                                "but voice commands will not work.\n\n" +
                                "Try using the 'Restart Service' option from the system tray menu.",
                                "Service Start Failed",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                        }
                    }
                }
                else
                {
                    logger.LogInformation("Voice Assistant service is already running");
                    Console.WriteLine("INFO: Voice Assistant service is already running");
                }
                
                // Show the window after service check
                Console.WriteLine("Showing main window...");
                mainWindow.Show();
                logger.LogInformation("Voice Assistant UI started successfully");
                Console.WriteLine("Voice Assistant UI started successfully");
                Console.WriteLine("======= STARTUP COMPLETE =======");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CRITICAL ERROR: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                
                var logger = _serviceProvider?.GetService<ILogger<App>>();
                logger?.LogError(ex, "Error during application startup");
                
                MessageBox.Show(
                    $"An error occurred during startup: {ex.Message}",
                    "Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                
                Shutdown();
            }
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            Console.WriteLine("======= APPLICATION SHUTTING DOWN =======");
            
            // Get the logger
            var logger = _serviceProvider?.GetService<ILogger<App>>();
            logger?.LogInformation("Voice Assistant UI shutting down...");
            
            // Dispose services
            if (_serviceProvider is IDisposable disposable)
            {
                Console.WriteLine("Disposing service provider...");
                disposable.Dispose();
            }
            
            logger?.LogInformation("Voice Assistant UI shutdown complete");
            Console.WriteLine("Voice Assistant UI shutdown complete");
            Console.WriteLine("======= SHUTDOWN COMPLETE =======");
        }
    }
}