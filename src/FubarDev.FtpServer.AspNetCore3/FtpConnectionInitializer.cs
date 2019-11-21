// <copyright file="FtpConnectionInitializer.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;

namespace FubarDev.FtpServer
{
    /// <summary>
    /// Default implementation of <see cref="IFtpConnectionInitializer"/>.
    /// </summary>
    internal class FtpConnectionInitializer : IFtpConnectionInitializer
    {
        /// <inheritdoc />
        public event EventHandler<ConnectionEventArgs>? ConfigureConnection;

        /// <summary>
        /// Called when the connection should be configured.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The event data.</param>
        public void OnConfigureConnection(object sender, ConnectionEventArgs e)
        {
            ConfigureConnection?.Invoke(sender, e);
        }
    }
}
