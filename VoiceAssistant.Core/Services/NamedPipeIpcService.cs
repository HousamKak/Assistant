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
        private Task _reconnectTask;
        private bool _reconnecting;
        private readonly int _maxRetries = 15; // More retries
        private readonly int _retryDelayMs = 1000;

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
            _logger.LogInformation("Created NamedPipeIpcService with pipe name: {PipeName}", _pipeName);
        }

        /// <inheritdoc/>
        public async Task StartAsync(bool isServer, CancellationToken cancellationToken = default)
        {
            if (_isConnected)
            {
                _logger.LogWarning("IPC service is already connected");
                return;
            }

            // Cancel any existing cancellation token
            _cts?.Cancel();
            _cts?.Dispose();
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
            if (!_isConnected && _connectionTask == null && _reconnectTask == null)
            {
                return;
            }

            try
            {
                _logger.LogInformation("Stopping IPC service");
                _cts?.Cancel();
                
                // Wait for client/server tasks to complete
                if (_connectionTask != null && !_connectionTask.IsCompleted)
                {
                    try
                    {
                        await Task.WhenAny(_connectionTask, Task.Delay(1000));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error waiting for connection task to complete");
                    }
                }
                
                if (_reconnectTask != null && !_reconnectTask.IsCompleted)
                {
                    try
                    {
                        await Task.WhenAny(_reconnectTask, Task.Delay(1000));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error waiting for reconnect task to complete");
                    }
                }
                
                // Close reader/writer
                if (_writer != null)
                {
                    try 
                    {
                        await _writer.FlushAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error flushing writer");
                    }
                }
                
                _isConnected = false;
                _reconnecting = false;
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
            catch (IOException ex)
            {
                _logger.LogError(ex, "IO error sending IPC message - pipe may be broken");
                
                // Mark as disconnected and try to reconnect
                _isConnected = false;
                
                // Start reconnection process if we're a client
                if (!_isServer && !_reconnecting)
                {
                    StartReconnecting();
                }
                
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending IPC message");
                throw;
            }
        }

        private void StartReconnecting()
        {
            if (_reconnecting || _disposed)
            {
                return;
            }
            
            _reconnecting = true;
            _reconnectTask = Task.Run(async () =>
            {
                _logger.LogInformation("Starting reconnection process");
                int retryCount = 0;
                
                // Cleanup old connection first
                CleanupConnection();
                
                while (retryCount < _maxRetries && !_disposed && _reconnecting)
                {
                    try
                    {
                        _logger.LogInformation("Attempting to reconnect, try {RetryCount}/{MaxRetries}", 
                            retryCount + 1, _maxRetries);
                            
                        // Create a fresh cancellation token for this attempt
                        var reconnectCts = new CancellationTokenSource();
                        
                        if (_isServer)
                        {
                            await StartServerAsync(reconnectCts.Token);
                        }
                        else
                        {
                            await StartClientAsync(reconnectCts.Token);
                        }
                        
                        if (_isConnected)
                        {
                            _logger.LogInformation("Reconnection successful");
                            _reconnecting = false;
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Reconnection attempt {RetryCount} failed", retryCount + 1);
                    }
                    
                    retryCount++;
                    
                    // Don't delay on the last attempt
                    if (retryCount < _maxRetries && !_disposed && _reconnecting)
                    {
                        await Task.Delay(_retryDelayMs);
                    }
                }
                
                _logger.LogError("Failed to reconnect after {MaxRetries} attempts", _maxRetries);
                _reconnecting = false;
            });
        }

        private void CleanupConnection()
        {
            try
            {
                _logger.LogDebug("Cleaning up existing connection resources");
                
                _writer?.Dispose();
                _reader?.Dispose();
                _writer = null;
                _reader = null;
                
                if (_pipeServer != null)
                {
                    if (_pipeServer.IsConnected)
                    {
                        _pipeServer.Disconnect();
                    }
                    _pipeServer.Dispose();
                    _pipeServer = null;
                }
                
                if (_pipeClient != null)
                {
                    if (_pipeClient.IsConnected)
                    {
                        _pipeClient.Close();
                    }
                    _pipeClient.Dispose();
                    _pipeClient = null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error cleaning up connection");
            }
        }

        public async Task StartServerAsync(CancellationToken cancellationToken)
        {
            // Dispose of existing server if any
            if (_pipeServer != null)
            {
                _pipeServer.Dispose();
                _pipeServer = null;
            }

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
                finally
                {
                    // Ensure we're marked as disconnected when the task ends
                    _isConnected = false;
                }
            }, cancellationToken);
            
            // Return immediately without waiting for connection
            // This allows the service to start successfully
        }

        private async Task StartClientAsync(CancellationToken cancellationToken)
        {
            // Dispose of existing client if any
            if (_pipeClient != null)
            {
                _pipeClient.Dispose();
                _pipeClient = null;
            }

            _pipeClient = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                        const int maxRetries = 5;         // Try more times
            const int connectTimeoutMs = 2000; // Shorter timeout per attempt
            int retryCount = 0;
            
            while (retryCount < maxRetries && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Connecting to IPC server, attempt {RetryCount}/{MaxRetries} with timeout {TimeoutMs}ms", 
                        retryCount + 1, maxRetries, connectTimeoutMs);
                        
                    // Use a shorter timeout per attempt but try more times
                    await _pipeClient.ConnectAsync(connectTimeoutMs, cancellationToken);
                    
                    _reader = new StreamReader(_pipeClient, Encoding.UTF8);
                    _writer = new StreamWriter(_pipeClient, Encoding.UTF8) { AutoFlush = true };
                    
                    _isConnected = true;
                    _logger.LogInformation("Connected to IPC server successfully");
                    
                    // Start message loop
                    _connectionTask = Task.Run(ReceiveMessagesAsync, cancellationToken);
                    
                    break;
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("Connection attempt {RetryCount} timed out after {TimeoutMs}ms", 
                        retryCount + 1, connectTimeoutMs);
                    retryCount++;
                    
                    if (retryCount < maxRetries)
                    {
                        await Task.Delay(500, cancellationToken); // Short delay between attempts
                    }
                }
                catch (Exception ex) when (retryCount < maxRetries - 1 && !cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Failed to connect to IPC server on attempt {RetryCount}, retrying...", retryCount + 1);
                    retryCount++;
                    await Task.Delay(500, cancellationToken);
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
                _logger.LogInformation("Starting message receiving loop");
                
                while ((_isServer && _pipeServer?.IsConnected == true) || 
                       (!_isServer && _pipeClient?.IsConnected == true))
                {
                    if (_reader == null)
                    {
                        _logger.LogWarning("Reader is null, breaking message loop");
                        break;
                    }
                    
                    string messageJson = await _reader.ReadLineAsync();
                    
                    if (string.IsNullOrEmpty(messageJson))
                    {
                        // A null/empty line might indicate the pipe is broken
                        _logger.LogWarning("Received empty message, pipe may be broken");
                        await Task.Delay(100); // Small delay to avoid tight loop
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
                        else
                        {
                            _logger.LogWarning("Deserialized message was null");
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
            catch (ObjectDisposedException ex)
            {
                _logger.LogWarning(ex, "Pipe was disposed while reading");
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
                _logger.LogInformation("Disposing NamedPipeIpcService");
                
                _disposed = true; // Mark as disposed first to stop reconnection attempts
                _reconnecting = false;
                
                // Cancel any ongoing operations
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;
                
                // Clean up resources
                CleanupConnection();
            }

            _disposed = true;
        }
    }
}