// <copyright file="SendResponseServerCommandHandler.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;
using System.Threading;
using System.Threading.Tasks;

using FubarDev.FtpServer.Features;
using FubarDev.FtpServer.ServerCommands;

using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace FubarDev.FtpServer.ServerCommandHandlers
{
    /// <summary>
    /// Command handler for the <see cref="SendResponseServerCommand"/>.
    /// </summary>
    public class SendResponseServerCommandHandler : IServerCommandHandler<SendResponseServerCommand>
    {
        private readonly IFtpConnectionContextAccessor _connectionContextAccessor;
        private readonly ILogger<SendResponseServerCommandHandler>? _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="SendResponseServerCommandHandler"/> class.
        /// </summary>
        /// <param name="connectionContextAccessor">The FTP connection context accessor.</param>
        /// <param name="logger">The logger.</param>
        public SendResponseServerCommandHandler(
            IFtpConnectionContextAccessor connectionContextAccessor,
            ILogger<SendResponseServerCommandHandler>? logger = null)
        {
            _connectionContextAccessor = connectionContextAccessor;
            _logger = logger;
        }

        /// <inheritdoc />
        public Task ExecuteAsync(SendResponseServerCommand command, CancellationToken cancellationToken)
        {
            return WriteResponseAsync(_connectionContextAccessor.Context.Features, command.Response, cancellationToken);
        }

        private async Task WriteResponseAsync(
            IFeatureCollection features,
            IFtpResponse response,
            CancellationToken cancellationToken)
        {
            var transportFeature = features.Get<IConnectionTransportFeature>();
            var encoding = features.Get<IEncodingFeature>().Encoding;

            var writer = transportFeature.Transport.Output;

            _logger?.Log(response);

            await foreach (var line in response.GetLinesAsync(cancellationToken).ConfigureAwait(false))
            {
                var data = encoding.GetBytes($"{line}\r\n");
                var memory = writer.GetMemory(data.Length);
                data.AsSpan().CopyTo(memory.Span);
                writer.Advance(data.Length);
                var flushResult = await writer.FlushAsync(cancellationToken);
                if (flushResult.IsCanceled || flushResult.IsCompleted)
                {
                    break;
                }
            }
        }
    }
}
