// VoiceAssistant.UI/App.xaml.cs
using System;
using System.IO;
using System.Reflection;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
            // Configure services
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Add logging
            services.AddLogging(builder =>
            {
                builder.AddDebug();
                builder.SetMinimumLevel(LogLevel.Debug);
            });

            // Register services
            services.AddSingleton<IIpcService, NamedPipeIpcService>();
            services.AddSingleton<IAssistantUIService, AssistantUIService>();
            services.AddSingleton<IStartupService, StartupService>();
            
            // Register windows
            services.AddTransient<MainWindow>();
            services.AddTransient<SettingsWindow>();
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            try
            {
                // Create and show the main window
                var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
                mainWindow.Show();
                
                // Check if the voice assistant service is installed and running
                var startupService = _serviceProvider.GetRequiredService<IStartupService>();
                if (!startupService.IsServiceInstalled())
                {
                    MessageBox.Show(
                        "The Voice Assistant Service is not installed. Some features may not work correctly.",
                        "Service Not Installed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else if (!startupService.IsServiceRunning())
                {
                    var result = MessageBox.Show(
                        "The Voice Assistant Service is not running. Would you like to start it now?",
                        "Service Not Running",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        startupService.StartService();
                    }
                }
            }
            catch (Exception ex)
            {
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
            // Dispose services
            if (_serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
