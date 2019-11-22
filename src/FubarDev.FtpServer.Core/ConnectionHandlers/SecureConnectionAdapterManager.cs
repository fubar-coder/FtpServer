// <copyright file="SecureConnectionAdapterManager.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System.IO.Pipelines;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

using FubarDev.FtpServer.Authentication;

using Microsoft.AspNetCore.Connections.Features;
using Microsoft.Extensions.Logging;

namespace FubarDev.FtpServer.ConnectionHandlers
{
    /// <summary>
    /// A connection adapter that allows enabling and resetting of an SSL/TLS connection.
    /// </summary>
    internal class SecureConnectionAdapterManager : IFtpSecureConnectionAdapterManager
    {
        private readonly IDuplexPipe _socketPipe;
        private readonly IDuplexPipe _connectionPipe;
        private readonly IDuplexPipe _transportPipe;
        private readonly ISslStreamWrapperFactory _sslStreamWrapperFactory;
        private readonly IConnectionTransportFeature _transportFeature;
        private readonly CancellationToken _connectionClosed;
        private readonly ILoggerFactory? _loggerFactory;
        private IFtpConnectionAdapter _activeCommunicationService;

        /// <summary>
        /// Initializes a new instance of the <see cref="SecureConnectionAdapterManager"/> class.
        /// </summary>
        /// <param name="socketPipe">The pipe from the socket.</param>
        /// <param name="connectionPipe">The pipe to the connection object.</param>
        /// <param name="transportPipe">The new transport pipe.</param>
        /// <param name="sslStreamWrapperFactory">The SSL stream wrapper factory.</param>
        /// <param name="transportFeature">The transport feature to update.</param>
        /// <param name="connectionClosed">The cancellation token for a closed connection.</param>
        /// <param name="loggerFactory">The logger factory.</param>
        public SecureConnectionAdapterManager(
            IDuplexPipe socketPipe,
            IDuplexPipe connectionPipe,
            IDuplexPipe transportPipe,
            ISslStreamWrapperFactory sslStreamWrapperFactory,
            IConnectionTransportFeature transportFeature,
            CancellationToken connectionClosed,
            ILoggerFactory? loggerFactory = null)
        {
            _socketPipe = socketPipe;
            _connectionPipe = connectionPipe;
            _transportPipe = transportPipe;
            _sslStreamWrapperFactory = sslStreamWrapperFactory;
            _transportFeature = transportFeature;
            _transportFeature.Transport = socketPipe;
            _connectionClosed = connectionClosed;
            _loggerFactory = loggerFactory;
            _activeCommunicationService = new PassThroughConnectionAdapter(
                socketPipe,
                connectionPipe,
                connectionClosed,
                loggerFactory);
        }

        /// <inheritdoc />
        public FtpServiceStatus Status => _activeCommunicationService.Status;

        /// <inheritdoc />
        public async Task PauseAsync(CancellationToken cancellationToken)
        {
            await _activeCommunicationService.PauseAsync(cancellationToken)
               .ConfigureAwait(false);
            _transportFeature.Transport = _socketPipe;
        }

        /// <inheritdoc />
        public async Task ContinueAsync(CancellationToken cancellationToken)
        {
            await _activeCommunicationService.ContinueAsync(cancellationToken)
               .ConfigureAwait(false);
            _transportFeature.Transport = _transportPipe;
        }

        /// <inheritdoc />
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (Status == FtpServiceStatus.Running)
            {
                return;
            }

            await _activeCommunicationService.StartAsync(cancellationToken)
               .ConfigureAwait(false);
            _transportFeature.Transport = _transportPipe;
        }

        /// <inheritdoc />
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (Status == FtpServiceStatus.Stopped || Status == FtpServiceStatus.ReadyToRun)
            {
                return;
            }

            await _activeCommunicationService.StopAsync(cancellationToken)
               .ConfigureAwait(false);
            _transportFeature.Transport = _socketPipe;
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
        public Task EnableSslStreamAsync(X509Certificate certificate, CancellationToken cancellationToken)
        {
            return ActivateAsync(
                new SslStreamConnectionAdapter(
                    _socketPipe,
                    _connectionPipe,
                    _sslStreamWrapperFactory,
                    certificate,
                    _connectionClosed,
                    _loggerFactory?.CreateLogger<SslStreamConnectionAdapter>()),
                cancellationToken);
        }
    }
}
