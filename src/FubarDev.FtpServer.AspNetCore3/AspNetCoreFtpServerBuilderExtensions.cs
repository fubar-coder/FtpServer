// <copyright file="AspNetCoreFtpServerBuilderExtensions.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System.Net;

using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace FubarDev.FtpServer
{
    /// <summary>
    /// Extension methods for the FTP server builder.
    /// </summary>
    public static class AspNetCoreFtpServerBuilderExtensions
    {
        /// <summary>
        /// Configures that the server should listen on localhost.
        /// </summary>
        /// <param name="builder">The FTP server builder.</param>
        /// <param name="port">The port.</param>
        /// <returns>The configured FTP server builder.</returns>
        public static IFtpServerBuilder ListenLocalhost(this IFtpServerBuilder builder, int port)
        {
            builder.Services.Configure<KestrelServerOptions>(
                opt => opt.ListenLocalhost(
                    port,
                    lo => lo.UseConnectionHandler<FtpConnectionHandler>()));
            return builder;
        }

        /// <summary>
        /// Configures that the server should listen on any IP address.
        /// </summary>
        /// <param name="builder">The FTP server builder.</param>
        /// <param name="port">The port.</param>
        /// <returns>The configured FTP server builder.</returns>
        public static IFtpServerBuilder ListenAnyIP(this IFtpServerBuilder builder, int port)
        {
            builder.Services.Configure<KestrelServerOptions>(
                opt => opt.ListenAnyIP(
                    port,
                    lo => lo.UseConnectionHandler<FtpConnectionHandler>()));
            return builder;
        }

        /// <summary>
        /// Configures that the server should listen on the given address.
        /// </summary>
        /// <param name="builder">The FTP server builder.</param>
        /// <param name="address">The address to listen on.</param>
        /// <param name="port">The port.</param>
        /// <returns>The configured FTP server builder.</returns>
        public static IFtpServerBuilder Listen(this IFtpServerBuilder builder, IPAddress address, int port)
        {
            builder.Services.Configure<KestrelServerOptions>(
                opt => opt.Listen(
                    address,
                    port,
                    lo => lo.UseConnectionHandler<FtpConnectionHandler>()));
            return builder;
        }

        /// <summary>
        /// Configures that the server should listen on the given end point.
        /// </summary>
        /// <param name="builder">The FTP server builder.</param>
        /// <param name="endPoint">The end point to listen on.</param>
        /// <returns>The configured FTP server builder.</returns>
        public static IFtpServerBuilder Listen(this IFtpServerBuilder builder, IPEndPoint endPoint)
        {
            builder.Services.Configure<KestrelServerOptions>(
                opt => opt.Listen(
                    endPoint,
                    lo => lo.UseConnectionHandler<FtpConnectionHandler>()));
            return builder;
        }
    }
}
