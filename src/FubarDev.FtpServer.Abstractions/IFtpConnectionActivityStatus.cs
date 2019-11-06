// <copyright file="IFtpConnectionActivityStatus.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;

namespace FubarDev.FtpServer
{
    /// <summary>
    /// Provides the status of the FTP connection.
    /// </summary>
    public interface IFtpConnectionActivityStatus
    {
        /// <summary>
        /// Gets a value indicating whether the connection is still alive.
        /// </summary>
        bool IsAlive { get; }

        /// <summary>
        /// Gets the time of last activity (UTC).
        /// </summary>
        DateTime LastActivityUtc { get; }
    }
}
