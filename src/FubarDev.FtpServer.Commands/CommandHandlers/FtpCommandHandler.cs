//-----------------------------------------------------------------------
// <copyright file="FtpCommandHandler.cs" company="Fubar Development Junker">
//     Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>
// <author>Mark Junker</author>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

using FubarDev.FtpServer.Features;
using FubarDev.FtpServer.Localization;

using JetBrains.Annotations;

using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace FubarDev.FtpServer.CommandHandlers
{
    /// <summary>
    /// The base class for all FTP command handlers.
    /// </summary>
    public abstract class FtpCommandHandler : IFtpCommandHandler
    {
        private IFtpServerMessages? _serverMessages;
        private IServiceProvider? _requestServices;
        private FtpCommandHandlerContext? _commandHandlerContext;

        /// <summary>
        /// Gets or sets the FTP command context.
        /// </summary>
        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global", Justification = "Set using reflection.")]
        [SuppressMessage("ReSharper", "MemberCanBeProtected.Global", Justification = "Required for setting through reflection.")]
        public FtpCommandHandlerContext CommandContext
        {
            get => _commandHandlerContext ?? throw new InvalidOperationException("The context was used outside of an active connection.");
            set => _commandHandlerContext = value;
        }

        /// <summary>
        /// Gets the connection this command was created for.
        /// </summary>
        protected FtpContext FtpContext => CommandContext.FtpContext ?? throw new InvalidOperationException("The connection information was used outside of an active connection.");

        /// <summary>
        /// Gets the connection this command was created for.
        /// </summary>
        [Obsolete("Access the features directly.")]
        protected IFtpConnection Connection => FtpContext.Connection;

        /// <summary>
        /// Gets the connection features.
        /// </summary>
        protected IFeatureCollection Features => FtpContext.Features;

        /// <summary>
        /// Gets the server messages to be returned.
        /// </summary>
        protected IFtpServerMessages ServerMessages
            => _serverMessages ??= RequestServices.GetRequiredService<IFtpServerMessages>();

        /// <summary>
        /// Gets the services for the request.
        /// </summary>
        protected IServiceProvider RequestServices
            => _requestServices ??= Features.GetServiceProvider();

        /// <inheritdoc />
        public abstract Task<IFtpResponse?> Process(FtpCommand command, CancellationToken cancellationToken);

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
        protected string T(string message, params object?[] args)
        {
            return Features.Get<ILocalizationFeature>().Catalog.GetString(message, args);
        }
    }
}
