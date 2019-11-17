// <copyright file="SecureConnectionAdapterManager.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System.IO.Pipelines;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

using FubarDev.FtpServer.Authentication;

namespace FubarDev.FtpServer.ConnectionHandlers
{
    /// <summary>
    /// A connection adapter that allows enabling and resetting of an SSL/TLS connection.
    /// </summary>
    internal class SecureConnectionAdapterManager : IFtpSecureConnectionAdapterManager
    {
        private readonly IDuplexPipe _socketPipe;
        private readonly IDuplexPipe _connectionPipe;
        private readonly ISslStreamWrapperFactory _sslStreamWrapperFactory;
        private readonly CancellationToken _connectionClosed;
        private IFtpConnectionAdapter _activeCommunicationService;

        /// <summary>
        /// Initializes a new instance of the <see cref="SecureConnectionAdapterManager"/> class.
        /// </summary>
        /// <param name="socketPipe">The pipe from the socket.</param>
        /// <param name="connectionPipe">The pipe to the connection object.</param>
        /// <param name="sslStreamWrapperFactory">The SSL stream wrapper factory.</param>
        /// <param name="connectionClosed">The cancellation token for a closed connection.</param>
        public SecureConnectionAdapterManager(
            IDuplexPipe socketPipe,
            IDuplexPipe connectionPipe,
            ISslStreamWrapperFactory sslStreamWrapperFactory,
            CancellationToken connectionClosed)
        {
            _socketPipe = socketPipe;
            _connectionPipe = connectionPipe;
            _sslStreamWrapperFactory = sslStreamWrapperFactory;
            _connectionClosed = connectionClosed;
            _activeCommunicationService = new PassThroughConnectionAdapter(
                socketPipe,
                connectionPipe,
                connectionClosed);
        }

        /// <inheritdoc />
        public FtpServiceStatus Status => _activeCommunicationService.Status;

        /// <inheritdoc />
        public Task PauseAsync(CancellationToken cancellationToken)
        {
            return _activeCommunicationService.PauseAsync(cancellationToken);
        }

        /// <inheritdoc />
        public Task ContinueAsync(CancellationToken cancellationToken)
        {
            return _activeCommunicationService.ContinueAsync(cancellationToken);
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            return _activeCommunicationService.StartAsync(cancellationToken);
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return _activeCommunicationService.StopAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task ResetAsync(CancellationToken cancellationToken)
        {
            await StopAsync(cancellationToken)
               .ConfigureAwait(false);
            _activeCommunicationService = new PassThroughConnectionAdapter(
                _socketPipe,
                _connectionPipe,
                _connectionClosed);
            await StartAsync(cancellationToken)
               .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task ActivateAsync(IFtpConnectionAdapter connectionAdapter, CancellationToken cancellationToken)
        {
            await StopAsync(cancellationToken)
               .ConfigureAwait(false);
            try
            {
                // Try to activate the new connection adapter
                _activeCommunicationService = connectionAdapter;
                await StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Use the default (non-encrypting) connection adapter if activation failed
                _activeCommunicationService = new PassThroughConnectionAdapter(
                    _socketPipe,
                    _connectionPipe,
                    _connectionClosed);
                await StartAsync(cancellationToken)
                   .ConfigureAwait(false);
                throw;
            }
        }

        /// <inheritdoc />
        public Task EnableSslStreamAsync(X509Certificate2 certificate, CancellationToken cancellationToken)
        {
            return ActivateAsync(
                new SslStreamConnectionAdapter(
                    _socketPipe,
                    _connectionPipe,
                    _sslStreamWrapperFactory,
                    certificate,
                    _connectionClosed),
                cancellationToken);
        }
    }
}
