// <copyright file="DefaultFtpCommandDispatcher.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using FubarDev.FtpServer.Features;
using FubarDev.FtpServer.Features.Impl;
using FubarDev.FtpServer.ServerCommands;

using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace FubarDev.FtpServer.Commands
{
    /// <summary>
    /// Default implementation of <see cref="IFtpCommandDispatcher"/>.
    /// </summary>
    public sealed class DefaultFtpCommandDispatcher : IFtpCommandDispatcher
    {
        private readonly IFtpConnectionContextAccessor _connectionContextAccessor;
        private readonly IFtpLoginStateMachine _loginStateMachine;
        private readonly IFtpCommandActivator _commandActivator;
        private readonly ILogger<DefaultFtpCommandDispatcher>? _logger;
        private readonly FtpCommandExecutionDelegate _executionDelegate;

        /// <summary>
        /// Initializes a new instance of the <see cref="DefaultFtpCommandDispatcher"/> class.
        /// </summary>
        /// <param name="connectionContextAccessor">The FTP connection context accessor.</param>
        /// <param name="loginStateMachine">The login state machine.</param>
        /// <param name="commandActivator">The command activator.</param>
        /// <param name="middlewareObjects">The list of middleware objects.</param>
        /// <param name="logger">The logger.</param>
        public DefaultFtpCommandDispatcher(
            IFtpConnectionContextAccessor connectionContextAccessor,
            IFtpLoginStateMachine loginStateMachine,
            IFtpCommandActivator commandActivator,
            IEnumerable<IFtpCommandMiddleware> middlewareObjects,
            ILogger<DefaultFtpCommandDispatcher>? logger = null)
        {
            _connectionContextAccessor = connectionContextAccessor;
            _loginStateMachine = loginStateMachine;
            _commandActivator = commandActivator;
            _logger = logger;
            var nextStep = new FtpCommandExecutionDelegate(ExecuteCommandAsync);
            foreach (var middleware in middlewareObjects.Reverse())
            {
                var tempStep = nextStep;
                nextStep = (context) => middleware.InvokeAsync(context, tempStep);
            }

            _executionDelegate = nextStep;
        }

        /// <summary>
        /// Gets the FTP connection features.
        /// </summary>
        private IFeatureCollection Features => _connectionContextAccessor.Context.Features;

        /// <inheritdoc />
        public async Task DispatchAsync(FtpContext context, CancellationToken cancellationToken)
        {
            var loginStateMachine =
                _loginStateMachine
                ?? throw new InvalidOperationException("Login state machine not initialized.");

            var commandHandlerContext = new FtpCommandHandlerContext(context);
            var result = _commandActivator.Create(commandHandlerContext);
            if (result == null)
            {
                await SendResponseAsync(
                        new FtpResponse(500, T("Syntax error, command unrecognized.")),
                        cancellationToken)
                   .ConfigureAwait(false);
                return;
            }

            var handler = result.Handler;
            var isLoginRequired = result.Information.IsLoginRequired;
            if (isLoginRequired && loginStateMachine.Status != SecurityStatus.Authorized)
            {
                await SendResponseAsync(
                        new FtpResponse(530, T("Not logged in.")),
                        cancellationToken)
                   .ConfigureAwait(false);
                return;
            }

            if (result.Information.IsAbortable)
            {
                await ExecuteBackgroundCommandAsync(context, handler, cancellationToken)
                   .ConfigureAwait(false);
            }
            else
            {
                var executionContext = new FtpExecutionContext(context, handler, cancellationToken);
                await _executionDelegate(executionContext)
                   .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Translates a message using the current catalog of the active connection.
        /// </summary>
        /// <param name="message">The message to translate.</param>
        /// <returns>The translated message.</returns>
        private string T(string message)
        {
            return Features.Get<ILocalizationFeature>().Catalog.GetString(message);
        }

        private Task ExecuteBackgroundCommandAsync(
            FtpContext context,
            IFtpCommandBase handler,
            CancellationToken cancellationToken)
        {
            var backgroundTaskFeature = Features.Get<IBackgroundTaskLifetimeFeature?>();
            if (backgroundTaskFeature == null)
            {
                backgroundTaskFeature = new BackgroundTaskLifetimeFeature(
                    handler,
                    context.Command,
                    ct =>
                    {
                        var executionContext = new FtpExecutionContext(context, handler, ct);
                        return _executionDelegate(executionContext);
                    },
                    cancellationToken);
                Features.Set(backgroundTaskFeature);
                return Task.CompletedTask;
            }

            return SendResponseAsync(
                new FtpResponse(503, T("Parallel commands aren't allowed.")),
                cancellationToken);
        }

        private async Task ExecuteCommandAsync(
            FtpExecutionContext context)
        {
            var response = await context.ExecuteCommand(
                (command, ct) => context.CommandHandler.Process(command, ct),
                _logger,
                context.CommandAborted);

            if (response != null)
            {
                try
                {
                    var lifetimeFeature = Features.Get<IConnectionLifetimeFeature>();
                    await SendResponseAsync(response, lifetimeFeature.ConnectionClosed)
                       .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex.Is<OperationCanceledException>())
                {
                    _logger?.LogWarning("Sending the response cancelled: {response}", response);
                }
            }
        }

        private async Task SendResponseAsync(IFtpResponse? response, CancellationToken cancellationToken)
        {
            if (response == null)
            {
                return;
            }

            var serverCommandFeature = Features.Get<IServerCommandFeature>();
            await serverCommandFeature.ServerCommandWriter
               .WriteAsync(new SendResponseServerCommand(response), cancellationToken)
               .ConfigureAwait(false);
            if (response.Code == 421)
            {
                // Critical Error: We have to close the connection!
                await serverCommandFeature.ServerCommandWriter
                   .WriteAsync(new CloseConnectionServerCommand(), cancellationToken)
                   .ConfigureAwait(false);
            }
        }
    }
}
