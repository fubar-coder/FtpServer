// <copyright file="ConnectionExtensions.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;
using System.ComponentModel.DataAnnotations;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using FubarDev.FtpServer.Features;
using FubarDev.FtpServer.FileSystem.Error;

using Microsoft.Extensions.Logging;

namespace FubarDev.FtpServer
{
    /// <summary>
    /// Extension methods for <see cref="IFtpConnection"/>.
    /// </summary>
    public static class ConnectionExtensions
    {
        /// <summary>
        /// Executes some code with error handling.
        /// </summary>
        /// <param name="connection">The connection to execute the code for.</param>
        /// <param name="command">The command to execute the code for.</param>
        /// <param name="commandAction">The action to be executed.</param>
        /// <param name="logger">The logger to be used for logging.</param>
        /// <param name="cancellationToken">The cancellation token to signal command abortion.</param>
        /// <returns>The task with the (optional) response.</returns>
        [Obsolete("Use the extension method accepting the connection context.")]
        public static Task<IFtpResponse?> ExecuteCommand(
            this IFtpConnection connection,
            FtpCommand command,
            Func<FtpCommand, CancellationToken, Task<IFtpResponse?>> commandAction,
            ILogger? logger,
            CancellationToken cancellationToken)
        {
            var serverCommandWriter = connection.Features.Get<IServerCommandFeature>().ServerCommandWriter;
            var context = new FtpContext(command, serverCommandWriter, connection);
            return context.ExecuteCommand(
                commandAction,
                logger,
                cancellationToken);
        }

        /// <summary>
        /// Executes some code with error handling.
        /// </summary>
        /// <param name="context">The context to execute the code in.</param>
        /// <param name="commandAction">The action to be executed.</param>
        /// <param name="logger">The logger to be used for logging.</param>
        /// <param name="cancellationToken">The cancellation token to signal command abortion.</param>
        /// <returns>The task with the (optional) response.</returns>
        public static async Task<IFtpResponse?> ExecuteCommand(
            this FtpContext context,
            Func<FtpCommand, CancellationToken, Task<IFtpResponse?>> commandAction,
            ILogger? logger,
            CancellationToken cancellationToken)
        {
            var localizationFeature = context.Features.Get<ILocalizationFeature>();
            var command = context.Command;
            IFtpResponse? response;
            try
            {
                response = await commandAction(command, cancellationToken)
                   .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var exception = ex;
                while (exception is AggregateException aggregateException)
                {
                    exception = aggregateException.InnerException;
                }

                switch (exception)
                {
                    case ValidationException validationException:
                        response = new FtpResponse(
                            425,
                            validationException.Message);
                        logger?.LogWarning(validationException.Message);
                        break;

                    case SocketException se when se.ErrorCode == (int)SocketError.ConnectionAborted:
                    case OperationCanceledException _:
                        response = new FtpResponse(426, localizationFeature.Catalog.GetString("Connection closed; transfer aborted."));
                        logger?.LogTrace("Command {command} cancelled with response {response}", command, response);
                        break;

                    case FileSystemException fse:
                    {
                        var message = fse.Message != null ? $"{fse.FtpErrorName}: {fse.Message}" : fse.FtpErrorName;
                        logger?.LogInformation("Rejected command ({command}) with error {code} {message}", command, fse.FtpErrorCode, message);
                        response = new FtpResponse(fse.FtpErrorCode, message);
                        break;
                    }

                    case NotSupportedException nse:
                    {
                        var message = nse.Message ?? localizationFeature.Catalog.GetString("Command {command} not supported", command);
                        logger?.LogInformation(message);
                        response = new FtpResponse(502, message);
                        break;
                    }

                    default:
                        logger?.LogError(0, ex, "Failed to process message ({command})", command);
                        response = new FtpResponse(501, localizationFeature.Catalog.GetString("Syntax error in parameters or arguments."));
                        break;
                }
            }

            return response;
        }
    }
}
