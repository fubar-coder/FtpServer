// <copyright file="BackgroundTransferService.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System.Threading;
using System.Threading.Tasks;

using FubarDev.FtpServer.BackgroundTransfer;

using Microsoft.Extensions.Hosting;

namespace FubarDev.FtpServer
{
    /// <summary>
    /// Generic host for the FTP server.
    /// </summary>
    internal class BackgroundTransferService : IHostedService
    {
        private readonly IFtpService _backgroundTransferWorker;

        /// <summary>
        /// Initializes a new instance of the <see cref="BackgroundTransferService"/> class.
        /// </summary>
        /// <param name="backgroundTransferWorker">The worker for background transfers.</param>
        public BackgroundTransferService(
            IBackgroundTransferWorker backgroundTransferWorker)
        {
            _backgroundTransferWorker = (IFtpService)backgroundTransferWorker;
        }

        /// <inheritdoc />
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _backgroundTransferWorker.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await _backgroundTransferWorker.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
