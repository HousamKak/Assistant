// VoiceAssistant.UI/Services/StartupService.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.ServiceProcess;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace VoiceAssistant.UI.Services
{
    /// <summary>
    /// Implementation of the startup service.
    /// </summary>
    public class StartupService : IStartupService
    {
        private readonly ILogger<StartupService> _logger;
        private const string ServiceName = "VoiceAssistantService";
        private const string StartupRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "VoiceAssistant";

        /// <summary>
        /// Initializes a new instance of the StartupService class.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        /// <exception cref="ArgumentNullException">Thrown when logger is null.</exception>
        public StartupService(ILogger<StartupService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        public void AddToStartup()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true);
                
                if (key == null)
                {
                    _logger.LogError("Startup registry key not found");
                    return;
                }
                
                string executablePath = Assembly.GetEntryAssembly().Location;
                
                // Replace .dll with .exe for .NET Core apps
                if (executablePath.EndsWith(".dll"))
                {
                    executablePath = executablePath.Substring(0, executablePath.Length - 4) + ".exe";
                }
                
                key.SetValue(AppName, executablePath);
                _logger.LogInformation("Added application to startup: {Path}", executablePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding application to startup");
                throw;
            }
        }

        /// <inheritdoc/>
        public void RemoveFromStartup()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true);
                
                if (key == null)
                {
                    _logger.LogError("Startup registry key not found");
                    return;
                }
                
                if (key.GetValue(AppName) != null)
                {
                    key.DeleteValue(AppName);
                    _logger.LogInformation("Removed application from startup");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing application from startup");
                throw;
            }
        }

        /// <inheritdoc/>
        public bool IsInStartup()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey);
                
                if (key == null)
                {
                    return false;
                }
                
                return key.GetValue(AppName) != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if application is in startup");
                return false;
            }
        }

        /// <inheritdoc/>
        public bool IsServiceInstalled()
        {
            try
            {
                ServiceController[] services = ServiceController.GetServices();
                foreach (var service in services)
                {
                    if (service.ServiceName.Equals(ServiceName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if service is installed");
                return false;
            }
        }

        /// <inheritdoc/>
        public bool IsServiceRunning()
        {
            try
            {
                // Method 1: Try to check via ServiceController (requires elevated permissions)
                try
                {
                    ServiceController[] services = ServiceController.GetServices();
                    foreach (var service in services)
                    {
                        if (service.ServiceName.Equals(ServiceName, StringComparison.OrdinalIgnoreCase))
                        {
                            bool isRunning = service.Status == ServiceControllerStatus.Running;
                            _logger.LogDebug("Service status via ServiceController: {Status}", service.Status);
                            return isRunning;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // If this fails (likely due to permission issues), fall back to process checking
                    _logger.LogWarning(ex, "Could not check service status via ServiceController, falling back to process detection");
                }

                // Method 2: Check if the service process is running
                var serviceProcesses = System.Diagnostics.Process.GetProcessesByName("VoiceAssistant.Service");
                if (serviceProcesses.Length > 0)
                {
                    _logger.LogInformation("Service detected via process: {ProcessId}", serviceProcesses[0].Id);
                    return true;
                }

                _logger.LogInformation("Service not detected via any method");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if service is running");
                return false;
            }
        }

        /// <inheritdoc/>
        public bool StartService()
        {
            try
            {
                if (!IsServiceInstalled())
                {
                    _logger.LogError("Cannot start service: Service is not installed");
                    return false;
                }
                
                using var serviceController = new ServiceController(ServiceName);
                
                if (serviceController.Status == ServiceControllerStatus.Running)
                {
                    _logger.LogInformation("Service is already running");
                    return true;
                }
                
                serviceController.Start();
                serviceController.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                
                _logger.LogInformation("Service started successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting service");
                return false;
            }
        }

        /// <inheritdoc/>
        public bool StopService()
        {
            try
            {
                if (!IsServiceInstalled())
                {
                    _logger.LogError("Cannot stop service: Service is not installed");
                    return false;
                }
                
                using var serviceController = new ServiceController(ServiceName);
                
                if (serviceController.Status == ServiceControllerStatus.Stopped)
                {
                    _logger.LogInformation("Service is already stopped");
                    return true;
                }
                
                serviceController.Stop();
                serviceController.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                
                _logger.LogInformation("Service stopped successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping service");
                return false;
            }
        }
    }
}