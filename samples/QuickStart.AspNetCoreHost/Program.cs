//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Fubar Development Junker">
//     Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>
// <author>Mark Junker</author>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

using FubarDev.FtpServer;
using FubarDev.FtpServer.Authentication;
using FubarDev.FtpServer.Features;

using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace QuickStart.AspNetCoreHost
{
    internal static class Program
    {
        public static void Main(string[] args)
        {
            CreateWebHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateWebHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
               .ConfigureWebHostDefaults(
                    webBuilder =>
                    {
                        webBuilder
                           .ConfigureKestrel(opt => opt.ListenLocalhost(5000))
                           .UseFtpServer(
                                opt => opt
                                   .ListenLocalhost(990)
                                   .UseDotNetFileSystem()
                                   .EnableAnonymousAuthentication())
                           .UseStartup<Startup>();
                    })
               .ConfigureServices(
                    services =>
                    {
                        var implicitFtpsCertificate = new X509Certificate2("localhost.pfx");
                        services.Configure<AuthTlsOptions>(
                            opt =>
                            {
                                opt.ImplicitFtps = true;
                                opt.ServerCertificate = implicitFtpsCertificate;
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
                                            implicitFtpsCertificate));
                                };

                                return initializer;
                            });
                    });

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
