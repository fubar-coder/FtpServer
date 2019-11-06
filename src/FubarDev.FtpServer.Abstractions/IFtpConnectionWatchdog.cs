// <copyright file="IFtpConnectionWatchdog.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;

namespace FubarDev.FtpServer
{
    /// <summary>
    /// A simple watchdog interface.
    /// </summary>
    public interface IFtpConnectionWatchdog
    {
        /// <summary>
        /// Returns a new disposable object that is used to track data transfers.
        /// </summary>
        IDisposable RegisterDataTransfer();

        /// <summary>
        /// Ensure that the connection keeps alive.
        /// </summary>
        void KeepAlive();
    }
}
