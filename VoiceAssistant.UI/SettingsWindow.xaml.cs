// VoiceAssistant.UI/SettingsWindow.xaml.cs
using System;
using System.Linq;
using System.Windows;
using Microsoft.Extensions.Logging;
using VoiceAssistant.UI.Services;
using VoiceAssistant.Core.Interfaces;

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
        private readonly ITextToSpeechService _textToSpeechService;

        public SettingsWindow(
            ILogger<SettingsWindow> logger,
            IStartupService startupService,
            IAssistantUIService assistantUIService,
            ITextToSpeechService textToSpeechService)
        {
            InitializeComponent();
            
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _startupService = startupService ?? throw new ArgumentNullException(nameof(startupService));
            _assistantUIService = assistantUIService ?? throw new ArgumentNullException(nameof(assistantUIService));
            _textToSpeechService = textToSpeechService ?? throw new ArgumentNullException(nameof(textToSpeechService));
            
            // Load settings
            LoadSettings();
            
            // Load available voices
            LoadVoices();
        }

        private void LoadVoices()
        {
            try
            {
                using var synthesizer = new System.Speech.Synthesis.SpeechSynthesizer();
                var voices = synthesizer.GetInstalledVoices()
                    .Where(v => v.Enabled)
                    .Select(v => v.VoiceInfo)
                    .ToList();
                
                VoiceSelectionComboBox.ItemsSource = voices;
                
                // Select the current voice if set
                var settings = _assistantUIService.GetSettings();
                if (settings != null && !string.IsNullOrEmpty(settings.TextToSpeechVoice))
                {
                    var voice = voices.FirstOrDefault(v => 
                        v.Name.Equals(settings.TextToSpeechVoice, StringComparison.OrdinalIgnoreCase));
                    
                    if (voice != null)
                    {
                        VoiceSelectionComboBox.SelectedItem = voice;
                    }
                }
                else if (voices.Any())
                {
                    // Select first voice by default
                    VoiceSelectionComboBox.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading available voices");
                MessageBox.Show("Error loading available voices: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                
                // Add TTS settings
                EnableTtsCheckBox.IsChecked = true; // Default
                ResponsePhraseTextBox.Text = "Yes, I'm listening"; // Default
                SpeechRateSlider.Value = 0; // Default
                VolumeSlider.Value = 100; // Default
                
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
                    
                    // Load TTS settings if available
                    EnableTtsCheckBox.IsChecked = settings.EnableTextToSpeech;
                    
                    if (!string.IsNullOrEmpty(settings.WakeWordResponsePhrase))
                    {
                        ResponsePhraseTextBox.Text = settings.WakeWordResponsePhrase;
                    }
                    
                    SpeechRateSlider.Value = settings.TextToSpeechRate;
                    VolumeSlider.Value = settings.TextToSpeechVolume;
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
                
                // Get selected voice
                string voiceName = "";
                if (VoiceSelectionComboBox.SelectedItem is System.Speech.Synthesis.VoiceInfo selectedVoice)
                {
                    voiceName = selectedVoice.Name;
                }
                
                // Create settings object with TTS settings
                var settings = new AssistantSettings
                {
                    MinimizeToTray = MinimizeToTrayCheckBox.IsChecked ?? true,
                    WakeWordSensitivity = SensitivitySlider.Value,
                    EnableTransparency = TransparencyCheckBox.IsChecked ?? true,
                    DisplayMode = GetDisplayModeString(),
                    EnableTextToSpeech = EnableTtsCheckBox.IsChecked ?? true,
                    WakeWordResponsePhrase = ResponsePhraseTextBox.Text,
                    TextToSpeechVoice = voiceName,
                    TextToSpeechRate = (int)SpeechRateSlider.Value,
                    TextToSpeechVolume = (int)VolumeSlider.Value
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

        private void TestSpeech_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Get current settings from UI
                string responsePhrase = ResponsePhraseTextBox.Text;
                bool enableTts = EnableTtsCheckBox.IsChecked ?? true;
                
                if (!enableTts)
                {
                    MessageBox.Show("Text-to-speech is disabled. Enable it to test speech.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                
                if (string.IsNullOrEmpty(responsePhrase))
                {
                    MessageBox.Show("Please enter a response phrase to test.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                
                // Get selected voice
                string voiceName = "";
                if (VoiceSelectionComboBox.SelectedItem is System.Speech.Synthesis.VoiceInfo selectedVoice)
                {
                    voiceName = selectedVoice.Name;
                    _textToSpeechService.SetVoice(voiceName);
                }
                
                // Set rate and volume
                _textToSpeechService.SetRate((int)SpeechRateSlider.Value);
                _textToSpeechService.SetVolume((int)VolumeSlider.Value);
                
                // Speak the test phrase
                _textToSpeechService.SpeakAsync(responsePhrase);
                
                _logger.LogInformation("Test speech initiated: \"{ResponsePhrase}\"", responsePhrase);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing speech");
                MessageBox.Show("Error testing speech: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}