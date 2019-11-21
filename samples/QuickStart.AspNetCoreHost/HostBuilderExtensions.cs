// <copyright file="HostBuilderExtensions.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

using FubarDev.FtpServer;
using FubarDev.FtpServer.Authentication;
using FubarDev.FtpServer.Features;

using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace QuickStart.AspNetCoreHost
{
    /// <summary>
    /// Extension methods for <see cref="IHostBuilder"/>.
    /// </summary>
    public static class HostBuilderExtensions
    {
        /// <summary>
        /// Enable implicit TLS.
        /// </summary>
        /// <param name="hostBuilder">The host builder to enable implicit TLS for.</param>
        /// <param name="certificate">The certificate to use.</param>
        /// <returns>The modified host builder.</returns>
        public static IHostBuilder EnableImplicitTls(this IHostBuilder hostBuilder, X509Certificate2 certificate)
        {
            return hostBuilder
               .ConfigureServices(
                    services =>
                    {
                        services.Configure<AuthTlsOptions>(
                            opt =>
                            {
                                opt.ImplicitFtps = true;
                                opt.ServerCertificate = certificate;
                            });
                        services.Decorate<IFtpConnectionInitializer>(
                            (initializer, _) =>
                            {
                                initializer.ConfigureConnection += (s, e) =>
                                {
                                    e.AddAsyncInit(
                                        (connection, ct) => ActivateImplicitTls(
                                            connection,
                                            ct,
                                            certificate));
                                };

                                return initializer;
                            });
                    });
        }

        private static async Task ActivateImplicitTls(
            ConnectionContext connectionContext,
            CancellationToken cancellationToken,
            X509Certificate2 certificate)
        {
            var serviceProvider = connectionContext.Features.Get<IServiceProvidersFeature>().RequestServices;
            var secureConnectionAdapterManager = connectionContext.Features
               .Get<INetworkStreamFeature>()
               .SecureConnectionAdapterManager;
            await secureConnectionAdapterManager.EnableSslStreamAsync(certificate, cancellationToken)
               .ConfigureAwait(false);
            var stateMachine = serviceProvider.GetRequiredService<IFtpLoginStateMachine>();
            var authTlsMechanism = serviceProvider.GetRequiredService<IEnumerable<IAuthenticationMechanism>>()
               .Single(x => x.CanHandle("TLS"));
            stateMachine.Activate(authTlsMechanism);
        }
    }
}
