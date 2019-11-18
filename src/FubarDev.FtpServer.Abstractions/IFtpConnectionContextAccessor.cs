// <copyright file="IFtpConnectionContextAccessor.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using Microsoft.AspNetCore.Connections;

namespace FubarDev.FtpServer
{
    /// <summary>
    /// Interface to query the current connection context.
    /// </summary>
    public interface IFtpConnectionContextAccessor
    {
        /// <summary>
        /// Gets or sets the current connection context.
        /// </summary>
        ConnectionContext Context { get; set; }
    }
}
