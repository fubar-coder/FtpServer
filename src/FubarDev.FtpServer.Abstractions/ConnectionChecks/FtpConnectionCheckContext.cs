// <copyright file="FtpConnectionCheckContext.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using Microsoft.AspNetCore.Http.Features;

namespace FubarDev.FtpServer.ConnectionChecks
{
    /// <summary>
    /// The context of the FTP connection check.
    /// </summary>
    public class FtpConnectionCheckContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FtpConnectionCheckContext"/> class.
        /// </summary>
        /// <param name="features">The FTP connection features.</param>
        public FtpConnectionCheckContext(IFeatureCollection features)
        {
            Features = features;
        }

        /// <summary>
        /// Gets the FTP connection.
        /// </summary>
        public IFeatureCollection Features { get; }
    }
}
