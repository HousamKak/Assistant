// VoiceAssistant.Service/Program.cs

using Topshelf;
using VoiceAssistant.Service.Services;


namespace VoiceAssistant.Service
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // Setup TopShelf Windows Service
            var exitCode = HostFactory.Run(x =>
            {
                x.Service<ServiceManager>(service =>
                {
                    service.ConstructUsing(hostSettings => new ServiceManager());
                    service.WhenStarted(manager => manager.Start());
                    service.WhenStopped(manager => manager.Stop());
                });

                x.RunAsLocalSystem();
                x.StartAutomatically();

                x.SetDescription("Provides continuous voice assistant functionality with wake word detection");
                x.SetDisplayName("Voice Assistant Service");
                x.SetServiceName("VoiceAssistantService");

                // Handle service recovery options
                x.EnableServiceRecovery(recovery =>
                {
                    recovery.RestartService(1); // Restart after 1 minute
                    recovery.SetResetPeriod(1); // Reset failure count after 1 day
                });
            });

            Environment.ExitCode = (int)exitCode;
        }
    }
}