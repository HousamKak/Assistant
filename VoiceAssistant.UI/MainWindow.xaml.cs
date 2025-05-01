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
            
            // Position the window in the bottom right corner of the screen
            PositionWindowBottomRight();
            
            // Get animations from resources
            _wakeWordDetectedStoryboard = (Storyboard)FindResource("WakeWordDetectedStoryboard");
            _enhancedPulseStoryboard = (Storyboard)FindResource("EnhancedPulseStoryboard");
            
            // Connect to the IPC service
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
        }

        private void ConnectionTimer_Tick(object sender, EventArgs e)
        {
            // Check if service is running
            bool isServiceRunning = _startupService.IsServiceRunning();
            
            if (!isServiceRunning)
            {
                _logger.LogWarning("Voice Assistant service is not running");
                UpdateConnectionStatus(false, "Service not running");
            }
            else if (!_isServiceConnected && DateTime.Now - _lastConnectionAttempt > _connectionRetryInterval)
            {
                // Try to reconnect if service is running but we're not connected
                _logger.LogInformation("Attempting to reconnect to Voice Assistant service");
                ConnectToIpcService();
            }
        }

        private async void ConnectToIpcService()
        {
            try
            {
                _lastConnectionAttempt = DateTime.Now;
                UpdateConnectionStatus(false, "Connecting...");
                
                _ipcService.MessageReceived += OnIpcMessageReceived;
                await _ipcService.StartAsync(false);
                
                if (_ipcService.IsConnected)
                {
                    _logger.LogInformation("Connected to voice assistant service");
                    _isServiceConnected = true;
                    UpdateConnectionStatus(true, "Connected");
                    
                    // Request current state
                    await _ipcService.SendMessageAsync(new IpcMessage
                    {
                        Type = MessageType.ServiceStatus,
                        Content = "GetState"
                    });
                }
                else
                {
                    _logger.LogWarning("Failed to connect to voice assistant service");
                    _isServiceConnected = false;
                    UpdateConnectionStatus(false, "Connection failed");
                    UpdateUIForState(new AssistantState { State = ListeningState.Idle });
                }
            }
            catch (Exception ex)
            {
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
                }
                else
                {
                    ConnectionStatus.Background = new SolidColorBrush(Color.FromRgb(229, 115, 115)); // Red
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

        private void OnIpcMessageReceived(object sender, IpcMessage message)
        {
            _logger.LogDebug("Received IPC message of type {MessageType}", message.Type);
            
            try
            {
                switch (message.Type)
                {
                    case MessageType.StateChange:
                        var state = JsonSerializer.Deserialize<AssistantState>(message.Content);
                        if (state != null)
                        {
                            Dispatcher.Invoke(() => UpdateUIForState(state));
                        }
                        break;
                        
                    case MessageType.Command:
                        var result = JsonSerializer.Deserialize<CommandResult>(message.Content);
                        if (result != null)
                        {
                            Dispatcher.Invoke(() => ShowCommandResult(result));
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling IPC message");
            }
        }

        private void OnAssistantStateChanged(object sender, AssistantState state)
        {
            Dispatcher.Invoke(() => UpdateUIForState(state));
        }

        private void OnCommandProcessed(object sender, CommandResult result)
        {
            Dispatcher.Invoke(() => ShowCommandResult(result));
        }

        private void UpdateUIForState(AssistantState state)
        {
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
            // Make sure window is visible
            EnsureWindowVisible();
            
            // Show wake word notification with animation
            _wakeWordDetectedStoryboard.Stop();
            WakeWordNotification.Opacity = 1.0;
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
                _logger.LogWarning(ex, "Could not play notification sound");
            }
        }

        private void EnsureWindowVisible()
        {
            if (!IsVisible)
            {
                Show();
            }
            
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        private void ShowCommandResult(CommandResult result)
        {
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
        
        private void RestartService_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateConnectionStatus(false, "Restarting service...");
                
                if (_startupService.IsServiceRunning())
                {
                    _startupService.StopService();
                }
                
                Task.Delay(2000).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        bool started = _startupService.StartService();
                        
                        if (started)
                        {
                            _logger.LogInformation("Service restarted successfully");
                            UpdateConnectionStatus(false, "Service restarting...");
                            
                            // Reconnect after a short delay
                            Task.Delay(3000).ContinueWith(__ =>
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    ConnectToIpcService();
                                });
                            });
                        }
                        else
                        {
                            _logger.LogError("Failed to restart service");
                            UpdateConnectionStatus(false, "Restart failed");
                        }
                    });
                });
            }
            catch (Exception ex)
            {
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