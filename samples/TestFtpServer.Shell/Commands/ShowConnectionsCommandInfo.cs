// <copyright file="ShowConnectionsCommandInfo.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BetterConsoleTables;

using JKang.IpcServiceFramework;

using TestFtpServer.Api;

namespace TestFtpServer.Shell.Commands
{
    /// <summary>
    /// Command handler for showing all active connections.
    /// </summary>
    public class ShowConnectionsCommandInfo : IExecutableCommandInfo
    {
        private readonly IpcServiceClient<IFtpServerHost> _client;

        /// <summary>
        /// Initializes a new instance of the <see cref="ShowConnectionsCommandInfo"/> class.
        /// </summary>
        /// <param name="client">The IPC client.</param>
        public ShowConnectionsCommandInfo(IpcServiceClient<IFtpServerHost> client)
        {
            _client = client;
        }

        /// <inheritdoc />
        public string Name { get; } = "connections";

        /// <inheritdoc />
        public IReadOnlyCollection<string> AlternativeNames { get; } = Array.Empty<string>();

        /// <param name="cancellationToken"></param>
        /// <inheritdoc />
        public IAsyncEnumerable<ICommandInfo> GetSubCommandsAsync(CancellationToken cancellationToken)
            => AsyncEnumerable.Empty<ICommandInfo>();

        /// <inheritdoc />
        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            var connections = await _client
               .InvokeAsync(host => host.GetConnections(), cancellationToken)
               .ConfigureAwait(false);

            if (connections.Count == 0)
            {
                Console.WriteLine("No connections found.");
                return;
            }

            var table = new Table(
                    TableConfiguration.Markdown(),
                    "ID",
                    "Alive",
                    "Remote IP",
                    "User",
                    "Transfer")
               .AddRows(
                    connections.Select(
                        x => new object[]
                        {
                            x.Id,
                            x.IsAlive,
                            x.RemoteIp,
                            x.User.ToString(),
                            x.HasActiveTransfer,
                        }));
            Console.WriteLine(table.ToString());
        }
    }
}
