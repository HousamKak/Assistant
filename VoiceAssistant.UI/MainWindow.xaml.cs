// VoiceAssistant.UI/MainWindow.xaml.cs
using System;
using System.Globalization;
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
        private bool _isManuallyClosing;

        public MainWindow(
            ILogger<MainWindow> logger,
            IAssistantUIService assistantUIService,
            IIpcService ipcService)
        {
            InitializeComponent();
            
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _assistantUIService = assistantUIService ?? throw new ArgumentNullException(nameof(assistantUIService));
            _ipcService = ipcService ?? throw new ArgumentNullException(nameof(ipcService));
            
            // Position the window in the bottom right corner of the screen
            PositionWindowBottomRight();
            
            // Connect to the IPC service
            ConnectToIpcService();
            
            // Subscribe to assistant state changes
            _assistantUIService.StateChanged += OnAssistantStateChanged;
            _assistantUIService.CommandProcessed += OnCommandProcessed;
        }

        private async void ConnectToIpcService()
        {
            try
            {
                _ipcService.MessageReceived += OnIpcMessageReceived;
                await _ipcService.StartAsync(false);
                
                if (_ipcService.IsConnected)
                {
                    _logger.LogInformation("Connected to voice assistant service");
                    
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
                    UpdateUIForState(new AssistantState { State = ListeningState.Idle });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error connecting to voice assistant service");
                UpdateUIForState(new AssistantState { State = ListeningState.Idle });
            }
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
                    break;
                    
                case ListeningState.WakeWordDetected:
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
            if (state.State != ListeningState.Listening && state.State != ListeningState.WakeWordDetected)
            {
                ModifyPulseAnimation(1.5);
            }
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
            var settingsWindow = (SettingsWindow)Application.Current.ServiceProvider.GetService(typeof(SettingsWindow));
            settingsWindow.ShowDialog();
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
                _ipcService.MessageReceived -= OnIpcMessageReceived;
                
                base.OnClosing(e);
            }
        }
    }
}