// <copyright file="SimplePasvAddressResolver.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.Extensions.Options;

namespace FubarDev.FtpServer
{
    /// <summary>
    /// The default implementation of the <see cref="SimplePasvAddressResolver"/>.
    /// </summary>
    /// <remarks>
    /// The address family number gets ignored by this resolver. We always use the same
    /// address family as the local end point.
    /// </remarks>
    public class SimplePasvAddressResolver : IPasvAddressResolver
    {
        private readonly SimplePasvOptions _options;

        /// <summary>
        /// Initializes a new instance of the <see cref="SimplePasvAddressResolver"/> class.
        /// </summary>
        /// <param name="options">The options.</param>
        public SimplePasvAddressResolver(IOptions<SimplePasvOptions> options)
        {
            _options = options.Value;
        }

        /// <inheritdoc />
        public Task<PasvListenerOptions> GetOptionsAsync(
            ConnectionContext connectionContext,
            AddressFamily? addressFamily,
            CancellationToken cancellationToken)
        {
            var minPort = _options.PasvMinPort ?? 0;
            if (minPort > 0 && minPort < 1024)
            {
                minPort = 1024;
            }

            var maxPort = Math.Max(_options.PasvMaxPort ?? 0, minPort);
            IPAddress publicAddress;
            if (_options.PublicAddress != null)
            {
                publicAddress = _options.PublicAddress;
            }
            else
            {
                var connectionFeature = connectionContext.Features.Get<IConnectionEndPointFeature>();
                var localIpEndPoint = (IPEndPoint)connectionFeature.LocalEndPoint;
                publicAddress = localIpEndPoint.Address;
            }

            return Task.FromResult(new PasvListenerOptions(minPort, maxPort, publicAddress));
        }
    }
}
