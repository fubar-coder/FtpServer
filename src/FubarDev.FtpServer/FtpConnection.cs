//-----------------------------------------------------------------------
// <copyright file="FtpConnection.cs" company="Fubar Development Junker">
//     Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>
// <author>Mark Junker</author>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using FubarDev.FtpServer.ConnectionHandlers;
using FubarDev.FtpServer.DataConnection;
using FubarDev.FtpServer.Events;
using FubarDev.FtpServer.Features;
using FubarDev.FtpServer.Features.Impl;
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
    public sealed class FtpConnection : FtpConnectionContext, IFtpConnection, IObservable<IFtpConnectionEvent>, IFtpConnectionEventHost
    {
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        private readonly TcpClient _socket;

        private readonly IFtpConnectionAccessor _connectionAccessor;

        private readonly IServerCommandExecutor _serverCommandExecutor;

        private readonly SecureDataConnectionWrapper _secureDataConnectionWrapper;

        private readonly IDisposable? _loggerScope;

        private readonly Channel<IServerCommand> _serverCommandChannel;

        private readonly Pipe _socketCommandPipe = new Pipe();

        private readonly Pipe _socketResponsePipe = new Pipe();

        private readonly NetworkStreamFeature _networkStreamFeature;

        private readonly Task _commandReader;

        private readonly Channel<FtpCommand> _ftpCommandChannel = Channel.CreateBounded<FtpCommand>(5);

        private readonly int? _dataPort;

        private readonly ILogger<FtpConnection>? _logger;

        private readonly IPEndPoint _remoteEndPoint;

        private readonly object _observersLock = new object();

        private readonly List<ObserverRegistration> _observers = new List<ObserverRegistration>();

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

        private readonly IdleCheck _idleCheck;

        private readonly IServiceProvider _serviceProvider;

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
#pragma warning disable 618
            ConnectionServices =
#pragma warning restore 618
                _serviceProvider = serviceProvider;

            ConnectionId = "FTP-" + Guid.NewGuid().ToString("N");

            _dataPort = portOptions.Value.DataPort;
            _idleCheck = new IdleCheck(this);
            var remoteEndPoint = _remoteEndPoint = (IPEndPoint)socket.Client.RemoteEndPoint;

            var properties = new Dictionary<string, object?>
            {
                ["RemoteAddress"] = remoteEndPoint.ToString(),
                ["RemoteIp"] = remoteEndPoint.Address.ToString(),
                ["RemotePort"] = remoteEndPoint.Port,
                ["ConnectionId"] = ConnectionId,
            };

            _loggerScope = logger?.BeginScope(properties);

            _socket = socket;
            _connectionAccessor = connectionAccessor;
            _serverCommandExecutor = serverCommandExecutor;
            _secureDataConnectionWrapper = secureDataConnectionWrapper;
            _serverCommandChannel = Channel.CreateBounded<IServerCommand>(new BoundedChannelOptions(3));

            _logger = logger;

            var parentFeatures = new FeatureCollection();
            var connectionFeature = new ConnectionFeature(
                (IPEndPoint)socket.Client.LocalEndPoint,
                remoteEndPoint);
            var secureConnectionFeature = new SecureConnectionFeature();

            var applicationInputPipe = new Pipe();
            var applicationOutputPipe = new Pipe();
            var socketPipe = new DuplexPipe(_socketCommandPipe.Reader, _socketResponsePipe.Writer);
            var connectionPipe = new DuplexPipe(applicationOutputPipe.Reader, applicationInputPipe.Writer);

            var originalStream = socketAccessor.TcpSocketStream ?? socket.GetStream();
            _streamReaderService = new ConnectionClosingNetworkStreamReader(
                originalStream,
                _socketCommandPipe.Writer,
                _cancellationTokenSource,
                loggerFactory?.CreateLogger($"{nameof(StreamPipeWriterService)}:Socket:Receive"));
            _streamWriterService = new StreamPipeWriterService(
                originalStream,
                _socketResponsePipe.Reader,
                _cancellationTokenSource.Token,
                loggerFactory?.CreateLogger($"{nameof(StreamPipeWriterService)}:Socket:Transmit"));

            var transport = new DuplexPipe(applicationInputPipe.Reader, applicationOutputPipe.Writer);
            var transportFeature = new FtpConnectionTransportFeature(transport);

            _networkStreamFeature = new NetworkStreamFeature(
                new SecureConnectionAdapterManager(
                    socketPipe,
                    connectionPipe,
                    sslStreamWrapperFactory,
                    _cancellationTokenSource.Token),
                transportFeature);

#pragma warning disable 618
            parentFeatures.Set<IConnectionFeature>(connectionFeature);
#pragma warning restore 618
            parentFeatures.Set<IConnectionEndPointFeature>(connectionFeature);
            parentFeatures.Set<ISecureConnectionFeature>(secureConnectionFeature);
            parentFeatures.Set<IServerCommandFeature>(new ServerCommandFeature(_serverCommandChannel));
            parentFeatures.Set<INetworkStreamFeature>(_networkStreamFeature);

            parentFeatures.Set<IFtpConnectionEventHost>(this);
            parentFeatures.Set<IFtpConnectionStatusCheck>(_idleCheck);
            
            parentFeatures.Set<IConnectionIdFeature>(new FtpConnectionIdFeature(ConnectionId));
            parentFeatures.Set<IConnectionLifetimeFeature>(new FtpConnectionLifetimeFeature(this));
            parentFeatures.Set<IConnectionTransportFeature>(transportFeature);
            parentFeatures.Set<IServiceProvidersFeature>(new FtpServiceProviderFeature(serviceProvider));

            var defaultEncoding = options.Value.DefaultEncoding ?? Encoding.ASCII;
            var authInfoFeature = new AuthorizationInformationFeature();

            var features = new FeatureCollection(parentFeatures);
            features.Set<ILocalizationFeature>(new LocalizationFeature(catalogLoader));
            features.Set<IFileSystemFeature>(new FileSystemFeature());
#pragma warning disable 618
            features.Set<IAuthorizationInformationFeature>(authInfoFeature);
#pragma warning restore 618
            features.Set<IConnectionUserFeature>(authInfoFeature);
            features.Set<IEncodingFeature>(new EncodingFeature(defaultEncoding));
            features.Set<ITransferConfigurationFeature>(new TransferConfigurationFeature());

            Features = features;

            _commandReader = ReadCommandsFromPipeline(
                applicationInputPipe.Reader,
                _ftpCommandChannel.Writer,
                _cancellationTokenSource.Token);
        }

        /// <inheritdoc />
        public event EventHandler? Closed;

        /// <inheritdoc />
        [Obsolete("Use the IServiceProvidersFeature")]
        public IServiceProvider ConnectionServices { get; }

        /// <inheritdoc />
        public override string ConnectionId { get; set; }

        /// <summary>
        /// Gets the feature collection.
        /// </summary>
        public override IFeatureCollection Features { get; }

        /// <summary>
        /// Gets the cancellation token to use to signal a task cancellation.
        /// </summary>
        [Obsolete("Use the IConnectionLifetimeFeature")]
        CancellationToken IFtpConnection.CancellationToken => _cancellationTokenSource.Token;

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
                    _serviceProvider.GetRequiredService<ActiveDataConnectionFeatureFactory>();
                var dataConnectionFeature = await activeDataConnectionFeatureFactory
                   .CreateFeatureAsync(null, _remoteEndPoint, _dataPort)
                   .ConfigureAwait(false);
                Features.Set(dataConnectionFeature);

                // Set the checks for the activity information of the FTP connection.
                var checks = _serviceProvider.GetRequiredService<IEnumerable<IFtpConnectionCheck>>().ToList();
                _idleCheck.SetChecks(checks);

                // Connection information
                var connectionFeature = Features.Get<IConnectionEndPointFeature>();
                _logger?.LogInformation("Connected from {remoteIp}", connectionFeature.RemoteEndPoint);

                await _streamWriterService.StartAsync(CancellationToken.None)
                   .ConfigureAwait(false);
                await _streamReaderService.StartAsync(CancellationToken.None)
                   .ConfigureAwait(false);
                await _networkStreamFeature.SecureConnectionAdapterManager.StartAsync(CancellationToken.None)
                   .ConfigureAwait(false);

                _commandChannelReader = CommandChannelDispatcherAsync(
                    _ftpCommandChannel.Reader,
                    _cancellationTokenSource.Token);

                _serverCommandHandler = SendResponsesAsync(_serverCommandChannel, _cancellationTokenSource.Token);
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

                        await _networkStreamFeature.SecureConnectionAdapterManager.StopAsync(CancellationToken.None)
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
                    foreach (var featureItem in Features)
                    {
                        try
                        {
                            switch (featureItem.Value)
                            {
                                case IFtpConnection _:
                                    // Never dispose the connection itself.
                                    break;
                                case IFtpDataConnectionFeature feature:
                                    await feature.DisposeAsync().ConfigureAwait(false);
                                    break;
                                case IDisposable disposable:
                                    disposable.Dispose();
                                    break;
                            }
                        }
                        catch (Exception ex)
                        {
                            // Ignore exceptions
                            _logger?.LogWarning(ex, "Failed to dispose feature of type {featureType}: {errorMessage}", featureItem.Key, ex.Message);
                        }

                        Features[featureItem.Key] = null;
                    }

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

        /// <inheritdoc />
        public IDisposable Subscribe(IObserver<IFtpConnectionEvent> observer)
        {
            var registration = new ObserverRegistration(this, observer);
            lock (_observersLock)
            {
                _observers.Add(registration);
            }

            return registration;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!_connectionClosing)
            {
                Abort();
            }

            // HINT: This code is now used in three different places:
            // - FtpConnection.StopAsync
            // - FtpConnection.Dispose
            // - ReinCommandHandler.Process
            //
            // We really need to clean up this mess!
            // Dispose all features (if disposable)
            foreach (var featureItem in Features)
            {
                try
                {
                    // TODO: Call DisposeAsync on platforms supporting IAsyncDisposable.
                    switch (featureItem.Value)
                    {
                        case IFtpConnection _:
                            // Never dispose the connection itself.
                            break;
                        case IDisposable disposable:
                            disposable.Dispose();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    // Ignore exceptions
                    _logger?.LogWarning(ex, "Failed to dispose feature of type {featureType}: {errorMessage}", featureItem.Key, ex.Message);
                }
            }

            _socket.Dispose();
            _cancellationTokenSource.Dispose();
            _loggerScope?.Dispose();
        }

        /// <summary>
        /// Publish the event.
        /// </summary>
        /// <param name="evt">The event to publish.</param>
        public void PublishEvent(IFtpConnectionEvent evt)
        {
            foreach (var observer in GetObservers())
            {
                observer.OnNext(evt);
            }
        }

        private void Abort()
        {
            if (_connectionClosing)
            {
                return;
            }

            _connectionClosing = true;
            _cancellationTokenSource.Cancel(true);
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
                    _cancellationTokenSource.Cancel();
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
            var dispatcher = _serviceProvider.GetRequiredService<IFtpCommandDispatcher>();
            return dispatcher.DispatchAsync(context, _cancellationTokenSource.Token);
        }

        private void OnClosed()
        {
            Closed?.Invoke(this, new EventArgs());
        }

        private async Task ReadCommandsFromPipeline(
            PipeReader reader,
            ChannelWriter<FtpCommand> commandWriter,
            CancellationToken cancellationToken)
        {
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

                foreach (var observer in GetObservers())
                {
                    observer.OnError(ex);
                }

                Abort();
            }
            finally
            {
                reader.Complete();

                _logger?.LogDebug("Stopped reading commands");

                foreach (var observer in GetObservers())
                {
                    observer.OnCompleted();
                }
            }
        }

        private async Task CommandChannelDispatcherAsync(ChannelReader<FtpCommand> commandReader, CancellationToken cancellationToken)
        {
            // Initialize middleware objects
            var middlewareObjects = _serviceProvider.GetRequiredService<IEnumerable<IFtpMiddleware>>();
            var nextStep = new FtpRequestDelegate(DispatchCommandAsync);
            foreach (var middleware in middlewareObjects.Reverse())
            {
                var tempStep = nextStep;
                nextStep = (context) => middleware.InvokeAsync(context, tempStep);
            }

            var requestDelegate = nextStep;

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
                            PublishEvent(new FtpConnectionCommandReceivedEvent(command));
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

        private List<IObserver<IFtpConnectionEvent>> GetObservers()
        {
            lock (_observersLock)
            {
                return _observers.Select(x => x.Observer).ToList();
            }
        }

        private class IdleCheck : IFtpConnectionStatusCheck
        {
            private readonly IFtpConnection _connection;
            private readonly List<IFtpConnectionCheck> _checks = new List<IFtpConnectionCheck>();

            public IdleCheck(IFtpConnection connection)
            {
                _connection = connection;
            }

            /// <inheritdoc />
            public bool CheckIfAlive()
            {
                var context = new FtpConnectionCheckContext(_connection);
                var checkResults = _checks
                   .Select(x => x.Check(context))
                   .ToArray();
                return checkResults.Select(x => x.IsUsable)
                   .Aggregate(true, (pv, item) => pv && item);
            }

            public void SetChecks(IEnumerable<IFtpConnectionCheck> checks)
            {
                _checks.Clear();
                _checks.AddRange(checks);
            }
        }

        /// <summary>
        /// Observer registration.
        /// </summary>
        private class ObserverRegistration : IDisposable
        {
            private readonly FtpConnection _connection;

            public ObserverRegistration(
                FtpConnection connection,
                IObserver<IFtpConnectionEvent> observer)
            {
                _connection = connection;
                Observer = observer;
            }

            /// <summary>
            /// Gets the registered observer.
            /// </summary>
            public IObserver<IFtpConnectionEvent> Observer { get; }

            /// <inheritdoc />
            public void Dispose()
            {
                lock (_connection._observersLock)
                {
                    _connection._observers.Remove(this);
                }
            }
        }

        private class ConnectionClosingNetworkStreamReader : StreamPipeReaderService
        {
            private readonly CancellationTokenSource _connectionClosedCts;

            public ConnectionClosingNetworkStreamReader(
                Stream stream,
                PipeWriter pipeWriter,
                CancellationTokenSource connectionClosedCts,
                ILogger? logger = null)
                : base(stream, pipeWriter, connectionClosedCts.Token, logger)
            {
                _connectionClosedCts = connectionClosedCts;
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
                _connectionClosedCts.Cancel();
            }
        }

        private class ConnectionFeature
            : IConnectionEndPointFeature,
#pragma warning disable 618
                IConnectionFeature
#pragma warning restore 618
        {
            private IPEndPoint _localEndPoint;
            private IPEndPoint _remoteEndPoint;

            public ConnectionFeature(
                IPEndPoint localEndPoint,
                IPEndPoint remoteEndPoint)
            {
                _localEndPoint = localEndPoint;
                _remoteEndPoint = remoteEndPoint;
            }

            /// <inheritdoc />
            public EndPoint RemoteEndPoint
            {
                get => _remoteEndPoint;
                set => _remoteEndPoint = (IPEndPoint)value;
            }

            /// <inheritdoc />
            public EndPoint LocalEndPoint
            {
                get => _localEndPoint;
                set => _localEndPoint = (IPEndPoint)value;
            }

            /// <inheritdoc />
            IPEndPoint IConnectionFeature.LocalEndPoint => _localEndPoint;

            /// <inheritdoc />
            IPEndPoint IConnectionFeature.RemoteEndPoint => _remoteEndPoint;
        }

        private class SecureConnectionFeature : ISecureConnectionFeature
        {
            /// <inheritdoc />
            public CreateEncryptedStreamDelegate CreateEncryptedStream { get; set; } = Task.FromResult;

            /// <inheritdoc />
            public CloseEncryptedStreamDelegate CloseEncryptedControlStream { get; set; } = ct => Task.CompletedTask;
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

        private class FtpConnectionIdFeature : IConnectionIdFeature
        {
            private readonly string _connectionId;

            public FtpConnectionIdFeature(string connectionId)
            {
                _connectionId = connectionId;
            }

            /// <inheritdoc />
            public string ConnectionId
            {
                get => _connectionId;
                set { }
            }
        }

        private class FtpConnectionTransportFeature : IConnectionTransportFeature
        {
            public FtpConnectionTransportFeature(IDuplexPipe transport)
            {
                Transport = transport;
            }

            /// <inheritdoc />
            public IDuplexPipe Transport { get; set; }
        }

        private class FtpConnectionLifetimeFeature : IConnectionLifetimeFeature
        {
            private readonly FtpConnection _connection;

            public FtpConnectionLifetimeFeature(FtpConnection connection)
            {
                _connection = connection;
                ConnectionClosed = connection._cancellationTokenSource.Token;
            }

            /// <inheritdoc />
            public CancellationToken ConnectionClosed { get; set; }

            /// <inheritdoc />
            public void Abort()
            {
                _connection.Abort();
            }
        }

        private class FtpServiceProviderFeature : IServiceProvidersFeature
        {
            public FtpServiceProviderFeature(
                IServiceProvider serviceProvider)
            {
                RequestServices = serviceProvider;
            }

            /// <inheritdoc />
            public IServiceProvider RequestServices { get; set; }
        }
    }
}
