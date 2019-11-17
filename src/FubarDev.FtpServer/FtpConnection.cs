//-----------------------------------------------------------------------
// <copyright file="FtpConnection.cs" company="Fubar Development Junker">
//     Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>
// <author>Mark Junker</author>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using FubarDev.FtpServer.Authentication;
using FubarDev.FtpServer.CommandHandlers;
using FubarDev.FtpServer.Commands;
using FubarDev.FtpServer.ConnectionChecks;
using FubarDev.FtpServer.DataConnection;
using FubarDev.FtpServer.Features;
using FubarDev.FtpServer.Localization;
using FubarDev.FtpServer.Networking;
using FubarDev.FtpServer.ServerCommands;

using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FubarDev.FtpServer
{
    /// <summary>
    /// This class represents a FTP connection.
    /// </summary>
    public sealed class FtpConnection : IFtpConnection
    {
        private readonly TcpClient _socket;

        private readonly IFtpConnectionAccessor _connectionAccessor;

        private readonly IServerCommandExecutor _serverCommandExecutor;

        private readonly SecureDataConnectionWrapper _secureDataConnectionWrapper;

        private readonly IDisposable? _loggerScope;

        private readonly Channel<IServerCommand> _serverCommandChannel =
            Channel.CreateBounded<IServerCommand>(new BoundedChannelOptions(5));

        private readonly Pipe _socketCommandPipe = new Pipe();

        private readonly Pipe _socketResponsePipe = new Pipe();

        private readonly Task _commandReader;

        private readonly Channel<FtpCommand> _ftpCommandChannel = Channel.CreateBounded<FtpCommand>(5);

        private readonly int? _dataPort;

        private readonly ILogger<FtpConnection>? _logger;

        /// <summary>
        /// This semaphore avoids the execution of `StopAsync` while a `StartAsync` is running.
        /// </summary>
        private readonly SemaphoreSlim _serviceControl = new SemaphoreSlim(1);

        /// <summary>
        /// This semaphore avoids the duplicate execution of `StopAsync`.
        /// </summary>
        private readonly SemaphoreSlim _stopSemaphore = new SemaphoreSlim(1);

        /// <summary>
        /// Gets the stream reader service.
        /// </summary>
        /// <remarks>
        /// It writes data from the network stream into a pipe.
        /// </remarks>
        private readonly IFtpService _streamReaderService;

        /// <summary>
        /// Gets the stream writer service.
        /// </summary>
        /// <remarks>
        /// It reads data from the pipe and writes it to the network stream.
        /// </remarks>
        private readonly IFtpService _streamWriterService;

        private readonly FtpConnectionContext _context;

        private bool _connectionClosing;

        private int _connectionClosed;

        private Task? _commandChannelReader;

        private Task? _serverCommandHandler;

        /// <summary>
        /// Initializes a new instance of the <see cref="FtpConnection"/> class.
        /// </summary>
        /// <param name="socketAccessor">The accessor to get the socket used to communicate with the client.</param>
        /// <param name="options">The options for the FTP connection.</param>
        /// <param name="portOptions">The <c>PORT</c> command options.</param>
        /// <param name="connectionAccessor">The accessor to get the connection that is active during the <see cref="FtpCommandHandler.Process"/> method execution.</param>
        /// <param name="catalogLoader">The catalog loader for the FTP server.</param>
        /// <param name="serverCommandExecutor">The executor for server commands.</param>
        /// <param name="serviceProvider">The service provider for the connection.</param>
        /// <param name="secureDataConnectionWrapper">Wraps a data connection into an SSL stream.</param>
        /// <param name="sslStreamWrapperFactory">The SSL stream wrapper factory.</param>
        /// <param name="logger">The logger for the FTP connection.</param>
        /// <param name="loggerFactory">The logger factory.</param>
        public FtpConnection(
            TcpSocketClientAccessor socketAccessor,
            IOptions<FtpConnectionOptions> options,
            IOptions<PortCommandOptions> portOptions,
            IFtpConnectionAccessor connectionAccessor,
            IFtpCatalogLoader catalogLoader,
            IServerCommandExecutor serverCommandExecutor,
            IServiceProvider serviceProvider,
            SecureDataConnectionWrapper secureDataConnectionWrapper,
            ISslStreamWrapperFactory sslStreamWrapperFactory,
            ILogger<FtpConnection>? logger = null,
            ILoggerFactory? loggerFactory = null)
        {
            var socket = socketAccessor.TcpSocketClient ?? throw new InvalidOperationException("The socket to communicate with the client was not set");
            var defaultEncoding = options.Value.DefaultEncoding ?? Encoding.ASCII;
            var applicationInputPipe = new Pipe();
            var applicationOutputPipe = new Pipe();
            var socketPipe = new DuplexPipe(_socketCommandPipe.Reader, _socketResponsePipe.Writer);
            var connectionPipe = new DuplexPipe(applicationOutputPipe.Reader, applicationInputPipe.Writer);
            var transport = new DuplexPipe(applicationInputPipe.Reader, applicationOutputPipe.Writer);
            var remoteEndPoint = (IPEndPoint)socket.Client.RemoteEndPoint;

            _context = new FtpConnectionContext(
                catalogLoader,
                defaultEncoding,
                _serverCommandChannel.Writer,
                socketPipe,
                connectionPipe,
                sslStreamWrapperFactory,
                transport,
                socket.Client.LocalEndPoint,
                remoteEndPoint,
                serviceProvider);

            var properties = new Dictionary<string, object?>
            {
                ["RemoteAddress"] = remoteEndPoint.ToString(),
                ["RemoteIp"] = remoteEndPoint.Address.ToString(),
                ["RemotePort"] = remoteEndPoint.Port,
                ["ConnectionId"] = _context.ConnectionId,
            };

            _socket = socket;
            _loggerScope = logger?.BeginScope(properties);

            _dataPort = portOptions.Value.DataPort;
            _connectionAccessor = connectionAccessor;
            _serverCommandExecutor = serverCommandExecutor;
            _secureDataConnectionWrapper = secureDataConnectionWrapper;

            _logger = logger;

            var originalStream = socketAccessor.TcpSocketStream ?? _socket.GetStream();
            _streamReaderService = new ConnectionClosingNetworkStreamReader(
                originalStream,
                _socketCommandPipe.Writer,
                _context,
                loggerFactory?.CreateLogger($"{nameof(StreamPipeWriterService)}:Socket:Receive"));
            _streamWriterService = new StreamPipeWriterService(
                originalStream,
                _socketResponsePipe.Reader,
                _context.ConnectionClosed,
                loggerFactory?.CreateLogger($"{nameof(StreamPipeWriterService)}:Socket:Transmit"));

            _commandReader = ReadCommandsFromPipeline(
                _ftpCommandChannel.Writer,
                _context.ConnectionClosed);
        }

        /// <inheritdoc />
        public event EventHandler? Closed;

        /// <inheritdoc />
        [Obsolete("Use the IServiceProvidersFeature")]
        public IServiceProvider ConnectionServices => _context.RequestServices;

        /// <summary>
        /// Gets the feature collection.
        /// </summary>
        public IFeatureCollection Features => _context.Features;

        /// <summary>
        /// Gets the cancellation token to use to signal a task cancellation.
        /// </summary>
        [Obsolete("Use the IConnectionLifetimeFeature")]
        CancellationToken IFtpConnection.CancellationToken => _context.ConnectionClosed;

        /// <inheritdoc />
        public async Task StartAsync()
        {
            await _serviceControl.WaitAsync().ConfigureAwait(false);
            try
            {
                // Initialize the FTP connection accessor
                _connectionAccessor.FtpConnection = this;

                // Set the default FTP data connection feature
                var activeDataConnectionFeatureFactory =
                    _context.RequestServices.GetRequiredService<ActiveDataConnectionFeatureFactory>();
                var dataConnectionFeature = await activeDataConnectionFeatureFactory
                   .CreateFeatureAsync(null, (IPEndPoint)_context.RemoteEndPoint, _dataPort)
                   .ConfigureAwait(false);
                Features.Set(dataConnectionFeature);

                // Set the checks for the activity information of the FTP connection.
                var checks = _context.RequestServices.GetRequiredService<IEnumerable<IFtpConnectionCheck>>().ToList();
                _context.SetChecks(checks);

                // Connection information
                var connectionFeature = Features.Get<IConnectionEndPointFeature>();
                _logger?.LogInformation("Connected from {remoteIp}", connectionFeature.RemoteEndPoint);

                await _streamWriterService.StartAsync(CancellationToken.None)
                   .ConfigureAwait(false);
                await _streamReaderService.StartAsync(CancellationToken.None)
                   .ConfigureAwait(false);
                await _context.SecureConnectionAdapterManager.StartAsync(CancellationToken.None)
                   .ConfigureAwait(false);

                _commandChannelReader = CommandChannelDispatcherAsync(
                    _ftpCommandChannel.Reader,
                    _context.ConnectionClosed);

                _serverCommandHandler = SendResponsesAsync(_serverCommandChannel, _context.ConnectionClosed);
            }
            finally
            {
                _serviceControl.Release();
            }
        }

        /// <inheritdoc />
        public async Task StopAsync()
        {
            var success = await _stopSemaphore.WaitAsync(0)
               .ConfigureAwait(false);
            if (!success)
            {
                // Handles recursion caused by CommandChannelDispatcherAsync.
                return;
            }

            try
            {
                _logger?.LogTrace("StopAsync called");

                await _serviceControl.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (Interlocked.CompareExchange(ref _connectionClosed, 1, 0) != 0)
                    {
                        return;
                    }

                    Abort();

                    try
                    {
                        _serverCommandChannel.Writer.Complete();
                        await _commandReader.ConfigureAwait(false);

                        if (_commandChannelReader != null)
                        {
                            await _commandChannelReader.ConfigureAwait(false);
                        }

                        if (_serverCommandHandler != null)
                        {
                            await _serverCommandHandler.ConfigureAwait(false);
                        }

                        await _context.SecureConnectionAdapterManager.StopAsync(CancellationToken.None)
                           .ConfigureAwait(false);
                        await _streamReaderService.StopAsync(CancellationToken.None)
                           .ConfigureAwait(false);
                        await _streamWriterService.StopAsync(CancellationToken.None)
                           .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // Something went wrong... badly!
                        _logger?.LogError(ex, ex.Message);
                    }

                    // Dispose all features (if disposable)
                    await Features.ResetAsync(CancellationToken.None, _logger).ConfigureAwait(false);

                    _logger?.LogInformation("Connection closed");
                }
                finally
                {
                    _serviceControl.Release();
                }
            }
            finally
            {
                _stopSemaphore.Release();
            }

            OnClosed();
        }

        /// <inheritdoc/>
        public async Task<IFtpDataConnection> OpenDataConnectionAsync(TimeSpan? timeout, CancellationToken cancellationToken)
        {
            var dataConnectionFeature = Features.Get<IFtpDataConnectionFeature>();
            var dataConnection = await dataConnectionFeature.GetDataConnectionAsync(timeout ?? TimeSpan.FromSeconds(10), cancellationToken)
               .ConfigureAwait(false);
            return await _secureDataConnectionWrapper.WrapAsync(dataConnection)
               .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!_connectionClosing)
            {
                Abort();
            }

            _socket.Dispose();
            _context.Dispose();
            _loggerScope?.Dispose();
        }

        private void Abort()
        {
            if (_connectionClosing)
            {
                return;
            }

            _connectionClosing = true;
            _context.Abort();
        }

        /// <summary>
        /// Send responses to the client.
        /// </summary>
        /// <param name="serverCommandReader">Reader for the responses.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The task.</returns>
        private async Task SendResponsesAsync(
            ChannelReader<IServerCommand> serverCommandReader,
            CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    _logger?.LogTrace("Wait to read server commands");
                    var hasResponse = await serverCommandReader.WaitToReadAsync(cancellationToken)
                       .ConfigureAwait(false);
                    if (!hasResponse)
                    {
                        _logger?.LogTrace("Server command channel completed");
                        return;
                    }

                    while (serverCommandReader.TryRead(out var response))
                    {
                        _logger?.LogTrace("Executing server command \"{response}\"", response);
                        await _serverCommandExecutor.ExecuteAsync(response, cancellationToken)
                           .ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                var exception = ex;
                while (exception is AggregateException aggregateException && aggregateException.InnerException != null)
                {
                    exception = aggregateException.InnerException;
                }

                switch (exception)
                {
                    case IOException _:
                        if (cancellationToken.IsCancellationRequested)
                        {
                            _logger?.LogWarning("Last response probably incomplete");
                        }
                        else
                        {
                            _logger?.LogWarning("Connection lost or closed by client. Remaining output discarded");
                        }

                        break;

                    case OperationCanceledException _:
                        // Cancelled
                        break;
                    case null:
                        // Should never happen
                        break;
                    default:
                        // Don't throw, connection gets closed anyway.
                        _logger?.LogError(0, exception, exception.Message);
                        break;
                }
            }
            finally
            {
                _logger?.LogDebug("Stopped sending responses");
                try
                {
                    _context.Abort();
                }
                catch (ObjectDisposedException)
                {
                    // This might happen if the command handler stopped the connection.
                    // Usual reasons are:
                    // - Closing the connection was requested by the client
                    // - Closing the connection was forced due to an FTP status 421
                }
            }
        }

        /// <summary>
        /// Final (default) dispatch from FTP commands to the handlers.
        /// </summary>
        /// <param name="context">The context for the FTP command execution.</param>
        /// <returns>The task.</returns>
        private Task DispatchCommandAsync(FtpContext context)
        {
            var dispatcher = _context.RequestServices.GetRequiredService<IFtpCommandDispatcher>();
            return dispatcher.DispatchAsync(context, _context.ConnectionClosed);
        }

        private void OnClosed()
        {
            Closed?.Invoke(this, new EventArgs());
        }

        private async Task ReadCommandsFromPipeline(
            ChannelWriter<FtpCommand> commandWriter,
            CancellationToken cancellationToken)
        {
            var reader = _context.Transport.Input;
            var collector = new FtpCommandCollector(() => Features.Get<IEncodingFeature>().Encoding);

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var result = await reader.ReadAsync(cancellationToken)
                       .ConfigureAwait(false);

                    var buffer = result.Buffer;
                    var position = buffer.Start;
                    while (buffer.TryGet(ref position, out var memory))
                    {
                        var commands = collector.Collect(memory.Span);
                        foreach (var command in commands)
                        {
                            await commandWriter.WriteAsync(command, cancellationToken)
                               .ConfigureAwait(false);
                        }
                    }

                    // Required to signal an end of the read operation.
                    reader.AdvanceTo(buffer.End);

                    // Stop reading if there's no more data coming.
                    if (result.IsCompleted)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex) when (ex.Is<IOException>() && !cancellationToken.IsCancellationRequested)
            {
                _logger?.LogWarning("Connection lost or closed by client");
                Abort();
            }
            catch (Exception ex) when (ex.Is<IOException>())
            {
                // Most likely closed by server.
                _logger?.LogWarning("Connection lost or closed by server");
                Abort();
            }
            catch (Exception ex) when (ex.Is<OperationCanceledException>())
            {
                // We're getting here because someone called StopAsync on the connection.
                // Reasons might be:
                // - Server detected a closed connection in another part of the communication stack
                // - QUIT command
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Closing connection due to error {0}", ex.Message);
                Abort();
            }
            finally
            {
                reader.Complete();

                _logger?.LogDebug("Stopped reading commands");
            }
        }

        private async Task CommandChannelDispatcherAsync(ChannelReader<FtpCommand> commandReader, CancellationToken cancellationToken)
        {
            // Initialize middleware objects
            var middlewareObjects = _context.RequestServices.GetRequiredService<IEnumerable<IFtpMiddleware>>();
            var nextStep = new FtpRequestDelegate(DispatchCommandAsync);
            foreach (var middleware in middlewareObjects.Reverse())
            {
                var tempStep = nextStep;
                nextStep = (context) => middleware.InvokeAsync(context, tempStep);
            }

            var requestDelegate = nextStep;

            // Statistics feature
            var statisticsCollectorFeature = Features.Get<IFtpStatisticsCollectorFeature>();
            try
            {
                Task<bool>? readTask = null;
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (readTask == null)
                    {
                        readTask = commandReader.WaitToReadAsync(cancellationToken).AsTask();
                    }

                    var tasks = new List<Task>() { readTask };
                    var backgroundTaskLifetimeService = Features.Get<IBackgroundTaskLifetimeFeature?>();
                    if (backgroundTaskLifetimeService != null)
                    {
                        tasks.Add(backgroundTaskLifetimeService.Task);
                    }

                    var completedTask = await Task.WhenAny(tasks.ToArray()).ConfigureAwait(false);
                    if (completedTask == null)
                    {
                        break;
                    }

                    // ReSharper disable once PatternAlwaysOfType
                    if (backgroundTaskLifetimeService?.Task == completedTask)
                    {
                        _logger?.LogTrace("Background task completed.");
                        await completedTask.ConfigureAwait(false);
                        Features.Set<IBackgroundTaskLifetimeFeature?>(null);
                    }
                    else
                    {
                        var hasCommand = await readTask.ConfigureAwait(false);
                        readTask = null;

                        if (!hasCommand)
                        {
                            break;
                        }

                        while (commandReader.TryRead(out var command))
                        {
                            var receivedCommand = command;
                            statisticsCollectorFeature.ForEach(collector => collector.ReceivedCommand(receivedCommand));

                            _logger?.LogCommand(command);
                            var context = new FtpContext(command, _serverCommandChannel, this);
                            await requestDelegate(context)
                               .ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex.Is<OperationCanceledException>())
            {
                // Was expected, ignore!
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, ex.Message);
            }
            finally
            {
                // Trigger stopping this connection
                Abort();
            }
        }

        private class ConnectionClosingNetworkStreamReader : StreamPipeReaderService
        {
            private readonly IConnectionLifetimeFeature _lifetimeFeature;

            public ConnectionClosingNetworkStreamReader(
                Stream stream,
                PipeWriter pipeWriter,
                IConnectionLifetimeFeature lifetimeFeature,
                ILogger? logger = null)
                : base(stream, pipeWriter, lifetimeFeature.ConnectionClosed, logger)
            {
                _lifetimeFeature = lifetimeFeature;
            }

            /// <inheritdoc />
            protected override async Task<int> ReadFromStreamAsync(byte[] buffer, int offset, int length, CancellationToken cancellationToken)
            {
                var readTask = Stream
                   .ReadAsync(buffer, offset, length, cancellationToken);

                // We ensure that this service can be closed ASAP with the help
                // of a Task.Delay.
                var resultTask = await Task.WhenAny(readTask, Task.Delay(-1, cancellationToken))
                   .ConfigureAwait(false);
                if (resultTask != readTask || cancellationToken.IsCancellationRequested)
                {
                    Logger?.LogTrace("Cancelled through Task.Delay");
                    return 0;
                }

                var result = readTask.Result;
                readTask.Dispose();
                return result;
            }

            /// <inheritdoc />
            protected override async Task OnCloseAsync(Exception? exception, CancellationToken cancellationToken)
            {
                await base.OnCloseAsync(exception, cancellationToken)
                   .ConfigureAwait(false);

                // Signal a closed connection.
                _lifetimeFeature.Abort();
            }
        }

        private class DuplexPipe : IDuplexPipe
        {
            public DuplexPipe(PipeReader input, PipeWriter output)
            {
                Input = input;
                Output = output;
            }

            /// <inheritdoc />
            public PipeReader Input { get; }

            /// <inheritdoc />
            public PipeWriter Output { get; }
        }
    }
}
