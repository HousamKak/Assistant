using Microsoft.Extensions.Logging;
using NAudio.Wave;
using VoiceAssistant.Core.Interfaces;

namespace VoiceAssistant.Core.Services
{
    /// <summary>
    /// Implementation of the audio capture service using NAudio.
    /// </summary>
    public class AudioCaptureService : IAudioCaptureService
    {
        private readonly ILogger<AudioCaptureService> _logger;
        private WaveInEvent _waveIn;
        private bool _disposed;
        private bool _isRecording;
        private CancellationTokenSource _cts;

        public event EventHandler<byte[]> AudioDataCaptured;
        public event EventHandler<short[]> PcmDataCaptured;

        public int SampleRate { get; private set; }
        public int Channels { get; private set; }

        // Now just returns our flag
        public bool IsCapturing => _isRecording;

        public AudioCaptureService(ILogger<AudioCaptureService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            SampleRate = 16000; // Default sample rate for Whisper and Porcupine
            Channels   = 1;     // Mono audio
        }

        public async Task StartCaptureAsync(CancellationToken cancellationToken = default)
        {
            if (_isRecording)
            {
                _logger.LogWarning("Attempted to start audio capture when already capturing");
                return;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                await Task.Run(() =>
                {
                    _waveIn = new WaveInEvent
                    {
                        DeviceNumber     = 0,
                        WaveFormat       = new WaveFormat(SampleRate, Channels),
                        BufferMilliseconds = 50
                    };

                    _waveIn.DataAvailable     += OnDataAvailable;
                    _waveIn.RecordingStopped  += OnRecordingStopped;
                    _waveIn.StartRecording();
                    _isRecording = true;

                    _logger.LogInformation("Audio capture started at {SampleRate}Hz, {Channels} channel(s)", 
                        SampleRate, Channels);
                }, _cts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start audio capture");
                throw;
            }
        }

        public async Task StopCaptureAsync()
        {
            if (!_isRecording)
                return;

            try
            {
                await Task.Run(() =>
                {
                    _waveIn.StopRecording();
                });

                // We'll also clear the flag here, though OnRecordingStopped will fire too
                _isRecording = false;
                _logger.LogInformation("Audio capture stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping audio capture");
                throw;
            }
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            try
            {
                var audioData = new byte[e.BytesRecorded];
                Buffer.BlockCopy(e.Buffer, 0, audioData, 0, e.BytesRecorded);

                var pcmData = new short[e.BytesRecorded / 2];
                Buffer.BlockCopy(e.Buffer, 0, pcmData, 0, e.BytesRecorded);

                AudioDataCaptured?.Invoke(this, audioData);
                PcmDataCaptured?.Invoke(this, pcmData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing audio data");
            }
        }

        private void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            _isRecording = false;  // ensure flag is cleared even on error
            if (e.Exception != null)
                _logger.LogError(e.Exception, "Audio recording stopped due to an error");
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _cts?.Cancel();
                _cts?.Dispose();

                if (_waveIn != null)
                {
                    if (_isRecording)
                        _waveIn.StopRecording();

                    _waveIn.DataAvailable    -= OnDataAvailable;
                    _waveIn.RecordingStopped -= OnRecordingStopped;
                    _waveIn.Dispose();
                    _waveIn = null;
                }
            }

            _disposed = true;
        }
    }
}
