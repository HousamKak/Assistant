// VoiceAssistant.Service/Program.cs

using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Serilog.Events;
using Topshelf;
using VoiceAssistant.Service.Services;

namespace VoiceAssistant.Service
{
    public class Program
    {
        private static CancellationTokenSource _cts = new CancellationTokenSource();
        private static ServiceManager? _serviceManager; // Make nullable to avoid initialization error
        private static bool _isConsoleMode = false;

        public static void Main(string[] args)
        {
            // Configure logging first
            ConfigureSerilog();

            // Check if running in console mode
            _isConsoleMode = Array.Exists(args, arg => arg.Equals("--console", StringComparison.OrdinalIgnoreCase));

            if (_isConsoleMode)
            {
                RunAsConsole();
            }
            else
            {
                RunAsService();
            }
        }

        private static void ConfigureSerilog()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File("logs/voice-assistant-.log", 
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Log.Information("Logging configured. Starting Voice Assistant...");
        }

        private static void RunAsConsole()
        {
            try
            {
                Console.Title = "Voice Assistant Service (Console Mode)";
                Console.CancelKeyPress += Console_CancelKeyPress;
                
                Log.Information("Starting Voice Assistant in console mode. Press Ctrl+C to exit.");
                
                _serviceManager = new ServiceManager();
                bool startResult = _serviceManager.Start();
                
                if (startResult)
                {
                    Log.Information("Voice Assistant started successfully in console mode");
                    Log.Information("Press Ctrl+C to stop the service...");
                    
                    // Keep the console application running
                    Task.Delay(-1, _cts.Token).Wait();
                }
                else
                {
                    Log.Error("Failed to start Voice Assistant in console mode");
                }
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Fatal error in console mode");
            }
            finally
            {
                if (_serviceManager != null)
                {
                    Log.Information("Stopping Voice Assistant...");
                    _serviceManager.Stop();
                    Log.Information("Voice Assistant stopped successfully");
                }
                
                Log.CloseAndFlush();
            }
        }

        private static void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e) // Fix nullability mismatch
        {
            e.Cancel = true;
            _cts.Cancel();
            Log.Information("Shutdown requested. Stopping Voice Assistant...");
        }

        private static void RunAsService()
        {
            try
            {
                Log.Information("Configuring Voice Assistant as a Windows Service");

                var exitCode = HostFactory.Run(x =>
                {
                    // Use our Serilog logger
                    x.ConfigureSerilog();

                    x.Service<ServiceManager>(service =>
                    {
                        service.ConstructUsing(() => new ServiceManager()); // Correct usage of ConstructUsing
                        service.WhenStarted(manager =>
                        {
                            Log.Information("Service start requested");
                            manager.Start(); // Remove return value
                        });
                        service.WhenStopped(manager =>
                        {
                            Log.Information("Service stop requested");
                            manager.Stop(); // Remove return value
                        });
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

                    // Log TopShelf lifecycle events
                    x.OnException(ex =>
                    {
                        Log.Error(ex, "Service error: {ErrorMessage}", ex.Message);
                    });
                });

                Log.Information("Service exited with code: {ExitCode}", exitCode);
                Environment.ExitCode = (int)exitCode;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Fatal error in service mode");
                Environment.ExitCode = 1;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}