// VoiceAssistant.Core/Services/NamedPipeIpcService.cs
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VoiceAssistant.Core.Interfaces;
using VoiceAssistant.Core.Models;

namespace VoiceAssistant.Core.Services
{
    /// <summary>
    /// IPC service implementation using named pipes.
    /// </summary>
    public class NamedPipeIpcService : IIpcService
    {
        private readonly ILogger<NamedPipeIpcService> _logger;
        private readonly string _pipeName;
        private NamedPipeServerStream _pipeServer;
        private NamedPipeClientStream _pipeClient;
        private StreamReader _reader;
        private StreamWriter _writer;
        private CancellationTokenSource _cts;
        private bool _isServer;
        private bool _isConnected;
        private bool _disposed;
        private Task _connectionTask;

        /// <inheritdoc/>
        public event EventHandler<IpcMessage> MessageReceived;

        /// <inheritdoc/>
        public bool IsConnected => _isConnected;

        /// <summary>
        /// Initializes a new instance of the NamedPipeIpcService class.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        /// <param name="pipeName">The name of the pipe to use for communication.</param>
        /// <exception cref="ArgumentNullException">Thrown when logger or pipeName is null.</exception>
        public NamedPipeIpcService(ILogger<NamedPipeIpcService> logger, string pipeName = "VoiceAssistantPipe")
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pipeName = !string.IsNullOrEmpty(pipeName) ? pipeName : throw new ArgumentNullException(nameof(pipeName));
        }

        /// <inheritdoc/>
        public async Task StartAsync(bool isServer, CancellationToken cancellationToken = default)
        {
            if (_isConnected)
            {
                _logger.LogWarning("IPC service is already connected");
                return;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _isServer = isServer;

            try
            {
                if (_isServer)
                {
                    _logger.LogInformation("Starting IPC server with pipe name: {PipeName}", _pipeName);
                    await StartServerAsync(_cts.Token);
                }
                else
                {
                    _logger.LogInformation("Starting IPC client with pipe name: {PipeName}", _pipeName);
                    await StartClientAsync(_cts.Token);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start IPC service");
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task StopAsync()
        {
            if (!_isConnected)
            {
                return;
            }

            try
            {
                _cts?.Cancel();
                
                if (_connectionTask != null && !_connectionTask.IsCompleted)
                {
                    await _connectionTask;
                }
                
                _isConnected = false;
                _logger.LogInformation("IPC service stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping IPC service");
            }
        }

        /// <inheritdoc/>
        public async Task SendMessageAsync(IpcMessage message)
        {
            if (!_isConnected || _writer == null)
            {
                _logger.LogWarning("Cannot send message: IPC service is not connected");
                return;
            }

            try
            {
                string json = JsonSerializer.Serialize(message);
                await _writer.WriteLineAsync(json);
                await _writer.FlushAsync();
                _logger.LogDebug("Sent IPC message of type {MessageType}", message.Type);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending IPC message");
                throw;
            }
        }

        public async Task StartServerAsync(CancellationToken cancellationToken)
        {
            _pipeServer = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Message,
                PipeOptions.Asynchronous);

            _logger.LogInformation("IPC server created, waiting for client connection...");
            
            // Start a separate task for waiting for connection 
            // This allows the service to start without waiting for a client
            _connectionTask = Task.Run(async () => 
            {
                try
                {
                    // Wait for a client to connect
                    await _pipeServer.WaitForConnectionAsync(cancellationToken);
                    
                    _reader = new StreamReader(_pipeServer, Encoding.UTF8);
                    _writer = new StreamWriter(_pipeServer, Encoding.UTF8) { AutoFlush = true };
                    
                    _isConnected = true;
                    _logger.LogInformation("Client connected to IPC server");
                    
                    // Start message loop
                    await ReceiveMessagesAsync();
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Waiting for client connection canceled");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error waiting for client connection");
                }
            }, cancellationToken);
            
            // Return immediately without waiting for connection
            // This allows the service to start successfully
        }

        private async Task StartClientAsync(CancellationToken cancellationToken)
        {
            _pipeClient = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            
            const int maxRetries = 10;
            const int retryDelayMs = 1000;
            int retryCount = 0;
            
            while (retryCount < maxRetries && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Connecting to IPC server, attempt {RetryCount}/{MaxRetries}...", 
                        retryCount + 1, maxRetries);
                        
                    await _pipeClient.ConnectAsync(5000, cancellationToken);
                    
                    _reader = new StreamReader(_pipeClient, Encoding.UTF8);
                    _writer = new StreamWriter(_pipeClient, Encoding.UTF8) { AutoFlush = true };
                    
                    _isConnected = true;
                    _logger.LogInformation("Connected to IPC server");
                    
                    // Start message loop
                    _connectionTask = Task.Run(ReceiveMessagesAsync, cancellationToken);
                    
                    break;
                }
                catch (Exception ex) when (retryCount < maxRetries - 1 && !cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Failed to connect to IPC server, retrying in {RetryDelayMs}ms...", retryDelayMs);
                    retryCount++;
                    await Task.Delay(retryDelayMs, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to connect to IPC server after {RetryCount} attempts", retryCount + 1);
                    throw;
                }
            }
            
            if (!_isConnected && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Failed to connect to IPC server after {maxRetries} attempts");
            }
        }

        private async Task ReceiveMessagesAsync()
        {
            try
            {
                while ((_isServer && _pipeServer.IsConnected) || 
                       (!_isServer && _pipeClient.IsConnected))
                {
                    string messageJson = await _reader.ReadLineAsync();
                    
                    if (string.IsNullOrEmpty(messageJson))
                    {
                        continue;
                    }
                    
                    try
                    {
                        var message = JsonSerializer.Deserialize<IpcMessage>(messageJson);
                        
                        if (message != null)
                        {
                            _logger.LogDebug("Received IPC message of type {MessageType}", message.Type);
                            MessageReceived?.Invoke(this, message);
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError(ex, "Error deserializing IPC message: {Message}", messageJson);
                    }
                }
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Pipe connection was closed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in IPC message loop");
            }
            finally
            {
                _isConnected = false;
                _logger.LogInformation("IPC connection closed");
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes resources used by the IPC service.
        /// </summary>
        /// <param name="disposing">True if disposing managed resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _cts?.Cancel();
                _cts?.Dispose();
                
                _writer?.Dispose();
                _reader?.Dispose();
                
                _pipeServer?.Dispose();
                _pipeClient?.Dispose();
            }

            _disposed = true;
        }
    }
}