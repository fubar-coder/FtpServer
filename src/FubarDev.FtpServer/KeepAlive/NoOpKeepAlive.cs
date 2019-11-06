// <copyright file="NoOpKeepAlive.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;

namespace FubarDev.FtpServer.KeepAlive
{
    /// <summary>
    /// A no-op keep-alive for connections.
    /// </summary>
    /// <remarks>
    /// The connections are always seen as "alive".
    /// </remarks>
    public class NoOpKeepAlive : IFtpConnectionKeepAlive
    {
        /// <inheritdoc />
        public bool IsAlive => true;

        /// <inheritdoc />
        public DateTime LastActivityUtc => DateTime.UtcNow;

        /// <inheritdoc />
        public bool IsInDataTransfer { get; set; }

        /// <inheritdoc />
        public void KeepAlive()
        {
            // Nothing to do here.
        }
    }
}
