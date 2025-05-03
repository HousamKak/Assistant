// VoiceAssistant.Core/Services/WhisperCppSpeechRecognitionService.cs

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using VoiceAssistant.Core.Interfaces;
using VoiceAssistant.Core.Models.Exceptions;

namespace VoiceAssistant.Core.Services
{
    /// <summary>
    /// Implementation of speech recognition using whisper.cpp with CUDA for better performance on GPUs.
    /// </summary>
    public class WhisperCppSpeechRecognitionService : ISpeechRecognitionService, IDisposable
    {
        private readonly ILogger<WhisperCppSpeechRecognitionService> _logger;
        private readonly string _modelPath;
        private readonly string _executablePath;
        private readonly bool _cudaEnabled;
        private readonly string _modelType;
        private bool _disposed;
        private bool _isReady;
        private readonly SemaphoreSlim _initializationLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _processingLock = new SemaphoreSlim(1, 1);
        private bool _cudaDetected = false;

        public event EventHandler<string> SpeechRecognized;

        public bool IsReady => _isReady && !_disposed;

        /// <summary>
        /// Initializes a new instance of the WhisperCppSpeechRecognitionService class.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        /// <param name="modelPath">Path to the model file.</param>
        /// <param name="cudaEnabled">Whether CUDA support is enabled.</param>
        /// <param name="modelType">The type of model being used.</param>
        /// <param name="executablePath">Optional path to the whisper.cpp executable. If not provided, it will use the default location.</param>
        public WhisperCppSpeechRecognitionService(
            ILogger<WhisperCppSpeechRecognitionService> logger,
            string modelPath,
            bool cudaEnabled = true,
            string modelType = "large-v3",
            string executablePath = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (string.IsNullOrWhiteSpace(modelPath))
                throw new ArgumentNullException(nameof(modelPath));

            _modelPath = modelPath;
            _modelType = modelType;
            _cudaEnabled = cudaEnabled;

            // Use the provided executable path or default to the executable in the application directory
            _executablePath = executablePath ?? Path.Combine(
                Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory),
                "whisper",
                "main.exe");

            _logger.LogInformation("WhisperCppSpeechRecognitionService created with model type {ModelType} at path {ModelPath}. CUDA enabled: {CudaEnabled}", 
                _modelType, _modelPath, _cudaEnabled);
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (IsReady)
            {
                _logger.LogInformation("WhisperCpp already initialized");
                return;
            }

            // Prevent multiple threads from initializing simultaneously
            await _initializationLock.WaitAsync(cancellationToken);
            try
            {
                if (IsReady)
                {
                    _logger.LogInformation("WhisperCpp already initialized (check after lock)");
                    return;
                }

                _logger.LogInformation("Starting WhisperCpp initialization");

                // Verify that the model file exists
                if (!File.Exists(_modelPath))
                {
                    string errorMessage = $"Whisper model not found at {_modelPath}";
                    _logger.LogError(errorMessage);
                    throw new SpeechRecognitionException(errorMessage);
                }

                // Verify that the executable exists
                if (!File.Exists(_executablePath))
                {
                    string errorMessage = $"Whisper executable not found at {_executablePath}";
                    _logger.LogError(errorMessage);
                    throw new SpeechRecognitionException(errorMessage);
                }

                // Test run the whisper.cpp executable to check if CUDA is available
                try
                {
                    await Task.Run(() =>
                    {
                        using var process = new Process();
                        process.StartInfo = new ProcessStartInfo
                        {
                            FileName = _executablePath,
                            Arguments = "--help",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        };

                        process.Start();
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        bool exited = process.WaitForExit(5000); // Wait for 5 seconds max

                        if (!exited)
                        {
                            try
                            {
                                process.Kill();
                            }
                            catch
                            {
                                // Process might have exited after the check but before Kill
                            }
                            throw new SpeechRecognitionException("Timeout while testing whisper.cpp executable");
                        }

                        // Check if CUDA is mentioned in the output or error
                        _cudaDetected = (output + error).Contains("CUDA") || (output + error).Contains("cuBLAS");
                        
                        if (_cudaEnabled && _cudaDetected)
                        {
                            _logger.LogInformation("CUDA support detected in whisper.cpp");
                        }
                        else if (_cudaEnabled && !_cudaDetected)
                        {
                            _logger.LogWarning("CUDA is enabled in configuration but not detected in whisper.cpp. " +
                                              "Performance may be limited to CPU processing.");
                        }
                        
                        if (process.ExitCode != 0)
                        {
                            string stderr = process.StandardError.ReadToEnd();
                            throw new SpeechRecognitionException($"whisper.cpp test failed with exit code {process.ExitCode}: {stderr}");
                        }
                    }, cancellationToken);

                    _isReady = true;
                    _logger.LogInformation("WhisperCpp initialization completed successfully. CUDA enabled: {CudaDetected}", _cudaDetected);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("WhisperCpp initialization was canceled");
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize WhisperCpp: {ErrorMessage}", ex.Message);
                    throw new SpeechRecognitionException("WhisperCpp initialization error", ex);
                }
            }
            finally
            {
                _initializationLock.Release();
            }
        }

        public async Task<string> TranscribeAsync(byte[] audioData, CancellationToken cancellationToken = default)
        {
            if (audioData == null || audioData.Length == 0)
            {
                _logger.LogError("Cannot transcribe null or empty audio data");
                throw new ArgumentException("Audio data cannot be null or empty", nameof(audioData));
            }

            if (!IsReady)
            {
                _logger.LogWarning("Transcribe called but service is not initialized. Attempting initialization...");
                await InitializeAsync(cancellationToken);
            }

            // Ensure we're processing one transcription at a time to avoid overloading the GPU
            await _processingLock.WaitAsync(cancellationToken);
            try
            {
                _logger.LogDebug("Transcribing audio data of length {Length} bytes", audioData.Length);

                // Create a temporary WAV file for the audio data
                string tempWavPath = Path.Combine(Path.GetTempPath(), $"whisper_input_{Guid.NewGuid()}.wav");
                string tempOutputPath = Path.Combine(Path.GetTempPath(), $"whisper_output_{Guid.NewGuid()}.txt");

                try
                {
                    // Save the audio data to a temporary WAV file
                    await SaveWavFileAsync(audioData, tempWavPath, cancellationToken);

                    // Build arguments for the whisper.cpp executable
                    var args = new StringBuilder();
                    args.Append($"-m \"{_modelPath}\" ");
                    args.Append($"-f \"{tempWavPath}\" ");
                    args.Append($"-otxt -of \"{tempOutputPath}\" ");
                    args.Append("-l en ");  // Language: English
                    
                    // With CUDA, the binary automatically uses the GPU
                    // No need for explicit GPU flag as it would be ignored
                    
                    // Add additional parameters based on model type and requirements
                    args.Append("-t 4 ");  // Number of threads for CPU operations
                    args.Append("-p 1 ");  // Number of processors
                    args.Append("-ml 1 ");  // Max segment length

                    // Run the whisper.cpp executable
                    string output = await RunWhisperProcessAsync(args.ToString(), cancellationToken);

                    // Read the output file
                    string transcription = "";
                    if (File.Exists(tempOutputPath))
                    {
                        transcription = await File.ReadAllTextAsync(tempOutputPath, cancellationToken);
                        transcription = transcription.Trim();
                    }

                    if (string.IsNullOrEmpty(transcription))
                    {
                        _logger.LogWarning("No transcription output was generated. Process output: {Output}", output);
                        
                        // If the output file is empty but the process output has text, try to parse it
                        if (!string.IsNullOrEmpty(output) && output.Contains("["))
                        {
                            // Try to extract transcription from the process output
                            int startIdx = output.LastIndexOf("[");
                            if (startIdx >= 0)
                            {
                                int endIdx = output.IndexOf("]", startIdx);
                                if (endIdx > startIdx)
                                {
                                    // Extract the text between brackets
                                    transcription = output.Substring(startIdx + 1, endIdx - startIdx - 1).Trim();
                                    _logger.LogInformation("Extracted transcription from process output: {Transcription}", transcription);
                                }
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(transcription))
                    {
                        _logger.LogInformation("Transcription result: \"{Text}\"", transcription);
                        SpeechRecognized?.Invoke(this, transcription);
                    }
                    else
                    {
                        _logger.LogWarning("No speech detected in audio");
                    }

                    return transcription;
                }
                finally
                {
                    // Clean up temporary files
                    try
                    {
                        if (File.Exists(tempWavPath))
                            File.Delete(tempWavPath);
                        
                        if (File.Exists(tempOutputPath))
                            File.Delete(tempOutputPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete temporary files");
                    }
                }
            }
            finally
            {
                _processingLock.Release();
            }
        }

        private async Task SaveWavFileAsync(byte[] audioData, string filePath, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                try
                {
                    // Check if audioData is already in WAV format
                    bool isWavFormat = false;
                    if (audioData.Length > 44 && 
                        audioData[0] == 'R' && audioData[1] == 'I' && audioData[2] == 'F' && audioData[3] == 'F' &&
                        audioData[8] == 'W' && audioData[9] == 'A' && audioData[10] == 'V' && audioData[11] == 'E')
                    {
                        isWavFormat = true;
                    }

                    if (isWavFormat)
                    {
                        // Directly write WAV data to file
                        File.WriteAllBytes(filePath, audioData);
                    }
                    else
                    {
                        // Convert raw PCM data to WAV format
                        using var ms = new MemoryStream(audioData);
                        // Attempt to read as WAV first, if it fails, treat as raw PCM
                        try
                        {
                            using var wavReader = new WaveFileReader(ms);
                            using var fileStream = File.Create(filePath);
                            ms.Position = 0;
                            ms.CopyTo(fileStream);
                        }
                        catch
                        {
                            // Not a valid WAV, treat as raw PCM
                            ms.Position = 0;
                            using var writer = new WaveFileWriter(filePath, new WaveFormat(16000, 16, 1));
                            writer.Write(audioData, 0, audioData.Length);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saving WAV file: {ErrorMessage}", ex.Message);
                    throw;
                }
            }, cancellationToken);
        }

        private async Task<string> RunWhisperProcessAsync(string arguments, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                var output = new StringBuilder();
                var error = new StringBuilder();

                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = _executablePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                // Set up output handlers
                process.OutputDataReceived += (sender, e) => {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        output.AppendLine(e.Data);
                        
                        // Log CUDA-related information
                        if (e.Data.Contains("CUDA") || e.Data.Contains("cuBLAS") || e.Data.Contains("GPU"))
                        {
                            _logger.LogInformation("CUDA/GPU info: {Output}", e.Data);
                        }
                        else
                        {
                            _logger.LogDebug("Whisper output: {Output}", e.Data);
                        }
                    }
                };

                process.ErrorDataReceived += (sender, e) => {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        error.AppendLine(e.Data);
                        _logger.LogWarning("Whisper error: {Error}", e.Data);
                    }
                };

                _logger.LogInformation("Starting whisper.cpp process with arguments: {Arguments}", arguments);
                
                try
                {
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    // Use a timeout to prevent infinite waiting
                    int timeoutMs = 30000; // 30 seconds
                    if (!process.WaitForExit(timeoutMs))
                    {
                        _logger.LogWarning("whisper.cpp process timed out after {Timeout}ms", timeoutMs);
                        try
                        {
                            process.Kill();
                        }
                        catch
                        {
                            // Process might have exited after timeout check
                        }
                        throw new SpeechRecognitionException($"whisper.cpp process timed out after {timeoutMs}ms");
                    }

                    if (process.ExitCode != 0)
                    {
                        _logger.LogError("whisper.cpp process exited with code {ExitCode}. Error: {Error}", 
                            process.ExitCode, error.ToString());
                        throw new SpeechRecognitionException($"whisper.cpp process failed with exit code {process.ExitCode}: {error}");
                    }

                    _logger.LogInformation("whisper.cpp process completed successfully");
                    return output.ToString();
                }
                catch (Exception ex) when (!(ex is SpeechRecognitionException || ex is OperationCanceledException))
                {
                    _logger.LogError(ex, "Error running whisper.cpp process: {ErrorMessage}", ex.Message);
                    throw new SpeechRecognitionException("Error running whisper.cpp process", ex);
                }
            }, cancellationToken);
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
                _logger.LogInformation("Disposing WhisperCppSpeechRecognitionService");
                _initializationLock?.Dispose();
                _processingLock?.Dispose();
            }

            _disposed = true;
        }
    }
}