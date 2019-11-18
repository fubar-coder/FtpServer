// <copyright file="ConnectionInitAsyncDelegate.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Connections;

namespace FubarDev.FtpServer
{
    /// <summary>
    /// Async delegate to initialize a connection.
    /// </summary>
    /// <param name="connectionContext">The connection context to initialize.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The task for the asynchronous operation.</returns>
    public delegate Task ConnectionInitAsyncDelegate(ConnectionContext connectionContext, CancellationToken cancellationToken);
}
