// <copyright file="FtpServerListenerService.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FubarDev.FtpServer.Networking
{
    /// <summary>
    /// Listener for the server.
    /// </summary>
    /// <remarks>
    /// Accepting connections can be paused.
    /// </remarks>
    internal sealed class FtpServerListenerService : PausableFtpService
    {
        private readonly ChannelWriter<ConnectionContext> _newClientWriter;
        private readonly MultiBindingTcpListener _multiBindingTcpListener;

        private readonly CancellationTokenSource _connectionClosedCts;

        private Exception? _exception;

        /// <summary>
        /// Initializes a new instance of the <see cref="FtpServerListenerService"/> class.
        /// </summary>
        /// <param name="newClientWriter">Channel that receives all accepted clients.</param>
        /// <param name="serverOptions">The server options.</param>
        /// <param name="connectionClosedCts">Cancellation token source for a closed connection.</param>
        /// <param name="connectionListenerFactory">The connection listener factory.</param>
        /// <param name="loggerFactory">The logger factory.</param>
        /// <param name="logger">The logger.</param>
        public FtpServerListenerService(
            ChannelWriter<ConnectionContext> newClientWriter,
            IOptions<FtpServerOptions> serverOptions,
            CancellationTokenSource connectionClosedCts,
            ILoggerFactory? loggerFactory = null,
            IConnectionListenerFactory? connectionListenerFactory = null,
            ILogger? logger = null)
            : base(connectionClosedCts.Token, logger)
        {
            _newClientWriter = newClientWriter;
            _connectionClosedCts = connectionClosedCts;
            var options = serverOptions.Value;
            _multiBindingTcpListener = new MultiBindingTcpListener(
                options.ServerAddress,
                options.Port,
                connectionListenerFactory,
                loggerFactory,
                logger);
        }

        /// <summary>
        /// Event for a started listener.
        /// </summary>
        public event EventHandler<ListenerStartedEventArgs>? ListenerStarted;

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            await _multiBindingTcpListener.StartAsync().ConfigureAwait(false);

            // Notify of the port that's used by the listener.
            OnListenerStarted(new ListenerStartedEventArgs(_multiBindingTcpListener.Port));

            _multiBindingTcpListener.StartAccepting();

            try
            {
                while (true)
                {
                    var client = await _multiBindingTcpListener.WaitAnyTcpClientAsync(cancellationToken)
                       .ConfigureAwait(false);
                    await _newClientWriter.WriteAsync(client, cancellationToken)
                       .ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex.Is<OperationCanceledException>())
            {
                // Ignore - everything is fine
            }
            finally
            {
                await _multiBindingTcpListener.StopAsync().ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        protected override async Task<bool> OnFailedAsync(Exception exception, CancellationToken cancellationToken)
        {
            await base.OnFailedAsync(exception, cancellationToken)
               .ConfigureAwait(false);

            _exception = exception;

            return true;
        }

        /// <inheritdoc />
        protected override Task OnStoppedAsync(CancellationToken cancellationToken)
        {
            // Tell the channel that there's no more data coming
            _newClientWriter.Complete(_exception);

            // Signal a closed connection.
            _connectionClosedCts.Cancel();

            return Task.CompletedTask;
        }

        private void OnListenerStarted(ListenerStartedEventArgs e)
        {
            ListenerStarted?.Invoke(this, e);
        }
    }
}
