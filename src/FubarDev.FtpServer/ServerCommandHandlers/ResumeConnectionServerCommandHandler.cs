// <copyright file="ResumeConnectionServerCommandHandler.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System.Threading;
using System.Threading.Tasks;

using FubarDev.FtpServer.Features;
using FubarDev.FtpServer.ServerCommands;

using Microsoft.Extensions.Logging;

namespace FubarDev.FtpServer.ServerCommandHandlers
{
    /// <summary>
    /// Handler for the <see cref="ResumeConnectionServerCommand"/>.
    /// </summary>
    public class ResumeConnectionServerCommandHandler : IServerCommandHandler<ResumeConnectionServerCommand>
    {
        private readonly IFtpConnectionContextAccessor _connectionContextAccessor;
        private readonly ILogger<ResumeConnectionServerCommandHandler>? _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="ResumeConnectionServerCommandHandler"/> class.
        /// </summary>
        /// <param name="connectionContextAccessor">The FTP connection context accessor.</param>
        /// <param name="logger">The logger.</param>
        public ResumeConnectionServerCommandHandler(
            IFtpConnectionContextAccessor connectionContextAccessor,
            ILogger<ResumeConnectionServerCommandHandler>? logger = null)
        {
            _connectionContextAccessor = connectionContextAccessor;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task ExecuteAsync(ResumeConnectionServerCommand command, CancellationToken cancellationToken)
        {
            var features = _connectionContextAccessor.Context.Features;
            var networkStreamFeature = features.Get<INetworkStreamFeature>();

            await networkStreamFeature.SecureConnectionAdapterManager.ContinueAsync(cancellationToken)
               .ConfigureAwait(false);
            _logger?.LogDebug("Receiver resumed");
        }
    }
}
