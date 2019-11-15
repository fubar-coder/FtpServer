// <copyright file="NetworkStreamFeature.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

using FubarDev.FtpServer.ConnectionHandlers;

using Microsoft.AspNetCore.Connections.Features;

namespace FubarDev.FtpServer.Features.Impl
{
    internal class NetworkStreamFeature : INetworkStreamFeature, IResettableFeature
    {
        private readonly IConnectionTransportFeature _transportFeature;

        public NetworkStreamFeature(
            IFtpSecureConnectionAdapterManager secureConnectionAdapterManager,
            IConnectionTransportFeature transportFeature)
        {
            _transportFeature = transportFeature;
            SecureConnectionAdapterManager = secureConnectionAdapterManager;
        }

        /// <inheritdoc />
        public IFtpSecureConnectionAdapterManager SecureConnectionAdapterManager { get; }

        /// <inheritdoc />
        public PipeWriter Output => _transportFeature.Transport.Output;

        /// <inheritdoc />
        public Task ResetAsync(CancellationToken cancellationToken)
        {
            return SecureConnectionAdapterManager.ResetAsync(cancellationToken);
        }
    }
}
