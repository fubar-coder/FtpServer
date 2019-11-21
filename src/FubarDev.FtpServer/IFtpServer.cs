// <copyright file="IFtpServer.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;

namespace FubarDev.FtpServer
{
    /// <summary>
    /// The interface that must be implemented by the FTP server.
    /// </summary>
    public interface IFtpServer : IPausableFtpService, IFtpConnectionInitializer
    {
        /// <summary>
        /// This event is raised when the listener was started.
        /// </summary>
        event EventHandler<ListenerStartedEventArgs> ListenerStarted;

        /// <summary>
        /// Gets the public IP address (required for <c>PASV</c> and <c>EPSV</c>).
        /// </summary>
        string? ServerAddress { get; }

        /// <summary>
        /// Gets the port on which the FTP server is listening for incoming connections.
        /// </summary>
        /// <remarks>
        /// This value is only final after the <see cref="ListenerStarted"/> event was raised.
        /// </remarks>
        int Port { get; }

        /// <summary>
        /// Gets the max allows active connections.
        /// </summary>
        /// <remarks>
        /// This will cause connections to be refused if count is exceeded.
        /// 0 (default) means no control over connection count.
        /// </remarks>
        int MaxActiveConnections { get; }

        /// <summary>
        /// Gets a value indicating whether server ready to receive incoming connections.
        /// </summary>
        bool Ready { get; }

        /// <summary>
        /// Gets the FTP server statistics.
        /// </summary>
        [Obsolete("Use dependency injection to get the IFtpServerStatistics")]
        IFtpServerStatistics Statistics { get; }
    }
}
