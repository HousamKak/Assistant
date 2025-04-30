// VoiceAssistant.Core/Models/AudioData.cs
using System;

namespace VoiceAssistant.Core.Models
{
    /// <summary>
    /// Represents audio data captured for processing.
    /// </summary>
    public class AudioData
    {
        /// <summary>
        /// Gets or sets the raw audio bytes.
        /// </summary>
        public byte[] RawData { get; set; }
        
        /// <summary>
        /// Gets or sets the PCM audio samples.
        /// </summary>
        public short[] PcmData { get; set; }
        
        /// <summary>
        /// Gets or sets the sample rate of the audio.
        /// </summary>
        public int SampleRate { get; set; }
        
        /// <summary>
        /// Gets or sets the number of channels in the audio.
        /// </summary>
        public int Channels { get; set; }
        
        /// <summary>
        /// Gets or sets the timestamp when the audio was captured.
        /// </summary>
        public DateTime Timestamp { get; set; }
        
        /// <summary>
        /// Initializes a new instance of the AudioData class.
        /// </summary>
        /// <param name="rawData">The raw audio bytes.</param>
        /// <param name="pcmData">The PCM audio samples.</param>
        /// <param name="sampleRate">The sample rate of the audio.</param>
        /// <param name="channels">The number of channels in the audio.</param>
        public AudioData(byte[] rawData, short[] pcmData, int sampleRate, int channels)
        {
            RawData = rawData ?? throw new ArgumentNullException(nameof(rawData));
            PcmData = pcmData ?? throw new ArgumentNullException(nameof(pcmData));
            SampleRate = sampleRate > 0 ? sampleRate : throw new ArgumentOutOfRangeException(nameof(sampleRate));
            Channels = channels > 0 ? channels : throw new ArgumentOutOfRangeException(nameof(channels));
            Timestamp = DateTime.Now;
        }
    }
}