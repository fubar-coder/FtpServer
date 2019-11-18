// <copyright file="ConnectionEventArgs.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;

using Microsoft.AspNetCore.Connections;

namespace FubarDev.FtpServer
{
    /// <summary>
    /// Event arguments for a connection event.
    /// </summary>
    public class ConnectionEventArgs : EventArgs
    {
        private readonly List<ConnectionInitAsyncDelegate> _asyncInitFunctions = new List<ConnectionInitAsyncDelegate>();

        /// <summary>
        /// Initializes a new instance of the <see cref="ConnectionEventArgs"/> class.
        /// </summary>
        /// <param name="connectionContext">The connection context of the event.</param>
        public ConnectionEventArgs(ConnectionContext connectionContext)
        {
            ConnectionContext = connectionContext;
        }

        /// <summary>
        /// Gets the connection context for this event.
        /// </summary>
        public ConnectionContext ConnectionContext { get; }

        /// <summary>
        /// Gets the list of async init functions.
        /// </summary>
        public IEnumerable<ConnectionInitAsyncDelegate> AsyncInitFunctions => _asyncInitFunctions;

        /// <summary>
        /// Adds a new async init function.
        /// </summary>
        /// <param name="asyncInitFunc">The async init function to add.</param>
        public void AddAsyncInit(ConnectionInitAsyncDelegate asyncInitFunc)
        {
            _asyncInitFunctions.Add(asyncInitFunc);
        }
    }
}
