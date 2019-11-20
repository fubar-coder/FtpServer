// <copyright file="WebHostBuilderExtensions.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace FubarDev.FtpServer
{
    /// <summary>
    /// Extension methods for the <see cref="IWebHostBuilder"/>.
    /// </summary>
    public static class WebHostBuilderExtensions
    {
        /// <summary>
        /// Uses the FTP server.
        /// </summary>
        /// <param name="builder">The web host builder.</param>
        /// <param name="configure">The configuration action.</param>
        /// <returns>The configured web host builder.</returns>
        public static IWebHostBuilder UseFtpServer(this IWebHostBuilder builder, Action<IFtpServerBuilder> configure)
        {
            return builder.ConfigureServices(services => services.AddFtpServer(configure));
        }
    }
}
