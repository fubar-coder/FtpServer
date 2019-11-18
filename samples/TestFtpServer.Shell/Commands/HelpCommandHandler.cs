// <copyright file="HelpCommandHandler.cs" company="Fubar Development Junker">
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
    /// The <c>HELP</c> command.
    /// </summary>
    public class HelpCommandHandler : IRootCommandInfo, IExecutableCommandInfo
    {
        private readonly IpcServiceClient<IFtpServerHost> _client;

        /// <summary>
        /// Initializes a new instance of the <see cref="HelpCommandHandler"/> class.
        /// </summary>
        /// <param name="client">The client to be used to communicate with the FTP server.</param>
        public HelpCommandHandler(
            IpcServiceClient<IFtpServerHost> client)
        {
            _client = client;
        }

        /// <inheritdoc />
        public string Name { get; } = "help";

        /// <inheritdoc />
        public IReadOnlyCollection<string> AlternativeNames { get; } = Array.Empty<string>();

        /// <param name="cancellationToken"></param>
        /// <inheritdoc />
        public IAsyncEnumerable<ICommandInfo> GetSubCommandsAsync(CancellationToken cancellationToken)
            => AsyncEnumerable.Empty<ICommandInfo>();

        /// <inheritdoc />
        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            var help = new Table(new EmptyTableConfiguration(), "help", "Show help")
               .AddRows(
                    new List<object[]>()
                    {
                        new object[] { "exit", "Close this shell" },
                        new object[] { "pause", "Pause accepting clients" },
                        new object[] { "continue", "Continue accepting clients" },
                        new object[] { "stop", "Continue accepting clients" },
                        new object[] { "status", "Show server status" },
                        new object[] { "show <module>", "Show module information" },
                        new object[] { "close connection <name>", "Close the connection with the given name" },
                    });
            Console.Write(help.ToString());

            Console.WriteLine();
            var modules = await GetExtendedModuleNamesAsync(cancellationToken)
               .ConfigureAwait(false);

            if (modules == null)
            {
                Console.WriteLine("Modules: Communication failure. No modules found.");
            }
            else if (modules.Count == 0)
            {
                Console.WriteLine("Modules: No modules found.");
            }
            else
            {
                var modulesTable = new Table(
                        new EmptyTableConfiguration()
                        {
                            hasHeaderRow = true,
                        },
                        "Modules")
                   .AddRows(modules.Select(x => new object[] { x }));
                Console.WriteLine(modulesTable.ToString());
            }
        }

        private async Task<ICollection<string>> GetExtendedModuleNamesAsync(CancellationToken cancellationToken)
        {
            try
            {
                var extendedModuleInfoName = await _client
                   .InvokeAsync(host => host.GetExtendedModules(), cancellationToken)
                   .ConfigureAwait(false);

                return extendedModuleInfoName;
            }
            catch
            {
                return null;
            }
        }
    }
}
