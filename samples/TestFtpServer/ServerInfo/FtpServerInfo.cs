// <copyright file="FtpServerInfo.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;

using FubarDev.FtpServer;

using Microsoft.Extensions.DependencyInjection;

namespace TestFtpServer.ServerInfo
{
    /// <summary>
    /// Information about the FTP server.
    /// </summary>
    public class FtpServerInfo : ISimpleModuleInfo
    {
        private readonly IFtpServer _ftpServer;
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// Initializes a new instance of the <see cref="FtpServerInfo"/>.
        /// </summary>
        /// <param name="ftpServer">The FTP server to get the information from.</param>
        /// <param name="serviceProvider">The service provider.</param>
        public FtpServerInfo(
            IFtpServer ftpServer,
            IServiceProvider serviceProvider)
        {
            _ftpServer = ftpServer;
            _serviceProvider = serviceProvider;
        }

        /// <inheritdoc />
        public string Name { get; } = "server";

        /// <inheritdoc />
        public IEnumerable<(string label, string value)> GetInfo()
        {
            var statistics = _serviceProvider.GetRequiredService<IFtpServerStatistics>();
            yield return ("Server", $"{_ftpServer.Status}");
            yield return ("Port", $"{_ftpServer.Port}");
            yield return ("Active connections", $"{statistics.ActiveConnections}");
            yield return ("Total connections", $"{statistics.TotalConnections}");
        }
    }
}
