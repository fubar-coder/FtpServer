// <copyright file="TcpSocketKeepAlive.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;
using System.Net.Sockets;

using Microsoft.Extensions.DependencyInjection;

namespace FubarDev.FtpServer.KeepAlive
{
    /// <summary>
    /// Determines the status of the connection using the TCP socket.
    /// </summary>
    public class TcpSocketKeepAlive : IFtpConnectionKeepAlive
    {
        private readonly object _lock = new object();
        private readonly IFtpConnection _connection;
        private DateTime _lastActivityUtc = DateTime.UtcNow;

        /// <summary>
        /// Initializes a new instance of the <see cref="TcpSocketKeepAlive"/> class.
        /// </summary>
        /// <param name="connection">The FTP connection.</param>
        public TcpSocketKeepAlive(IFtpConnection connection)
        {
            _connection = connection;
        }

        /// <inheritdoc />
        public bool IsAlive => UpdateStatus();

        /// <inheritdoc />
        public DateTime LastActivityUtc
        {
            get
            {
                lock (_lock)
                {
                    return _lastActivityUtc;
                }
            }
        }

        /// <inheritdoc />
        public bool IsInDataTransfer { get; set; }

        /// <inheritdoc />
        public void KeepAlive()
        {
            // Keep-alive status is determined by querying the socket itself.
        }

        private bool UpdateStatus()
        {
            if (IsSocketConnectionEstablished())
            {
                lock (_lock)
                {
                    _lastActivityUtc = DateTime.UtcNow;
                }

                return true;
            }

            return false;
        }

        private bool IsSocketConnectionEstablished()
        {
            try
            {
                var socketAccessor = _connection.ConnectionServices.GetRequiredService<TcpSocketClientAccessor>();
                var client = socketAccessor?.TcpSocketClient;
                if (client == null)
                {
                    return false;
                }

                // https://docs.microsoft.com/en-us/dotnet/api/system.net.sockets.socket.connected?view=netframework-4.7.2
                // Quote:
                // If you need to determine the current state of the connection, make a nonblocking, zero-byte
                // Send call. If the call returns successfully or throws a WAEWOULDBLOCK error code (10035),
                // then the socket is still connected; otherwise, the socket is no longer connected.
                client.Client.Send(Array.Empty<byte>(), 0, 0, SocketFlags.None, out var socketError);
                return socketError == SocketError.Success || socketError == SocketError.WouldBlock;
            }
            catch
            {
                // Any error means that the connection isn't usable anymore.
                return false;
            }
        }
    }
}
