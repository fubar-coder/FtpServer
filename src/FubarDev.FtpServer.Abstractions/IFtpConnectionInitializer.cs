// <copyright file="IFtpConnectionInitializer.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;

namespace FubarDev.FtpServer
{
    /// <summary>
    /// Initializer for FTP connections.
    /// </summary>
    public interface IFtpConnectionInitializer
    {
        /// <summary>
        /// This event is raised when the connection is ready to be configured.
        /// </summary>
        event EventHandler<ConnectionEventArgs>? ConfigureConnection;
    }
}
