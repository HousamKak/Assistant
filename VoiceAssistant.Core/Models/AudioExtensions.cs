// VoiceAssistant.Core/Models/Extensions/AudioExtensions.cs
using System;
using System.IO;
using NAudio.Wave;

namespace VoiceAssistant.Core.Models.Extensions
{
    /// <summary>
    /// Extension methods for audio processing.
    /// </summary>
    public static class AudioExtensions
    {
        /// <summary>
        /// Converts raw PCM data to a WAV format byte array.
        /// </summary>
        /// <param name="pcmData">The PCM data to convert.</param>
        /// <param name="sampleRate">The sample rate of the audio.</param>
        /// <param name="channels">The number of channels in the audio.</param>
        /// <returns>A byte array containing the WAV data.</returns>
        public static byte[] ToWavFormat(this byte[] pcmData, int sampleRate, int channels)
        {
            if (pcmData == null)
                throw new ArgumentNullException(nameof(pcmData));
                
            if (sampleRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
                
            if (channels <= 0)
                throw new ArgumentOutOfRangeException(nameof(channels));
                
            using var ms = new MemoryStream();
            using (var writer = new WaveFileWriter(ms, new WaveFormat(sampleRate, 16, channels)))
            {
                writer.Write(pcmData, 0, pcmData.Length);
            }
            
            return ms.ToArray();
        }
        
        /// <summary>
        /// Resamples audio data to a new sample rate.
        /// </summary>
        /// <param name="audioData">The audio data to resample.</param>
        /// <param name="sourceFormat">The source audio format.</param>
        /// <param name="targetSampleRate">The target sample rate.</param>
        /// <returns>A byte array containing the resampled audio data.</returns>
        public static byte[] Resample(this byte[] audioData, WaveFormat sourceFormat, int targetSampleRate)
        {
            if (audioData == null)
                throw new ArgumentNullException(nameof(audioData));
                
            if (sourceFormat == null)
                throw new ArgumentNullException(nameof(sourceFormat));
                
            if (targetSampleRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(targetSampleRate));
                
            // If the source is already at the target rate, return the original data
            if (sourceFormat.SampleRate == targetSampleRate)
                return audioData;
                
            var targetFormat = new WaveFormat(targetSampleRate, sourceFormat.BitsPerSample, sourceFormat.Channels);
            
            using var sourceStream = new MemoryStream(audioData);
            using var sourceReader = new WaveFileReader(sourceStream);
            using var targetStream = new MemoryStream();
            using (var resampler = new MediaFoundationResampler(sourceReader, targetFormat))
            {
                WaveFileWriter.WriteWavFileToStream(targetStream, resampler);
            }
            
            return targetStream.ToArray();
        }
        
        /// <summary>
        /// Converts a short array of PCM samples to a byte array.
        /// </summary>
        /// <param name="pcmSamples">The PCM samples to convert.</param>
        /// <returns>A byte array containing the PCM data.</returns>
        public static byte[] ToByteArray(this short[] pcmSamples)
        {
            if (pcmSamples == null)
                throw new ArgumentNullException(nameof(pcmSamples));
                
            var bytes = new byte[pcmSamples.Length * 2];
            Buffer.BlockCopy(pcmSamples, 0, bytes, 0, bytes.Length);
            return bytes;
        }
        
        /// <summary>
        /// Calculates the RMS (Root Mean Square) volume level of PCM audio data.
        /// </summary>
        /// <param name="pcmSamples">The PCM samples to analyze.</param>
        /// <returns>The RMS volume level between 0.0 and 1.0.</returns>
        public static float CalculateRmsLevel(this short[] pcmSamples)
        {
            if (pcmSamples == null || pcmSamples.Length == 0)
                return 0;
                
            double sum = 0;
            for (int i = 0; i < pcmSamples.Length; i++)
            {
                sum += Math.Pow(pcmSamples[i], 2);
            }
            
            double rms = Math.Sqrt(sum / pcmSamples.Length);
            
            // Normalize to 0.0 - 1.0 range (16-bit PCM has values from -32768 to 32767)
            return (float)(rms / 32768.0);
        }
    }
}