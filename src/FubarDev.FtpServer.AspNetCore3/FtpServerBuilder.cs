// <copyright file="FtpServerBuilder.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using Microsoft.Extensions.DependencyInjection;

namespace FubarDev.FtpServer
{
    /// <summary>
    /// Default implementation of <see cref="IFtpServerBuilder"/>.
    /// </summary>
    internal class FtpServerBuilder : IFtpServerBuilder
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FtpServerBuilder"/> class.
        /// </summary>
        /// <param name="services">The service collection.</param>
        public FtpServerBuilder(IServiceCollection services)
        {
            Services = services;
        }

        /// <inheritdoc />
        public IServiceCollection Services { get; }
    }
}
