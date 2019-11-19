// <copyright file="ReflectionServerCommandExecutor.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

namespace FubarDev.FtpServer.ServerCommands
{
    /// <summary>
    /// This <see cref="IServerCommandExecutor"/> implementation calls the server command handler using reflection.
    /// </summary>
    public class ReflectionServerCommandExecutor : IServerCommandExecutor
    {
        private readonly IFtpConnectionContextAccessor _connectionContextAccessor;
        private readonly Dictionary<Type, CommandHandlerInfo> _serverCommandHandlerInfo =
            new Dictionary<Type, CommandHandlerInfo>();

        /// <summary>
        /// Initializes a new instance of the <see cref="ReflectionServerCommandExecutor"/> class.
        /// </summary>
        /// <param name="connectionContextAccessor">The FTP connection context accessor.</param>
        public ReflectionServerCommandExecutor(
            IFtpConnectionContextAccessor connectionContextAccessor)
        {
            _connectionContextAccessor = connectionContextAccessor;
        }

        /// <inheritdoc />
        public Task ExecuteAsync(IServerCommand serverCommand, CancellationToken cancellationToken)
        {
            var serverCommandType = serverCommand.GetType();
            if (!_serverCommandHandlerInfo.TryGetValue(serverCommandType, out var commandHandlerInfo))
            {
                var handlerType = typeof(IServerCommandHandler<>).MakeGenericType(serverCommandType);
                var executeAsyncMethod = handlerType.GetRuntimeMethod("ExecuteAsync", new[] { serverCommandType, typeof(CancellationToken) });
                var serviceProvider = _connectionContextAccessor.Context.Features.GetServiceProvider();
                var handler = serviceProvider.GetRequiredService(handlerType);

                commandHandlerInfo = new CommandHandlerInfo(handler, executeAsyncMethod!);
                _serverCommandHandlerInfo.Add(serverCommandType, commandHandlerInfo);
            }

            return (Task)commandHandlerInfo.ExecuteMethodInfo.Invoke(
                commandHandlerInfo.CommandHandler,
                new object[] { serverCommand, cancellationToken })!;
        }

        private class CommandHandlerInfo
        {
            public CommandHandlerInfo(object commandHandler, MethodInfo executeMethodInfo)
            {
                CommandHandler = commandHandler;
                ExecuteMethodInfo = executeMethodInfo;
            }
            public object CommandHandler { get; }
            public MethodInfo ExecuteMethodInfo { get; }
        }
    }
}
