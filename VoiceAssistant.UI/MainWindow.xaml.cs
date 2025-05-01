// VoiceAssistant.UI/MainWindow.xaml.cs
// This includes the full updated class with console logging and fixed nullability warnings

using System;
using System.Globalization;
using System.Media;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MaterialDesignThemes.Wpf;
using Microsoft.Extensions.Logging;
using VoiceAssistant.Core.Interfaces;
using VoiceAssistant.Core.Models;
using VoiceAssistant.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace VoiceAssistant.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ILogger<MainWindow> _logger;
        private readonly IAssistantUIService _assistantUIService;
        private readonly IIpcService _ipcService;
        private readonly IStartupService _startupService;
        private bool _isManuallyClosing;
        private bool _isServiceConnected;
        private DateTime _lastConnectionAttempt = DateTime.MinValue;
        private readonly TimeSpan _connectionRetryInterval = TimeSpan.FromSeconds(10);
        private readonly Storyboard _wakeWordDetectedStoryboard;
        private readonly Storyboard _enhancedPulseStoryboard;

        public MainWindow(
            ILogger<MainWindow> logger,
            IAssistantUIService assistantUIService,
            IIpcService ipcService,
            IStartupService startupService)
        {
            InitializeComponent();
            
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _assistantUIService = assistantUIService ?? throw new ArgumentNullException(nameof(assistantUIService));
            _ipcService = ipcService ?? throw new ArgumentNullException(nameof(ipcService));
            _startupService = startupService ?? throw new ArgumentNullException(nameof(startupService));
            
            Console.WriteLine("MainWindow initializing");
            _logger.LogInformation("MainWindow initializing");
            
            // Position the window in the bottom right corner of the screen
            PositionWindowBottomRight();
            
            // Get animations from resources
            _wakeWordDetectedStoryboard = (Storyboard)FindResource("WakeWordDetectedStoryboard");
            _enhancedPulseStoryboard = (Storyboard)FindResource("EnhancedPulseStoryboard");
            
            // Connect to the IPC service
            Console.WriteLine("Initial connection to IPC service");
            _logger.LogInformation("Initial connection to IPC service");
            ConnectToIpcService();
            
            // Subscribe to assistant state changes
            _assistantUIService.StateChanged += OnAssistantStateChanged;
            _assistantUIService.CommandProcessed += OnCommandProcessed;
            
            // Set up connection status check timer
            var connectionTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            connectionTimer.Tick += ConnectionTimer_Tick;
            connectionTimer.Start();
            
            // Update connection status initially
            UpdateConnectionStatus();
            
            // Log detailed information
            LogConnectionStatus();
            
            Console.WriteLine("MainWindow initialized successfully");
            _logger.LogInformation("MainWindow initialized successfully");
        }

        private void ConnectionTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                // Log connection status every 30 seconds
                if (DateTime.Now.Second % 30 == 0)
                {
                    LogConnectionStatus();
                }
                
                // Check if service is running
                bool isServiceRunning = _startupService.IsServiceRunning();
                
                if (!isServiceRunning)
                {
                    _logger.LogWarning("Voice Assistant service is not running");
                    Console.WriteLine("WARNING: Voice Assistant service is not running");
                    UpdateConnectionStatus(false, "Service not running");
                    _isServiceConnected = false;
                }
                else if (!_isServiceConnected && DateTime.Now - _lastConnectionAttempt > _connectionRetryInterval)
                {
                    // Try to reconnect if service is running but we're not connected
                    Console.WriteLine("Attempting to reconnect to Voice Assistant service");
                    _logger.LogInformation("Attempting to reconnect to Voice Assistant service");
                    ConnectToIpcService();
                }
                else if (_isServiceConnected && !_ipcService.IsConnected)
                {
                    // We thought we were connected, but we're not anymore
                    Console.WriteLine("WARNING: IPC connection lost");
                    _logger.LogWarning("IPC connection lost");
                    _isServiceConnected = false;
                    UpdateConnectionStatus(false, "Connection lost");
                    
                    // Try to reconnect
                    if (DateTime.Now - _lastConnectionAttempt > _connectionRetryInterval)
                    {
                        Console.WriteLine("Attempting to reconnect after connection loss");
                        _logger.LogInformation("Attempting to reconnect after connection loss");
                        ConnectToIpcService();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR checking connection status: {ex.Message}");
                _logger.LogError(ex, "Error checking connection status");
            }
        }

        private void LogConnectionStatus()
        {
            try
            {
                // Create a detailed connection status report
                Console.WriteLine("==== CONNECTION STATUS REPORT ====");
                Console.WriteLine($"Is service installed: {_startupService.IsServiceInstalled()}");
                Console.WriteLine($"Is service running: {_startupService.IsServiceRunning()}");
                Console.WriteLine($"Is UI connected to service: {_isServiceConnected}");
                Console.WriteLine($"Is IPC service connected: {_ipcService.IsConnected}");
                Console.WriteLine($"Time since last connection attempt: {DateTime.Now - _lastConnectionAttempt}");
                Console.WriteLine("================================");
                
                _logger.LogInformation("==== CONNECTION STATUS REPORT ====");
                _logger.LogInformation("Is service installed: {IsInstalled}", _startupService.IsServiceInstalled());
                _logger.LogInformation("Is service running: {IsRunning}", _startupService.IsServiceRunning());
                _logger.LogInformation("Is UI connected to service: {IsConnected}", _isServiceConnected);
                _logger.LogInformation("Is IPC service connected: {IsConnected}", _ipcService.IsConnected);
                _logger.LogInformation("Time since last connection attempt: {TimeSinceLastAttempt}", 
                    DateTime.Now - _lastConnectionAttempt);
                _logger.LogInformation("================================");
                
                // Try to get service process information
                try
                {
                    var serviceProcesses = System.Diagnostics.Process.GetProcessesByName("VoiceAssistant.Service");
                    Console.WriteLine($"Found {serviceProcesses.Length} service processes");
                    _logger.LogInformation("Found {Count} service processes", serviceProcesses.Length);
                    
                    foreach (var process in serviceProcesses)
                    {
                        Console.WriteLine($"Service process ID: {process.Id}, Start time: {process.StartTime}, Memory: {process.WorkingSet64 / 1024}KB");
                        _logger.LogInformation("Service process ID: {Id}, Start time: {StartTime}, Memory: {Memory}KB",
                            process.Id, process.StartTime, process.WorkingSet64 / 1024);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"WARNING: Could not get service process information: {ex.Message}");
                    _logger.LogWarning(ex, "Could not get service process information");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR logging connection status: {ex.Message}");
                _logger.LogError(ex, "Error logging connection status");
            }
        }

        private async void ConnectToIpcService()
        {
            try
            {
                _lastConnectionAttempt = DateTime.Now;
                UpdateConnectionStatus(false, "Connecting...");
                
                Console.WriteLine("Beginning connection process to IPC service");
                _logger.LogInformation("Beginning connection process to IPC service");
                
                bool serviceRunning = _startupService.IsServiceRunning();
                
                // First attempt to connect even if we think the service isn't running
                // The service process might be running but not properly detected
                if (!serviceRunning)
                {
                    Console.WriteLine("Service not detected as running, but attempting connection anyway");
                    _logger.LogWarning("Service not detected as running, but attempting connection anyway");
                }
                
                // Disconnect first if already connected
                if (_ipcService.IsConnected)
                {
                    Console.WriteLine("Disconnecting existing IPC connection before reconnecting");
                    _logger.LogInformation("Disconnecting existing IPC connection before reconnecting");
                    await _ipcService.StopAsync();
                }
                
                // Remove any existing event handlers to avoid duplicates
                _ipcService.MessageReceived -= OnIpcMessageReceived;
                // Add our event handler
                _ipcService.MessageReceived += OnIpcMessageReceived;
                
                Console.WriteLine("Starting IPC client connection to service");
                _logger.LogInformation("Starting IPC client connection to service");
                
                // Connect to the service regardless of service detection result
                await _ipcService.StartAsync(false);
                
                if (_ipcService.IsConnected)
                {
                    Console.WriteLine("SUCCESS: Connected to voice assistant service");
                    _logger.LogInformation("Connected to voice assistant service");
                    _isServiceConnected = true;
                    UpdateConnectionStatus(true, "Connected");
                    
                    // Request current state
                    Console.WriteLine("Requesting current state from service");
                    _logger.LogInformation("Requesting current state from service");
                    await _ipcService.SendMessageAsync(new IpcMessage
                    {
                        Type = MessageType.ServiceStatus,
                        Content = "GetState"
                    });
                }
                else
                {
                    Console.WriteLine("WARNING: Failed to connect to voice assistant service");
                    _logger.LogWarning("Failed to connect to voice assistant service");
                    _isServiceConnected = false;
                    
                    // Only try to start the service if the connection failed and service isn't running
                    if (!serviceRunning)
                    {
                        // Try to start the service if not running
                        Console.WriteLine("Attempting to start service since connection failed");
                        _logger.LogWarning("Attempting to start service since connection failed");
                        
                        bool started = _startupService.StartService();
                        if (started)
                        {
                            Console.WriteLine("Service started successfully. Waiting for it to initialize...");
                            _logger.LogInformation("Service started successfully. Waiting for it to initialize...");
                            
                            // Wait for service to start
                            await Task.Delay(5000);
                            
                            // Try connecting again
                            Console.WriteLine("Attempting connection after service start");
                            _logger.LogInformation("Attempting connection after service start");
                            await _ipcService.StartAsync(false);
                            
                            if (_ipcService.IsConnected)
                            {
                                Console.WriteLine("SUCCESS: Connected to voice assistant service after restart");
                                _logger.LogInformation("Connected to voice assistant service after restart");
                                _isServiceConnected = true;
                                UpdateConnectionStatus(true, "Connected");
                                
                                // Request current state
                                await _ipcService.SendMessageAsync(new IpcMessage
                                {
                                    Type = MessageType.ServiceStatus,
                                    Content = "GetState"
                                });
                                
                                return;
                            }
                        }
                        else
                        {
                            Console.WriteLine("ERROR: Failed to start Voice Assistant service");
                            _logger.LogError("Failed to start Voice Assistant service");
                        }
                    }
                    
                    UpdateConnectionStatus(false, "Connection failed");
                    UpdateUIForState(new AssistantState { State = ListeningState.Idle });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR connecting to voice assistant service: {ex.Message}");
                _logger.LogError(ex, "Error connecting to voice assistant service");
                _isServiceConnected = false;
                UpdateConnectionStatus(false, "Connection error");
                UpdateUIForState(new AssistantState { State = ListeningState.Idle });
            }
        }

        private void UpdateConnectionStatus(bool connected = false, string statusText = "Disconnected")
        {
            Dispatcher.Invoke(() =>
            {
                ConnectionStatus.Visibility = Visibility.Visible;
                ConnectionStatusText.Text = statusText;
                
                if (connected)
                {
                    ConnectionStatus.Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
                    Console.WriteLine($"Connection status updated: Connected ({statusText})");
                    _logger.LogInformation("Connection status updated: Connected ({StatusText})", statusText);
                }
                else
                {
                    ConnectionStatus.Background = new SolidColorBrush(Color.FromRgb(229, 115, 115)); // Red
                    Console.WriteLine($"Connection status updated: Disconnected ({statusText})");
                    _logger.LogInformation("Connection status updated: Disconnected ({StatusText})", statusText);
                }
                
                // Auto-hide after 5 seconds if connected
                if (connected)
                {
                    Task.Delay(5000).ContinueWith(_ =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            ConnectionStatus.Visibility = Visibility.Collapsed;
                        });
                    });
                }
            });
        }

        // Fixed nullability warning with object? instead of object
        private void OnIpcMessageReceived(object? sender, IpcMessage message)
        {
            Console.WriteLine($"Received IPC message of type {message.Type}");
            _logger.LogDebug("Received IPC message of type {MessageType}", message.Type);
            
            // If this is the first message after reconnection, force show wake word detection
            if (!_isServiceConnected)
            {
                _isServiceConnected = true;
                UpdateConnectionStatus(true, "Connected");
                Console.WriteLine("Connection established - received first IPC message");
                _logger.LogInformation("Connection established - received first IPC message");
                
                // Show wake word detection visual as a test/confirmation of connection
                ForceShowWakeWordDetection();
            }
            
            try
            {
                switch (message.Type)
                {
                    case MessageType.StateChange:
                        var state = JsonSerializer.Deserialize<AssistantState>(message.Content);
                        if (state != null)
                        {
                            Console.WriteLine($"Received state change to {state.State}");
                            _logger.LogInformation("Received state change to {State}", state.State);
                            Dispatcher.Invoke(() => UpdateUIForState(state));
                            
                            // If state is WakeWordDetected, make sure UI shows it
                            if (state.State == ListeningState.WakeWordDetected)
                            {
                                Console.WriteLine("Wake word detection state received from service");
                                _logger.LogInformation("Wake word detection state received from service");
                                ShowWakeWordDetected();
                            }
                        }
                        break;
                        
                    case MessageType.Command:
                        var result = JsonSerializer.Deserialize<CommandResult>(message.Content);
                        if (result != null)
                        {
                            Console.WriteLine($"Received command result: {result.Success} - {result.Message}");
                            _logger.LogInformation("Received command result: {Success} - {Message}", 
                                result.Success, result.Message);
                            Dispatcher.Invoke(() => ShowCommandResult(result));
                        }
                        break;
                        
                    default:
                        Console.WriteLine($"Received unhandled message type: {message.Type}");
                        _logger.LogDebug("Received unhandled message type: {MessageType}", message.Type);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR handling IPC message: {ex.Message}");
                _logger.LogError(ex, "Error handling IPC message");
            }
        }

        // Also need to fix nullability for these event handlers
        private void OnAssistantStateChanged(object? sender, AssistantState state)
        {
            Console.WriteLine($"AssistantUIService state changed to {state.State}");
            _logger.LogInformation("AssistantUIService state changed to {State}", state.State);
            Dispatcher.Invoke(() => UpdateUIForState(state));
        }

        private void OnCommandProcessed(object? sender, CommandResult result)
        {
            Console.WriteLine($"AssistantUIService command processed: {result.Success} - {result.Message}");
            _logger.LogInformation("AssistantUIService command processed: {Success} - {Message}",
                result.Success, result.Message);
            Dispatcher.Invoke(() => ShowCommandResult(result));
        }

        private void UpdateUIForState(AssistantState state)
        {
            Console.WriteLine($"Updating UI for state {state.State}");
            _logger.LogDebug("Updating UI for state {State}", state.State);
            
            switch (state.State)
            {
                case ListeningState.Idle:
                    PulseEllipse.Fill = new SolidColorBrush(Color.FromRgb(158, 158, 158)); // Gray
                    StatusIcon.Kind = PackIconKind.MicrophoneOff;
                    break;
                    
                case ListeningState.WaitingForWakeWord:
                    PulseEllipse.Fill = new RadialGradientBrush(
                        new GradientStopCollection
                        {
                            new GradientStop(Color.FromRgb(25, 118, 210), 0.0), // Blue
                            new GradientStop(Color.FromRgb(13, 71, 161), 1.0)
                        });
                    StatusIcon.Kind = PackIconKind.Microphone;
                    ModifyPulseAnimation(1.5); // Normal pulse speed
                    break;
                    
                case ListeningState.WakeWordDetected:
                    Console.WriteLine("UI showing wake word detected state");
                    _logger.LogInformation("UI showing wake word detected state");
                    PulseEllipse.Fill = new RadialGradientBrush(
                        new GradientStopCollection
                        {
                            new GradientStop(Color.FromRgb(76, 175, 80), 0.0), // Green
                            new GradientStop(Color.FromRgb(27, 94, 32), 1.0)
                        });
                    StatusIcon.Kind = PackIconKind.RecordRec;
                    
                    // Show wake word detected notification
                    ShowWakeWordDetected();
                    break;
                    
                case ListeningState.Listening:
                    PulseEllipse.Fill = new RadialGradientBrush(
                        new GradientStopCollection
                        {
                            new GradientStop(Color.FromRgb(76, 175, 80), 0.0), // Green
                            new GradientStop(Color.FromRgb(27, 94, 32), 1.0)
                        });
                    StatusIcon.Kind = PackIconKind.RecordRec;
                    
                    // Play pulse animation faster
                    ModifyPulseAnimation(0.8);
                    break;
                    
                case ListeningState.Processing:
                    PulseEllipse.Fill = new RadialGradientBrush(
                        new GradientStopCollection
                        {
                            new GradientStop(Color.FromRgb(255, 193, 7), 0.0), // Amber
                            new GradientStop(Color.FromRgb(255, 111, 0), 1.0)
                        });
                    StatusIcon.Kind = PackIconKind.Cogs;
                    break;
                    
                case ListeningState.Responding:
                    PulseEllipse.Fill = new RadialGradientBrush(
                        new GradientStopCollection
                        {
                            new GradientStop(Color.FromRgb(156, 39, 176), 0.0), // Purple
                            new GradientStop(Color.FromRgb(74, 20, 140), 1.0)
                        });
                    StatusIcon.Kind = PackIconKind.CommentText;
                    break;
            }
            
            // Reset pulse animation speed if not listening
            if (state.State != ListeningState.Listening)
            {
                ModifyPulseAnimation(1.5);
            }
            
            // Make sure window is visible when state changes to active states
            if (state.State == ListeningState.WakeWordDetected || 
                state.State == ListeningState.Listening ||
                state.State == ListeningState.Processing ||
                state.State == ListeningState.Responding)
            {
                EnsureWindowVisible();
            }
        }

        private void ShowWakeWordDetected()
        {
            Console.WriteLine("!!! WAKE WORD DETECTED !!!");
            _logger.LogInformation("Showing wake word detected notification");
            
            // Make sure window is visible
            EnsureWindowVisible();
            
            // Show wake word notification with animation
            _wakeWordDetectedStoryboard.Stop();
            WakeWordNotification.Opacity = 1.0;
            WakeWordNotification.Visibility = Visibility.Visible;
            _wakeWordDetectedStoryboard.Begin();
            
            // Play enhanced pulse animation
            _enhancedPulseStoryboard.Stop();
            _enhancedPulseStoryboard.Begin();
            
            // Try to play a notification sound
            try
            {
                SystemSounds.Asterisk.Play();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WARNING: Could not play notification sound: {ex.Message}");
                _logger.LogWarning(ex, "Could not play notification sound");
            }
        }

        private void ForceShowWakeWordDetection()
        {
            // This is a fallback method to ensure wake word detection is visible
            // It will be called directly when the IPC connection is restored
            
            Dispatcher.Invoke(() =>
            {
                Console.WriteLine("Forcing wake word detection display");
                _logger.LogInformation("Forcing wake word detection display");
                
                // Show the wake word notification
                WakeWordNotification.Opacity = 1.0;
                WakeWordNotification.Visibility = Visibility.Visible;
                
                // Change UI to wake word detected state
                PulseEllipse.Fill = new RadialGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromRgb(76, 175, 80), 0.0), // Green
                        new GradientStop(Color.FromRgb(27, 94, 32), 1.0)
                    });
                StatusIcon.Kind = PackIconKind.RecordRec;
                
                // Make sure window is visible
                EnsureWindowVisible();
                
                // Play the enhanced pulse animation
                _enhancedPulseStoryboard.Stop();
                _enhancedPulseStoryboard.Begin();
                
                // Create a timer to hide the notification after 3 seconds
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3)
                };
                
                timer.Tick += (s, e) =>
                {
                    var hideAnimation = new DoubleAnimation
                    {
                        From = 1.0,
                        To = 0.0,
                        Duration = TimeSpan.FromMilliseconds(500)
                    };
                    
                    hideAnimation.Completed += (_, __) => WakeWordNotification.Visibility = Visibility.Collapsed;
                    WakeWordNotification.BeginAnimation(OpacityProperty, hideAnimation);
                    
                    // Stop the timer
                    ((System.Windows.Threading.DispatcherTimer)s).Stop();
                };
                
                timer.Start();
                
                // Try to play a notification sound
                try
                {
                    SystemSounds.Asterisk.Play();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"WARNING: Could not play notification sound: {ex.Message}");
                    _logger.LogWarning(ex, "Could not play notification sound");
                }
            });
        }

        private void EnsureWindowVisible()
        {
            if (!IsVisible)
            {
                Console.WriteLine("Making window visible");
                _logger.LogDebug("Making window visible");
                Show();
            }
            
            if (WindowState == WindowState.Minimized)
            {
                Console.WriteLine("Restoring minimized window");
                _logger.LogDebug("Restoring minimized window");
                WindowState = WindowState.Normal;
            }
            
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        private void ShowCommandResult(CommandResult result)
        {
            Console.WriteLine($"Showing command result: {result.Success} - {result.Message}");
            _logger.LogDebug("Showing command result: {Success} - {Message}", result.Success, result.Message);
            
            // Update command bubble
            CommandText.Text = result.Message;
            
            // Change background color based on success
            CommandBubble.Background = new SolidColorBrush(
                result.Success ? Color.FromRgb(40, 53, 147) : Color.FromRgb(183, 28, 28));
                
            // Show the command bubble with animation
            CommandBubble.Visibility = Visibility.Visible;
            
            // Animate the command bubble
            var animation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(300)
            };
            
            CommandBubble.BeginAnimation(OpacityProperty, animation);
            
            // Auto-hide after 5 seconds
            Task.Delay(5000).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    var hideAnimation = new DoubleAnimation
                    {
                        From = 1,
                        To = 0,
                        Duration = TimeSpan.FromMilliseconds(300)
                    };
                    
                    hideAnimation.Completed += (s, e) => CommandBubble.Visibility = Visibility.Collapsed;
                    CommandBubble.BeginAnimation(OpacityProperty, hideAnimation);
                });
            });
        }

        private void ModifyPulseAnimation(double durationInSeconds)
        {
            var storyboard = (Storyboard)FindResource("PulseStoryboard");
            if (storyboard != null)
            {
                foreach (var animation in storyboard.Children)
                {
                    animation.Duration = TimeSpan.FromSeconds(durationInSeconds);
                }
            }
        }

        private void PositionWindowBottomRight()
        {
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            
            Left = screenWidth - Width - 20;
            Top = screenHeight - Height - 50;
        }

        private void Window_LocationChanged(object sender, EventArgs e)
        {
            // Ensure the window stays in the visible screen area
            if (Left < 0)
                Left = 0;
            
            if (Top < 0)
                Top = 0;
                
            if (Left + Width > SystemParameters.PrimaryScreenWidth)
                Left = SystemParameters.PrimaryScreenWidth - Width;
                
            if (Top + Height > SystemParameters.PrimaryScreenHeight)
                Top = SystemParameters.PrimaryScreenHeight - Height;
        }

        /// <summary>
        /// Handle double-click on the main window
        /// This gives a way to manually trigger the wake word detection for testing
        /// </summary>
        protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            
            // Only trigger if connected
            if (_isServiceConnected)
            {
                Console.WriteLine("Manual wake word trigger via double-click");
                _logger.LogInformation("Manual wake word trigger via double-click");
                
                try
                {
                    // Manually trigger listening mode
                    _assistantUIService.TriggerListening();
                    
                    // For immediate visual feedback while we wait for the service to respond
                    ShowWakeWordDetected();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR triggering wake word manually: {ex.Message}");
                    _logger.LogError(ex, "Error triggering wake word manually");
                }
            }
            else
            {
                Console.WriteLine("WARNING: Cannot trigger wake word - service not connected");
                _logger.LogWarning("Cannot trigger wake word: service not connected");
                UpdateConnectionStatus(false, "Not connected");
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Allow dragging the window
            DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Minimize to tray instead of closing
            Hide();
        }

        private void TrayIcon_TrayLeftMouseDown(object sender, RoutedEventArgs e)
        {
            // Show the window when tray icon is clicked
            Show();
            Activate();
        }

        private void ShowWindow_Click(object sender, RoutedEventArgs e)
        {
            Show();
            Activate();
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            // Show settings window
            var settingsWindow = ((App)Application.Current).ServiceProvider.GetRequiredService<SettingsWindow>();
            settingsWindow.ShowDialog();
        }
        
        private async void RestartService_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Console.WriteLine("Restarting Voice Assistant service");
                _logger.LogInformation("Restarting Voice Assistant service");
                UpdateConnectionStatus(false, "Restarting service...");
                
                // Disconnect first
                if (_ipcService.IsConnected)
                {
                    Console.WriteLine("Stopping IPC connection before service restart");
                    _logger.LogInformation("Stopping IPC connection before service restart");
                    await _ipcService.StopAsync();
                }
                _isServiceConnected = false;
                
                if (_startupService.IsServiceRunning())
                {
                    Console.WriteLine("Stopping Voice Assistant service");
                    _logger.LogInformation("Stopping Voice Assistant service");
                    bool stopped = _startupService.StopService();
                    
                    if (!stopped)
                    {
                        Console.WriteLine("ERROR: Failed to stop Voice Assistant service");
                        _logger.LogError("Failed to stop Voice Assistant service");
                        UpdateConnectionStatus(false, "Failed to stop service");
                        return;
                    }
                    
                    // Give it time to stop
                    await Task.Delay(2000);
                }
                
                Console.WriteLine("Starting Voice Assistant service");
                _logger.LogInformation("Starting Voice Assistant service");
                bool started = _startupService.StartService();
                
                if (started)
                {
                    Console.WriteLine("Service started successfully. Waiting for initialization...");
                    _logger.LogInformation("Service started successfully. Waiting for initialization...");
                    UpdateConnectionStatus(false, "Service starting...");
                    
                    // Wait for service to initialize
                    await Task.Delay(5000);
                    
                    // Reconnect to the service
                    Console.WriteLine("Connecting to restarted service");
                    _logger.LogInformation("Connecting to restarted service");
                    ConnectToIpcService();
                }
                else
                {
                    Console.WriteLine("ERROR: Failed to start Voice Assistant service");
                    _logger.LogError("Failed to start Voice Assistant service");
                    UpdateConnectionStatus(false, "Failed to start service");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR restarting service: {ex.Message}");
                _logger.LogError(ex, "Error restarting service");
                UpdateConnectionStatus(false, "Restart error");
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            _isManuallyClosing = true;
            Application.Current.Shutdown();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_isManuallyClosing)
            {
                e.Cancel = true;
                Hide();
            }
            else
            {
                Console.WriteLine("Closing application");
                _logger.LogInformation("Closing application");
                
                // Clean up
                _assistantUIService.StateChanged -= OnAssistantStateChanged;
                _assistantUIService.CommandProcessed -= OnCommandProcessed;

                if (_ipcService != null)
                {
                    _ipcService.MessageReceived -= OnIpcMessageReceived;
                    Task.Run(async () => await _ipcService.StopAsync()).Wait();
                }

                base.OnClosing(e);
            }
        }
    }
}