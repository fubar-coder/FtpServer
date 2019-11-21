// <copyright file="TcpListenerConnectionListener.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;

namespace FubarDev.FtpServer
{
    internal class TcpListenerConnectionListener : IConnectionListener
    {
        private readonly TcpListener _listener;
        private readonly ILoggerFactory? _loggerFactory;

        public TcpListenerConnectionListener(
            TcpListener listener,
            ILoggerFactory? loggerFactory = null)
        {
            _listener = listener;
            _loggerFactory = loggerFactory;
            EndPoint = listener.LocalEndpoint;
        }

        /// <inheritdoc />
        public EndPoint EndPoint { get; }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            _listener.Stop();
            return default;
        }

        /// <inheritdoc />
#nullable disable
        public async ValueTask<ConnectionContext> AcceptAsync(CancellationToken cancellationToken = default)
#nullable restore
        {
            while (true)
            {
                try
                {
                    var tcpClient = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);

                    // Only apply no delay to Tcp based endpoints
                    if (tcpClient.Client.LocalEndPoint is IPEndPoint)
                    {
                        tcpClient.Client.NoDelay = true;
                    }

                    var connection = new TcpClientConnection(tcpClient, _loggerFactory);
                    await connection.StartAsync();
                    return connection;
                }
                catch (ObjectDisposedException)
                {
                    // A call was made to UnbindAsync/DisposeAsync just return null which signals we're done
                    return null;
                }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.OperationAborted)
                {
                    // A call was made to UnbindAsync/DisposeAsync just return null which signals we're done
                    return null;
                }
                catch (SocketException)
                {
                    // The connection got reset while it was in the backlog, so we try again.
                }
            }
        }

        /// <inheritdoc />
        public ValueTask UnbindAsync(CancellationToken cancellationToken = default)
        {
            _listener.Stop();
            return default;
        }
    }
}
