// VoiceAssistant.Core/Services/TcpIpcService.cs
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
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
    /// IPC service implementation using TCP/IP sockets.
    /// </summary>
    public class TcpIpcService : IIpcService
    {
        private readonly ILogger<TcpIpcService> _logger;
        private readonly string _host;
        private readonly int _port;
        private TcpListener _listener;
        private TcpClient _client;
        private StreamReader _reader;
        private StreamWriter _writer;
        private CancellationTokenSource _cts;
        private bool _isServer;
        private bool _isConnected;
        private bool _disposed;
        private Task _connectionTask;
        private Task _reconnectTask;
        private bool _reconnecting;
        private readonly int _maxRetries = 15;
        private readonly int _retryDelayMs = 1000;

        /// <inheritdoc/>
        public event EventHandler<IpcMessage> MessageReceived;

        /// <inheritdoc/>
        public bool IsConnected => _isConnected;

        /// <summary>
        /// Initializes a new instance of the TcpIpcService class.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        /// <param name="host">The host address (default: 127.0.0.1).</param>
        /// <param name="port">The port to use for communication (default: 5000).</param>
        /// <exception cref="ArgumentNullException">Thrown when logger is null.</exception>
        public TcpIpcService(ILogger<TcpIpcService> logger, string host = "127.0.0.1", int port = 5000)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _host = !string.IsNullOrEmpty(host) ? host : throw new ArgumentNullException(nameof(host));
            _port = port > 0 ? port : throw new ArgumentOutOfRangeException(nameof(port), "Port must be greater than 0");
            _logger.LogInformation("Created TcpIpcService with host: {Host}, port: {Port}", _host, _port);
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
                    _logger.LogInformation("Starting IPC server on {Host}:{Port}", _host, _port);
                    await StartServerAsync(_cts.Token);
                }
                else
                {
                    _logger.LogInformation("Starting IPC client connecting to {Host}:{Port}", _host, _port);
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
                _logger.LogError(ex, "IO error sending IPC message - connection may be broken");
                
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
                
                if (_client != null)
                {
                    _client.Close();
                    _client.Dispose();
                    _client = null;
                }
                
                if (_listener != null)
                {
                    _listener.Stop();
                    _listener = null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error cleaning up connection");
            }
        }

        private async Task StartServerAsync(CancellationToken cancellationToken)
        {
            // Dispose of existing server if any
            if (_listener != null)
            {
                _listener.Stop();
                _listener = null;
            }

            // Create a TCP/IP socket
            _listener = new TcpListener(IPAddress.Parse(_host), _port);
            _listener.Start();

            _logger.LogInformation("TCP server listening on {Host}:{Port}, waiting for client connection...", _host, _port);
            
            // Start a separate task for waiting for connection 
            // This allows the service to start without waiting for a client
            _connectionTask = Task.Run(async () => 
            {
                try
                {
                    // Wait for a client to connect
                    _client = await _listener.AcceptTcpClientAsync(cancellationToken);
                    
                    var stream = _client.GetStream();
                    _reader = new StreamReader(stream, Encoding.UTF8);
                    _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
                    
                    _isConnected = true;
                    _logger.LogInformation("Client connected to TCP server from {RemoteEndPoint}", 
                        _client.Client.RemoteEndPoint);
                    
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
            if (_client != null)
            {
                _client.Close();
                _client.Dispose();
                _client = null;
            }

            const int maxRetries = 5;
            const int connectTimeoutMs = 2000; // Shorter timeout per attempt
            int retryCount = 0;
            
            while (retryCount < maxRetries && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Connecting to TCP server at {Host}:{Port}, attempt {RetryCount}/{MaxRetries}", 
                        _host, _port, retryCount + 1, maxRetries);
                    
                    // Create a new client with timeout
                    _client = new TcpClient();
                    
                    // Connect with timeout
                    using var timeoutCts = new CancellationTokenSource(connectTimeoutMs);
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
                    
                    // Start the connect task
                    var connectTask = _client.ConnectAsync(_host, _port);
                    var timeoutTask = Task.Delay(connectTimeoutMs, linkedCts.Token);
                    
                    // Wait for connect or timeout
                    await Task.WhenAny(connectTask, timeoutTask);
                    
                    if (!connectTask.IsCompleted)
                    {
                        // Connection timed out
                        throw new TimeoutException($"Connection attempt timed out after {connectTimeoutMs}ms");
                    }
                    
                    // Wait for the connect task to complete (should be fast as it's already done)
                    await connectTask;
                    
                    // If we get here, the connection was successful
                    var stream = _client.GetStream();
                    _reader = new StreamReader(stream, Encoding.UTF8);
                    _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
                    
                    _isConnected = true;
                    _logger.LogInformation("Connected to TCP server at {Host}:{Port} successfully", _host, _port);
                    
                    // Start message loop
                    _connectionTask = Task.Run(ReceiveMessagesAsync, cancellationToken);
                    
                    break;
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("Connection attempt {RetryCount} timed out after {TimeoutMs}ms", 
                        retryCount + 1, connectTimeoutMs);
                    retryCount++;
                    
                    // Close the client on timeout
                    _client?.Close();
                    _client?.Dispose();
                    _client = null;
                    
                    if (retryCount < maxRetries)
                    {
                        await Task.Delay(500, cancellationToken); // Short delay between attempts
                    }
                }
                catch (Exception ex) when (retryCount < maxRetries - 1 && !cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Failed to connect to TCP server on attempt {RetryCount}, retrying...", retryCount + 1);
                    retryCount++;
                    
                    // Close the client on error
                    _client?.Close();
                    _client?.Dispose();
                    _client = null;
                    
                    await Task.Delay(500, cancellationToken);
                }
                catch (Exception ex)
                {
                    // Close the client on error
                    _client?.Close();
                    _client?.Dispose();
                    _client = null;
                    
                    _logger.LogError(ex, "Failed to connect to TCP server after {RetryCount} attempts", retryCount + 1);
                    throw;
                }
            }
            
            if (!_isConnected && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Failed to connect to TCP server after {maxRetries} attempts");
            }
        }

        private async Task ReceiveMessagesAsync()
        {
            try
            {
                _logger.LogInformation("Starting message receiving loop");
                
                while ((_isServer && _client?.Connected == true) || 
                       (!_isServer && _client?.Connected == true))
                {
                    if (_reader == null)
                    {
                        _logger.LogWarning("Reader is null, breaking message loop");
                        break;
                    }
                    
                    string messageJson = await _reader.ReadLineAsync();
                    
                    if (string.IsNullOrEmpty(messageJson))
                    {
                        // A null/empty line might indicate the connection is broken
                        _logger.LogWarning("Received empty message, connection may be broken");
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
                _logger.LogWarning(ex, "Connection was closed");
            }
            catch (ObjectDisposedException ex)
            {
                _logger.LogWarning(ex, "Connection was disposed while reading");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in IPC message loop");
            }
            finally
            {
                _isConnected = false;
                _logger.LogInformation("TCP connection closed");
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
                _logger.LogInformation("Disposing TcpIpcService");
                
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