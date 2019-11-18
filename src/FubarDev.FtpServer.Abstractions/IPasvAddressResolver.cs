// <copyright file="IPasvAddressResolver.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Connections;

namespace FubarDev.FtpServer
{
    /// <summary>
    /// Interface to get the options for the <c>PASV</c>/<c>EPSV</c> commands.
    /// </summary>
    public interface IPasvAddressResolver
    {
        /// <summary>
        /// Get the <c>PASV</c>/<c>EPSV</c> options.
        /// </summary>
        /// <param name="connectionContext">The FTP connection context.</param>
        /// <param name="addressFamily">The address family for the address to be selected.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The task returning the options.</returns>
        Task<PasvListenerOptions> GetOptionsAsync(
            ConnectionContext connectionContext,
            AddressFamily? addressFamily,
            CancellationToken cancellationToken);
    }
}
