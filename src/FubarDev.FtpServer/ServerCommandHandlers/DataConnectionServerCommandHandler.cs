// <copyright file="DataConnectionServerCommandHandler.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;
using System.Threading;
using System.Threading.Tasks;

using FubarDev.FtpServer.DataConnection;
using FubarDev.FtpServer.Features;
using FubarDev.FtpServer.ServerCommands;
using FubarDev.FtpServer.Statistics;

using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace FubarDev.FtpServer.ServerCommandHandlers
{
    /// <summary>
    /// Handler for the <see cref="DataConnectionServerCommand"/>.
    /// </summary>
    public class DataConnectionServerCommandHandler : IServerCommandHandler<DataConnectionServerCommand>
    {
        private readonly IFtpConnectionContextAccessor _connectionContextAccessor;
        private readonly SecureDataConnectionWrapper _secureDataConnectionWrapper;
        private readonly ILogger<DataConnectionServerCommandHandler>? _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="DataConnectionServerCommandHandler"/> class.
        /// </summary>
        /// <param name="connectionContextAccessor">The FTP connection context accessor.</param>
        /// <param name="secureDataConnectionWrapper">Wraps a data connection into an SSL stream.</param>
        /// <param name="logger">The logger.</param>
        public DataConnectionServerCommandHandler(
            IFtpConnectionContextAccessor connectionContextAccessor,
            SecureDataConnectionWrapper secureDataConnectionWrapper,
            ILogger<DataConnectionServerCommandHandler>? logger = null)
        {
            _connectionContextAccessor = connectionContextAccessor;
            _secureDataConnectionWrapper = secureDataConnectionWrapper;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task ExecuteAsync(DataConnectionServerCommand command, CancellationToken cancellationToken)
        {
            var features = _connectionContextAccessor.Context.Features;
            var serverCommandWriter = features.Get<IServerCommandFeature>().ServerCommandWriter;
            var localizationFeature = features.Get<ILocalizationFeature>();

            using (CreateConnectionKeepAlive(features, command.StatisticsInformation))
            {
                // Try to open the data connection
                IFtpDataConnection dataConnection;
                try
                {
                    var timeout = TimeSpan.FromSeconds(10);
                    var dataConnectionFeature = features.Get<IFtpDataConnectionFeature>();
                    var rawDataConnection = await dataConnectionFeature.GetDataConnectionAsync(timeout, cancellationToken)
                       .ConfigureAwait(false);
                    dataConnection = await _secureDataConnectionWrapper.WrapAsync(rawDataConnection)
                       .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(0, ex, "Could not open data connection: {error}", ex.Message);
                    var errorResponse = new FtpResponse(
                        425,
                        localizationFeature.Catalog.GetString("Could not open data connection"));
                    await serverCommandWriter
                       .WriteAsync(new SendResponseServerCommand(errorResponse), cancellationToken)
                       .ConfigureAwait(false);
                    return;
                }

                // Execute the operation on the data connection.
                var context = new FtpContext(command.Command, serverCommandWriter, features);
                var commandResponse = await context.ExecuteCommand(
                    (_, ct) => command.DataConnectionDelegate(dataConnection, ct),
                    _logger,
                    cancellationToken);
                var response =
                    commandResponse
                    ?? new FtpResponse(226, localizationFeature.Catalog.GetString("Closing data connection."));

                // We have to leave the connection open if the response code is 250.
                if (response.Code != 250)
                {
                    // Close the data connection.
                    await serverCommandWriter
                       .WriteAsync(new CloseDataConnectionServerCommand(dataConnection), cancellationToken)
                       .ConfigureAwait(false);
                }

                // Send the response.
                await serverCommandWriter
                   .WriteAsync(new SendResponseServerCommand(response), cancellationToken)
                   .ConfigureAwait(false);
            }
        }

        private static IDisposable? CreateConnectionKeepAlive(
            IFeatureCollection features,
            FtpFileTransferInformation? information)
        {
            return information == null ? null : new ConnectionKeepAlive(features, information);
        }

        private class ConnectionKeepAlive : IDisposable
        {
            private readonly string _transferId;
            private readonly IFtpStatisticsCollectorFeature _collectorFeature;

            public ConnectionKeepAlive(
                IFeatureCollection features,
                FtpFileTransferInformation information)
            {
                _transferId = information.TransferId;
                _collectorFeature = features.Get<IFtpStatisticsCollectorFeature>();
                _collectorFeature.ForEach(collector => collector.StartFileTransfer(information));
            }

            /// <inheritdoc />
            public void Dispose()
            {
                _collectorFeature.ForEach(collector => collector.StopFileTransfer(_transferId));
            }
        }
    }
}
