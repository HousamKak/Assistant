
// VoiceAssistant.UI/SettingsWindow.xaml.cs
using System;
using System.Windows;
using Microsoft.Extensions.Logging;
using VoiceAssistant.UI.Services;

namespace VoiceAssistant.UI
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly ILogger<SettingsWindow> _logger;
        private readonly IStartupService _startupService;
        private readonly IAssistantUIService _assistantUIService;

        public SettingsWindow(
            ILogger<SettingsWindow> logger,
            IStartupService startupService,
            IAssistantUIService assistantUIService)
        {
            InitializeComponent();
            
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _startupService = startupService ?? throw new ArgumentNullException(nameof(startupService));
            _assistantUIService = assistantUIService ?? throw new ArgumentNullException(nameof(assistantUIService));
            
            // Load settings
            LoadSettings();
        }

        private void LoadSettings()
        {
            try
            {
                // Check if the app starts with Windows
                StartWithWindowsCheckBox.IsChecked = _startupService.IsInStartup();
                
                // Load other settings from configuration
                MinimizeToTrayCheckBox.IsChecked = true; // Default
                SensitivitySlider.Value = 0.7; // Default
                
                // Try to load from configuration if available
                var settings = _assistantUIService.GetSettings();
                if (settings != null)
                {
                    MinimizeToTrayCheckBox.IsChecked = settings.MinimizeToTray;
                    SensitivitySlider.Value = settings.WakeWordSensitivity;
                    
                    switch (settings.DisplayMode)
                    {
                        case "AlwaysVisible":
                            DisplayModeComboBox.SelectedIndex = 0;
                            break;
                        case "ShowWhenActive":
                            DisplayModeComboBox.SelectedIndex = 1;
                            break;
                        case "MinimizeToTray":
                            DisplayModeComboBox.SelectedIndex = 2;
                            break;
                    }
                    
                    TransparencyCheckBox.IsChecked = settings.EnableTransparency;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading settings");
                MessageBox.Show("Error loading settings: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveSettings()
        {
            try
            {
                // Update startup setting
                if (StartWithWindowsCheckBox.IsChecked == true)
                {
                    _startupService.AddToStartup();
                }
                else
                {
                    _startupService.RemoveFromStartup();
                }
                
                // Create settings object
                var settings = new AssistantSettings
                {
                    MinimizeToTray = MinimizeToTrayCheckBox.IsChecked ?? true,
                    WakeWordSensitivity = SensitivitySlider.Value,
                    EnableTransparency = TransparencyCheckBox.IsChecked ?? true,
                    DisplayMode = GetDisplayModeString()
                };
                
                // Save settings
                _assistantUIService.SaveSettings(settings);
                
                // Apply settings
                _assistantUIService.ApplySettings(settings);
                
                _logger.LogInformation("Settings saved successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving settings");
                MessageBox.Show("Error saving settings: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GetDisplayModeString()
        {
            switch (DisplayModeComboBox.SelectedIndex)
            {
                case 0:
                    return "AlwaysVisible";
                case 1:
                    return "ShowWhenActive";
                case 2:
                    return "MinimizeToTray";
                default:
                    return "AlwaysVisible";
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}