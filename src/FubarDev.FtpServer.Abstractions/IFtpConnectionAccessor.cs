// <copyright file="IFtpConnectionAccessor.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;

namespace FubarDev.FtpServer
{
    /// <summary>
    /// Accessor to get/set the current (scoped) FTP connection.
    /// </summary>
    [Obsolete("Use the IFtpConnectionContextAccessor instead.")]
    public interface IFtpConnectionAccessor
    {
        /// <summary>
        /// Gets or sets the current FTP connection.
        /// </summary>
        [Obsolete("Use the IFtpConnectionContextAccessor.Context instead.")]
        IFtpConnection FtpConnection { get; set; }
    }
}
