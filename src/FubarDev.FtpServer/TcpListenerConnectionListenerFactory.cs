// <copyright file="TcpListenerConnectionListenerFactory.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Connections;

namespace FubarDev.FtpServer
{
    /// <summary>
    /// A connection listener factory that uses a <see cref="TcpListener"/>.
    /// </summary>
    internal class TcpListenerConnectionListenerFactory : IConnectionListenerFactory
    {
        /// <inheritdoc />
        public ValueTask<IConnectionListener> BindAsync(
            EndPoint endpoint,
            CancellationToken cancellationToken = default)
        {
            var listener = new TcpListener((IPEndPoint)endpoint);
            listener.Start();
            return new ValueTask<IConnectionListener>(new TcpListenerConnectionListener(listener));
        }
    }
}
