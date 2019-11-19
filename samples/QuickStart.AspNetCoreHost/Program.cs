//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Fubar Development Junker">
//     Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>
// <author>Mark Junker</author>
//-----------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

using FubarDev.FtpServer;

using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
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

        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
               .ConfigureServices(
                    services =>
                    {
                        services
                           .AddFtpServer(
                                builder => builder
                                   .UseDotNetFileSystem()
                                   .EnableAnonymousAuthentication());
                    })
               .ConfigureKestrel(
                    opt =>
                    {
                        opt.ListenAnyIP(
                            21,
                            lo => { lo.UseConnectionHandler<FtpConnectionHandler>(); });
                    })
               .UseStartup<Startup>();
    }
}
