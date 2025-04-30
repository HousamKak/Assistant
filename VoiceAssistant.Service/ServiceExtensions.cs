
using Serilog;
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

            // Configure Serilog
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File("logs/voice-assistant-.log", rollingInterval: Serilog.RollingInterval.Day)
                .WriteTo.Console()
                .CreateLogger();
            
            return configurator;
        }
    }
}