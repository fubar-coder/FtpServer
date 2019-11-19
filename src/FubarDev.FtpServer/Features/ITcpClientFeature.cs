// <copyright file="ITcpClientFeature.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System.Net.Sockets;

namespace FubarDev.FtpServer.Features
{
    /// <summary>
    /// The TCP client feature.
    /// </summary>
    public interface ITcpClientFeature
    {
        /// <summary>
        /// Gets the TCP client.
        /// </summary>
        TcpClient TcpClient { get; }
    }
}
