// <copyright file="TcpListenerConnectionListenerFactory.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;

namespace FubarDev.FtpServer
{
    /// <summary>
    /// A connection listener factory that uses a <see cref="TcpListener"/>.
    /// </summary>
    internal class TcpListenerConnectionListenerFactory : IConnectionListenerFactory
    {
        private readonly List<IFtpControlStreamAdapter> _controlStreamAdapters;
        private readonly ILoggerFactory? _loggerFactory;

        public TcpListenerConnectionListenerFactory(
            IEnumerable<IFtpControlStreamAdapter> controlStreamAdapters,
            ILoggerFactory? loggerFactory = null)
        {
            _controlStreamAdapters = controlStreamAdapters.ToList();
            _loggerFactory = loggerFactory;
        }

        /// <inheritdoc />
        public ValueTask<IConnectionListener> BindAsync(
            EndPoint endpoint,
            CancellationToken cancellationToken = default)
        {
            var listener = new TcpListener((IPEndPoint)endpoint);
            listener.Start();

            var connectionListener = new TcpListenerConnectionListener(
                listener,
                _controlStreamAdapters,
                _loggerFactory);
            return new ValueTask<IConnectionListener>(connectionListener);
        }
    }
}
