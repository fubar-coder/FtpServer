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
using FubarDev.FtpServer.ServerCommands;

using Microsoft.AspNetCore.Connections;
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
    internal sealed class FtpConnection
#pragma warning disable 618
        : IFtpConnection
#pragma warning restore 618
    {
        private readonly IServerCommandExecutor _serverCommandExecutor;

        private readonly SecureDataConnectionWrapper _secureDataConnectionWrapper;

        private readonly IDisposable? _loggerScope;

        private readonly Channel<IServerCommand> _serverCommandChannel =
            Channel.CreateBounded<IServerCommand>(new BoundedChannelOptions(5));

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

        private readonly FtpConnectionContext _context;

        private readonly IDisposable _connectionClosedRegistration;

        private bool _connectionClosing;

        private int _connectionClosed;

        private Task? _commandChannelReader;

        private Task? _serverCommandHandler;

        private Task? _commandReader;

        /// <summary>
        /// Initializes a new instance of the <see cref="FtpConnection"/> class.
        /// </summary>
        /// <param name="options">The options for the FTP connection.</param>
        /// <param name="portOptions">The <c>PORT</c> command options.</param>
        /// <param name="connectionAccessor">The accessor to get the connection that is active during the <see cref="FtpCommandHandler.Process"/> method execution.</param>
        /// <param name="connectionContextAccessor">The accessor to get the FTP connection context.</param>
        /// <param name="catalogLoader">The catalog loader for the FTP server.</param>
        /// <param name="serverCommandExecutor">The executor for server commands.</param>
        /// <param name="serviceProvider">The service provider for the connection.</param>
        /// <param name="secureDataConnectionWrapper">Wraps a data connection into an SSL stream.</param>
        /// <param name="sslStreamWrapperFactory">The SSL stream wrapper factory.</param>
        /// <param name="connection">The connection.</param>
        /// <param name="logger">The logger for the FTP connection.</param>
        public FtpConnection(
            IOptions<FtpConnectionOptions> options,
            IOptions<PortCommandOptions> portOptions,
#pragma warning disable 618
            IFtpConnectionAccessor connectionAccessor,
#pragma warning restore 618
            IFtpConnectionContextAccessor connectionContextAccessor,
            IFtpCatalogLoader catalogLoader,
            IServerCommandExecutor serverCommandExecutor,
            IServiceProvider serviceProvider,
            SecureDataConnectionWrapper secureDataConnectionWrapper,
            ISslStreamWrapperFactory sslStreamWrapperFactory,
            ConnectionContext connection,
            ILogger<FtpConnection>? logger = null)
        {
            var defaultEncoding = options.Value.DefaultEncoding ?? Encoding.ASCII;
            var applicationInputPipe = new Pipe();
            var applicationOutputPipe = new Pipe();
            var remoteEndPoint = (IPEndPoint)connection.RemoteEndPoint;

            _context = new FtpConnectionContext(
                catalogLoader,
                defaultEncoding,
                _serverCommandChannel.Writer,
                connection.Transport,
                sslStreamWrapperFactory,
                applicationInputPipe,
                applicationOutputPipe,
                connection.LocalEndPoint,
                remoteEndPoint,
                serviceProvider);

            var tcpClientFeature = connection.Features.Get<ITcpClientFeature?>();
            if (tcpClientFeature?.TcpClient != null)
            {
                connection.Features.Set(tcpClientFeature);
            }

#pragma warning disable 618
            connectionAccessor.FtpConnection = this;
#pragma warning restore 618
            connectionContextAccessor.Context = _context;

            var properties = new Dictionary<string, object?>
            {
                ["RemoteAddress"] = remoteEndPoint.ToString(),
                ["RemoteIp"] = remoteEndPoint.Address.ToString(),
                ["RemotePort"] = remoteEndPoint.Port,
                ["ConnectionId"] = _context.ConnectionId,
            };

            _loggerScope = logger?.BeginScope(properties);

            _dataPort = portOptions.Value.DataPort;
            _serverCommandExecutor = serverCommandExecutor;
            _secureDataConnectionWrapper = secureDataConnectionWrapper;
            _connectionClosedRegistration = connection.ConnectionClosed.Register(() => _context.Abort());

            _logger = logger;
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

        /// <summary>
        /// Gets the connection context.
        /// </summary>
        public ConnectionContext Context => _context;

        /// <inheritdoc />
        public async Task StartAsync()
        {
            await _serviceControl.WaitAsync().ConfigureAwait(false);
            try
            {
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

                await _context.SecureConnectionAdapterManager.StartAsync(CancellationToken.None)
                   .ConfigureAwait(false);

                _commandReader = ReadCommandsFromPipeline(
                    _ftpCommandChannel.Writer,
                    _context.ConnectionClosed);

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

                        if (_commandReader != null)
                        {
                            await _commandReader.ConfigureAwait(false);
                        }

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

            _connectionClosedRegistration.Dispose();
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
                            var context = new FtpContext(command, _serverCommandChannel, _context.Features);
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
    }
}
