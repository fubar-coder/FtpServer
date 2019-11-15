// <copyright file="INetworkStreamFeature.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;
using System.IO.Pipelines;

using FubarDev.FtpServer.ConnectionHandlers;

namespace FubarDev.FtpServer.Features
{
    /// <summary>
    /// Features two services for reading from and writing to the network stream.
    /// </summary>
    /// <remarks>
    /// The main purpose for this services is the ability to pause and resume
    /// reading/writing from/to the stream to be able to enable TLS on demand.
    /// </remarks>
    internal interface INetworkStreamFeature
    {
        /// <summary>
        /// Gets the connection adapter that encrypts the network stream with an <c>SslStream</c> or something similar.
        /// </summary>
        IFtpSecureConnectionAdapterManager SecureConnectionAdapterManager { get; }

        /// <summary>
        /// Gets the pipe writer for sending the responses.
        /// </summary>
        [Obsolete("Use the IConnectionTransportFeature to get the pipe writer.")]
        PipeWriter Output { get; }
    }
}
