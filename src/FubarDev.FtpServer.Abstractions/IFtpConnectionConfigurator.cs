// <copyright file="IFtpConnectionConfigurator.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Connections;

namespace FubarDev.FtpServer
{
    /// <summary>
    /// Interface for services that need to reconfigure the connection.
    /// </summary>
    public interface IFtpConnectionConfigurator
    {
        /// <summary>
        /// Changes the connections configuration.
        /// </summary>
        /// <param name="connectionContext">The connection context.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The task.</returns>
        Task Configure(ConnectionContext connectionContext, CancellationToken cancellationToken);
    }
}
