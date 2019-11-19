// <copyright file="FtpConnectionHandler.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

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

using FubarDev.FtpServer;
using FubarDev.FtpServer.Authentication;
using FubarDev.FtpServer.Commands;
using FubarDev.FtpServer.Compatibility;
using FubarDev.FtpServer.ConnectionChecks;
using FubarDev.FtpServer.DataConnection;
using FubarDev.FtpServer.Features;
using FubarDev.FtpServer.Localization;
using FubarDev.FtpServer.ServerCommands;

using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace QuickStart.AspNetCoreHost
{
    public class FtpConnectionHandler : ConnectionHandler
    {
#pragma warning disable 618
        private readonly IFtpConnectionAccessor _connectionAccessor;
#pragma warning restore 618
        private readonly IFtpConnectionContextAccessor _connectionContextAccessor;
        private readonly IFtpCatalogLoader _catalogLoader;
        private readonly IServerCommandExecutor _serverCommandExecutor;
        private readonly IServiceProvider _serviceProvider;
        private readonly ISslStreamWrapperFactory _sslStreamWrapperFactory;
        private readonly ActiveDataConnectionFeatureFactory _activeDataConnectionFeatureFactory;
        private readonly IFtpServerMessages _ftpServerMessages;
        private readonly ILogger<FtpConnection>? _logger;
        private readonly PortCommandOptions _portOptions;
        private readonly FtpConnectionOptions _options;

        public FtpConnectionHandler(
            IOptions<FtpConnectionOptions> options,
            IOptions<PortCommandOptions> portOptions,
#pragma warning disable 618
            IFtpConnectionAccessor connectionAccessor,
#pragma warning restore 618
            IFtpConnectionContextAccessor connectionContextAccessor,
            IFtpCatalogLoader catalogLoader,
            IServerCommandExecutor serverCommandExecutor,
            IServiceProvider serviceProvider,
            ISslStreamWrapperFactory sslStreamWrapperFactory,
            ActiveDataConnectionFeatureFactory activeDataConnectionFeatureFactory,
            IFtpServerMessages ftpServerMessages,
            ILogger<FtpConnection>? logger = null)
        {
            _connectionAccessor = connectionAccessor;
            _connectionContextAccessor = connectionContextAccessor;
            _catalogLoader = catalogLoader;
            _serverCommandExecutor = serverCommandExecutor;
            _serviceProvider = serviceProvider;
            _sslStreamWrapperFactory = sslStreamWrapperFactory;
            _activeDataConnectionFeatureFactory = activeDataConnectionFeatureFactory;
            _ftpServerMessages = ftpServerMessages;
            _logger = logger;
            _portOptions = portOptions.Value;
            _options = options.Value;
        }

        /// <inheritdoc />
        public override async Task OnConnectedAsync(ConnectionContext connection)
        {
            var serverCommandChannel = Channel.CreateBounded<IServerCommand>(new BoundedChannelOptions(5));
            var ftpCommandChannel = Channel.CreateBounded<FtpCommand>(5);

            using var scope = _serviceProvider.CreateScope();
            var defaultEncoding = _options.DefaultEncoding ?? Encoding.ASCII;
            var applicationInputPipe = new Pipe();
            var applicationOutputPipe = new Pipe();
            var connectionPipe = new DuplexPipe(applicationOutputPipe.Reader, applicationInputPipe.Writer);
            var transport = new DuplexPipe(applicationInputPipe.Reader, applicationOutputPipe.Writer);

            var context = new FtpConnectionContext(
                _catalogLoader,
                defaultEncoding,
                serverCommandChannel.Writer,
                connection.Transport,
                connectionPipe,
                _sslStreamWrapperFactory,
                transport,
                connection.LocalEndPoint,
                connection.RemoteEndPoint,
                scope.ServiceProvider);

            using var connClosedReg = connection.ConnectionClosed.Register(() => context.Abort());

#pragma warning disable 618
            _connectionAccessor.FtpConnection = new FtpConnectionCompat(context.Features);
#pragma warning restore 618
            _connectionContextAccessor.Context = context;

            var remoteEndPoint = (IPEndPoint)connection.RemoteEndPoint;
            var dataPort = _portOptions.DataPort;

            var properties = new Dictionary<string, object?>
            {
                ["RemoteAddress"] = remoteEndPoint.ToString(),
                ["RemoteIp"] = remoteEndPoint.Address.ToString(),
                ["RemotePort"] = remoteEndPoint.Port,
                ["ConnectionId"] = context.ConnectionId,
            };

            using var loggerScope = _logger?.BeginScope(properties);
            var commandReader = ReadCommandsFromPipeline(context, ftpCommandChannel.Writer);

            // Set the default FTP data connection feature
            var dataConnectionFeature = await _activeDataConnectionFeatureFactory
               .CreateFeatureAsync(null, remoteEndPoint, dataPort)
               .ConfigureAwait(false);
            context.Features.Set(dataConnectionFeature);

            // Set the checks for the activity information of the FTP connection.
            var checks = scope.ServiceProvider.GetRequiredService<IEnumerable<IFtpConnectionCheck>>().ToList();
            context.SetChecks(checks);

            // Connection information
            _logger?.LogInformation("Connected from {remoteIp}", remoteEndPoint);

            await context.SecureConnectionAdapterManager.StartAsync(CancellationToken.None)
               .ConfigureAwait(false);

            var commandChannelReader = CommandChannelDispatcherAsync(context, ftpCommandChannel.Reader);
            var serverCommandHandler = SendResponsesAsync(context, serverCommandChannel.Reader);

            var response = new FtpResponseTextBlock(220, _ftpServerMessages.GetBannerMessage());

            // Send initial response
            await serverCommandChannel.Writer.WriteAsync(
                    new SendResponseServerCommand(response),
                    context.ConnectionClosed)
               .ConfigureAwait(false);

            await commandReader.ConfigureAwait(false);
            serverCommandChannel.Writer.Complete();

            await commandChannelReader.ConfigureAwait(false);
            await serverCommandHandler.ConfigureAwait(false);
            await context.SecureConnectionAdapterManager.StopAsync(CancellationToken.None)
               .ConfigureAwait(false);

            // Dispose all features (if disposable)
            await context.Features.ResetAsync(CancellationToken.None, _logger).ConfigureAwait(false);

            _logger?.LogInformation("Connection closed");
        }

        /// <summary>
        /// Send responses to the client.
        /// </summary>
        /// <param name="connectionContext">The FTP connection context.</param>
        /// <param name="serverCommandReader">The server command reader.</param>
        /// <returns>The task.</returns>
        private async Task SendResponsesAsync(
            FtpConnectionContext connectionContext,
            ChannelReader<IServerCommand> serverCommandReader)
        {
            var cancellationToken = connectionContext.ConnectionClosed;
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
                    connectionContext.Abort();
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
        /// Reads commands from the transport pipeline and adds them to the command channel.
        /// </summary>
        /// <param name="connectionContext">The connection context.</param>
        /// <param name="commandWriter"></param>
        /// <returns></returns>
        private async Task ReadCommandsFromPipeline(
            FtpConnectionContext connectionContext,
            ChannelWriter<FtpCommand> commandWriter)
        {
            var cancellationToken = connectionContext.ConnectionClosed;
            var reader = connectionContext.Transport.Input;
            var collector = new FtpCommandCollector(() => connectionContext.Encoding);

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
                connectionContext.Abort();
            }
            catch (Exception ex) when (ex.Is<IOException>())
            {
                // Most likely closed by server.
                _logger?.LogWarning("Connection lost or closed by server");
                connectionContext.Abort();
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
                connectionContext.Abort();
            }
            finally
            {
                reader.Complete();
                _logger?.LogDebug("Stopped reading commands");
            }
        }

        private async Task CommandChannelDispatcherAsync(
            FtpConnectionContext connectionContext,
            ChannelReader<FtpCommand> commandReader)
        {
            var cancellationToken = connectionContext.ConnectionClosed;
            var ftpCommandDispatcher = connectionContext.RequestServices.GetRequiredService<IFtpCommandDispatcher>();


            // Initialize middleware objects
            var middlewareObjects = connectionContext.RequestServices.GetRequiredService<IEnumerable<IFtpMiddleware>>();
            var nextStep = new FtpRequestDelegate(
                c => DispatchCommandAsync(c, ftpCommandDispatcher, cancellationToken));
            foreach (var middleware in middlewareObjects.Reverse())
            {
                var tempStep = nextStep;
                nextStep = (context) => middleware.InvokeAsync(context, tempStep);
            }

            var requestDelegate = nextStep;

            // Statistics feature
            var statisticsCollectorFeature = (IFtpStatisticsCollectorFeature)connectionContext;
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
                    var backgroundTaskLifetimeService = connectionContext.Features.Get<IBackgroundTaskLifetimeFeature?>();
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
                        connectionContext.Features.Set<IBackgroundTaskLifetimeFeature?>(null);
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
                            var context = new FtpContext(
                                command,
                                connectionContext.ServerCommandWriter,
                                connectionContext.Features);
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
                connectionContext.Abort();
            }
        }

        /// <summary>
        /// Final (default) dispatch from FTP commands to the handlers.
        /// </summary>
        /// <param name="context">The context for the FTP command execution.</param>
        /// <param name="ftpCommandDispatcher">The FTP command dispatcher.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The task.</returns>
        private Task DispatchCommandAsync(
            FtpContext context,
            IFtpCommandDispatcher ftpCommandDispatcher,
            CancellationToken cancellationToken)
        {
            return ftpCommandDispatcher.DispatchAsync(context, cancellationToken);
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
