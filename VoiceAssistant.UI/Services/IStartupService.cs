
// VoiceAssistant.UI/Services/IStartupService.cs
namespace VoiceAssistant.UI.Services
{
    /// <summary>
    /// Interface for startup-related services.
    /// </summary>
    public interface IStartupService
    {
        /// <summary>
        /// Adds the application to Windows startup.
        /// </summary>
        void AddToStartup();
        
        /// <summary>
        /// Removes the application from Windows startup.
        /// </summary>
        void RemoveFromStartup();
        
        /// <summary>
        /// Checks if the application is in Windows startup.
        /// </summary>
        /// <returns>True if the application is in Windows startup.</returns>
        bool IsInStartup();
        
        /// <summary>
        /// Checks if the voice assistant service is installed.
        /// </summary>
        /// <returns>True if the service is installed.</returns>
        bool IsServiceInstalled();
        
        /// <summary>
        /// Checks if the voice assistant service is running.
        /// </summary>
        /// <returns>True if the service is running.</returns>
        bool IsServiceRunning();
        
        /// <summary>
        /// Starts the voice assistant service.
        /// </summary>
        /// <returns>True if the service was started successfully.</returns>
        bool StartService();
        
        /// <summary>
        /// Stops the voice assistant service.
        /// </summary>
        /// <returns>True if the service was stopped successfully.</returns>
        bool StopService();
    }
}