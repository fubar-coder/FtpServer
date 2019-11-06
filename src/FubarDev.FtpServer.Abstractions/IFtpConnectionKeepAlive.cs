// <copyright file="IFtpConnectionKeepAlive.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;

namespace FubarDev.FtpServer
{
    /// <summary>
    /// Interface to ensure that a connection keeps alive.
    /// </summary>
    public interface IFtpConnectionKeepAlive : IFtpConnectionActivityStatus, IFtpConnectionWatchdog
    {
        /// <summary>
        /// Gets or sets a value indicating whether a data transfer is active.
        /// </summary>
        bool IsInDataTransfer { get; set; }
    }
}
