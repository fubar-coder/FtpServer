// <copyright file="AuthorizationMechanism.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;
using System.Threading;
using System.Threading.Tasks;

using FubarDev.FtpServer.Authentication;
using FubarDev.FtpServer.Compatibility;
using FubarDev.FtpServer.Features;

using JetBrains.Annotations;

using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;

namespace FubarDev.FtpServer.Authorization
{
    /// <summary>
    /// The base class for an authorization mechanism.
    /// </summary>
    public abstract class AuthorizationMechanism : IAuthorizationMechanism
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AuthorizationMechanism"/> class.
        /// </summary>
        /// <param name="connection">The required FTP connection.</param>
        [Obsolete("Use the constructor accepting the connection context.")]
        protected AuthorizationMechanism(IFtpConnection connection)
        {
            Connection = connection;
            Features = connection.Features;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AuthorizationMechanism"/> class.
        /// </summary>
        /// <param name="connectionContext">The FTP connection context.</param>
        protected AuthorizationMechanism(ConnectionContext connectionContext)
        {
#pragma warning disable 618
            Connection = new FtpConnectionCompat(connectionContext.Features);
#pragma warning restore 618
            Features = connectionContext.Features;
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
        public abstract void Reset(IAuthenticationMechanism? authenticationMechanism);

        /// <inheritdoc />
        public abstract Task<IFtpResponse> HandleUserAsync(string userIdentifier, CancellationToken cancellationToken);

        /// <inheritdoc />
        public abstract Task<IFtpResponse> HandlePassAsync(string password, CancellationToken cancellationToken);

        /// <inheritdoc />
        public abstract Task<IFtpResponse> HandleAcctAsync(string account, CancellationToken cancellationToken);

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
