// <copyright file="IFtpConnectionAdapter.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

namespace FubarDev.FtpServer.ConnectionHandlers
{
    /// <summary>
    /// An FTP connection adapter does something with the input and output pipes.
    /// </summary>
    /// <remarks>
    /// It uses a sender and a receiver service to be able to start/stop the tasks.
    /// </remarks>
    public interface IFtpConnectionAdapter : IPausableFtpService
    {
    }
}
