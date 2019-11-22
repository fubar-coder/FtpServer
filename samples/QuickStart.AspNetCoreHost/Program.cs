//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Fubar Development Junker">
//     Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>
// <author>Mark Junker</author>
//-----------------------------------------------------------------------

using System.Security.Cryptography.X509Certificates;

using FubarDev.FtpServer;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace QuickStart.AspNetCoreHost
{
    internal static class Program
    {
        public static void Main(string[] args)
        {
            CreateWebHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateWebHostBuilder(string[] args)
        {
            var hostBuilder = Host.CreateDefaultBuilder(args)
               .ConfigureWebHostDefaults(
                    webBuilder =>
                    {
                        webBuilder
                           .ConfigureKestrel(opt => opt.ListenLocalhost(5000))
                           .UseFtpServer(
                                opt => opt
                                   .UseImplicitTls(new X509Certificate2("localhost.pfx"))
                                   .ListenLocalhost(990)
                                   .UseDotNetFileSystem()
                                   .EnableAnonymousAuthentication())
                           .UseStartup<Startup>();
                    });
            return hostBuilder;
        }
    }
}
