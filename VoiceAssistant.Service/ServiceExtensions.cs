// VoiceAssistant.Service/ServiceExtensions.cs

using Topshelf.HostConfigurators;

namespace VoiceAssistant.Service
{
    /// <summary>
    /// Extension methods for TopShelf services.
    /// </summary>
    public static class ServiceExtensions
    {
        /// <summary>
        /// Configures Serilog logging for the TopShelf service.
        /// </summary>
        /// <param name="configurator">The HostConfigurator to configure.</param>
        /// <returns>The configured HostConfigurator.</returns>
        public static HostConfigurator UseSerilog(this HostConfigurator configurator)
        {
            if (configurator == null)
                throw new ArgumentNullException(nameof(configurator));

            // Ensure log directory exists
            string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            // Use the logger that was already configured in Program.cs
            configurator.UseSerilog();
            
            return configurator;
        }
    }
}