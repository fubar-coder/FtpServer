// <copyright file="FtpConnectionCompat.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;
using System.Threading;
using System.Threading.Tasks;

using FubarDev.FtpServer.DataConnection;
using FubarDev.FtpServer.Features;

using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable CS0067
namespace FubarDev.FtpServer.Compatibility
{
    /// <summary>
    /// This is just a compatibility object for the <see cref="IFtpConnection"/> interface.
    /// </summary>
    [Obsolete("There are now other means to access the features.")]
    public class FtpConnectionCompat : IFtpConnection
    {
        private readonly IFeatureCollection _features;

        public FtpConnectionCompat(IFeatureCollection features)
        {
            _features = features;
        }

        /// <inheritdoc />
        public event EventHandler? Closed;

        /// <inheritdoc />
        public IServiceProvider ConnectionServices => _features.GetServiceProvider();

        /// <inheritdoc />
        public IFeatureCollection Features => _features;

        /// <inheritdoc />
        public CancellationToken CancellationToken =>
            _features.Get<IConnectionLifetimeFeature>().ConnectionClosed;

        /// <inheritdoc />
        public void Dispose()
        {
            // Ignore
        }

        /// <inheritdoc />
        public Task StartAsync()
        {
            throw new InvalidOperationException();
        }

        /// <inheritdoc />
        public Task StopAsync()
        {
            throw new InvalidOperationException();
        }

        /// <inheritdoc />
        public async Task<IFtpDataConnection> OpenDataConnectionAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            var serviceProvider = Features.GetServiceProvider();
            var secureDataConnectionWrapper = serviceProvider.GetRequiredService<SecureDataConnectionWrapper>();
            var dataConnectionFeature = Features.Get<IFtpDataConnectionFeature>();
            var dataConnection = await dataConnectionFeature.GetDataConnectionAsync(timeout ?? TimeSpan.FromSeconds(10), cancellationToken)
               .ConfigureAwait(false);
            return await secureDataConnectionWrapper.WrapAsync(dataConnection)
               .ConfigureAwait(false);
        }
    }
}