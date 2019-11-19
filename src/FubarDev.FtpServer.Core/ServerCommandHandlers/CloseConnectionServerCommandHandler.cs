// <copyright file="CloseConnectionServerCommandHandler.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System.Threading;
using System.Threading.Tasks;

using FubarDev.FtpServer.ServerCommands;

using Microsoft.AspNetCore.Connections.Features;

namespace FubarDev.FtpServer.ServerCommandHandlers
{
    /// <summary>
    /// Handler for the <see cref="CloseConnectionServerCommand"/>.
    /// </summary>
    public class CloseConnectionServerCommandHandler : IServerCommandHandler<CloseConnectionServerCommand>
    {
        private readonly IFtpConnectionContextAccessor _connectionContextAccessor;

        /// <summary>
        /// Initializes a new instance of the <see cref="CloseConnectionServerCommandHandler"/> class.
        /// </summary>
        /// <param name="connectionContextAccessor">The FTP connection context accessor.</param>
        public CloseConnectionServerCommandHandler(
            IFtpConnectionContextAccessor connectionContextAccessor)
        {
            _connectionContextAccessor = connectionContextAccessor;
        }

        /// <inheritdoc />
        public Task ExecuteAsync(CloseConnectionServerCommand command, CancellationToken cancellationToken)
        {
            var features = _connectionContextAccessor.Context.Features;
            var lifetimeFeature = features.Get<IConnectionLifetimeFeature>();

            // Just abort the connection. This should avoid problems with an ObjectDisposedException.
            // The "StopAsync" will be called in CommandChannelDispatcherAsync.
            lifetimeFeature.Abort();

            return Task.CompletedTask;
        }
    }
}
