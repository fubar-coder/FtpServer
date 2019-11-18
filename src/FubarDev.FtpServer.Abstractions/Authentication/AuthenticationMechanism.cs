// <copyright file="AuthenticationMechanism.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;
using System.Threading;
using System.Threading.Tasks;

using FubarDev.FtpServer.Compatibility;
using FubarDev.FtpServer.Features;

using JetBrains.Annotations;

using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;

namespace FubarDev.FtpServer.Authentication
{
    /// <summary>
    /// The base class for an authentication mechanism.
    /// </summary>
    public abstract class AuthenticationMechanism : IAuthenticationMechanism
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AuthenticationMechanism"/> class.
        /// </summary>
        /// <param name="connection">Fhe FTP connection.</param>
        [Obsolete("Use the constructor accepting the connection context.")]
        protected AuthenticationMechanism(IFtpConnection connection)
        {
            Connection = connection;
            Features = connection.Features;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AuthenticationMechanism"/> class.
        /// </summary>
        /// <param name="context">The FTP connection context.</param>
        protected AuthenticationMechanism(ConnectionContext context)
        {
#pragma warning disable 618
            Connection = new FtpConnectionCompat(context.Features);
#pragma warning restore 618
            Features = context.Features;
        }

        /// <summary>
        /// Gets the FTP connection.
        /// </summary>
        [Obsolete("Access the features directly.")]
        public IFtpConnection Connection { get; }

        /// <summary>
        /// Gets the connection features.
        /// </summary>
        public IFeatureCollection Features { get; }

        /// <inheritdoc />
        public abstract void Reset();

        /// <inheritdoc />
        public abstract bool CanHandle(string methodIdentifier);

        /// <inheritdoc />
        public abstract Task<IFtpResponse> HandleAuthAsync(string methodIdentifier, CancellationToken cancellationToken);

        /// <inheritdoc />
        public abstract Task<IFtpResponse> HandleAdatAsync(byte[] data, CancellationToken cancellationToken);

        /// <inheritdoc />
        public abstract Task<IFtpResponse> HandlePbszAsync(long size, CancellationToken cancellationToken);

        /// <inheritdoc />
        public abstract Task<IFtpResponse> HandleProtAsync(string protCode, CancellationToken cancellationToken);

        /// <summary>
        /// Translates a message using the current catalog of the active connection.
        /// </summary>
        /// <param name="message">The message to translate.</param>
        /// <returns>The translated message.</returns>
        protected string T(string message)
        {
            return Features.Get<ILocalizationFeature>().Catalog.GetString(message);
        }

        /// <summary>
        /// Translates a message using the current catalog of the active connection.
        /// </summary>
        /// <param name="message">The message to translate.</param>
        /// <param name="args">The format arguments.</param>
        /// <returns>The translated message.</returns>
        [StringFormatMethod("message")]
        protected string T(string message, params object[] args)
        {
            return Features.Get<ILocalizationFeature>().Catalog.GetString(message, args);
        }
    }
}
