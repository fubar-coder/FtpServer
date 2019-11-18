// <copyright file="ShowCommandHandler.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using JKang.IpcServiceFramework;

using TestFtpServer.Api;

namespace TestFtpServer.Shell.Commands
{
    /// <summary>
    /// The <c>SHOW</c> command.
    /// </summary>
    public class ShowCommandHandler : IRootCommandInfo
    {
        private readonly IpcServiceClient<IFtpServerHost> _client;

        /// <summary>
        /// Initializes a new instance of the <see cref="ShowCommandHandler"/> class.
        /// </summary>
        /// <param name="client">The client to be used to communicate with the FTP server.</param>
        public ShowCommandHandler(
            IpcServiceClient<IFtpServerHost> client)
        {
            _client = client;
        }

        /// <inheritdoc />
        public string Name { get; } = "show";

        /// <inheritdoc />
        public IReadOnlyCollection<string> AlternativeNames { get; } = new[] { "list" };

        /// <param name="cancellationToken"></param>
        /// <inheritdoc />
        public async IAsyncEnumerable<ICommandInfo> GetSubCommandsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var extendedModuleInfoName = await _client
               .InvokeAsync(host => host.GetExtendedModules(), cancellationToken)
               .ConfigureAwait(false);
            var moduleInfos = extendedModuleInfoName
               .Select(x => new ModuleCommandInfo(_client, x))
               .Concat(
                    new ICommandInfo[]
                    {
                        new ShowConnectionsCommandInfo(_client),
                    });
            foreach (var moduleInfo in moduleInfos)
            {
                yield return moduleInfo;
            }
        }

        private class ModuleCommandInfo : IExecutableCommandInfo
        {
            private readonly IpcServiceClient<IFtpServerHost> _client;

            /// <summary>
            /// Initializes a new instance of the <see cref="ModuleCommandInfo"/> class.
            /// </summary>
            /// <param name="client">The client to be used to communicate with the FTP server.</param>
            /// <param name="moduleName">The name of the module.</param>
            public ModuleCommandInfo(
                IpcServiceClient<IFtpServerHost> client,
                string moduleName)
            {
                _client = client;
                Name = moduleName;
            }

            /// <inheritdoc />
            public string Name { get; }

            /// <inheritdoc />
            public IReadOnlyCollection<string> AlternativeNames { get; } = Array.Empty<string>();

            /// <param name="cancellationToken"></param>
            /// <inheritdoc />
            public IAsyncEnumerable<ICommandInfo> GetSubCommandsAsync(CancellationToken cancellationToken)
                => AsyncEnumerable.Empty<ICommandInfo>();

            /// <inheritdoc />
            public async Task ExecuteAsync(CancellationToken cancellationToken)
            {
                var info = await _client.InvokeAsync(host => host.GetExtendedModuleInfo(Name), cancellationToken)
                   .ConfigureAwait(false);

                if (!info.TryGetValue(Name, out var lines))
                {
                    return;
                }

                foreach (var line in lines)
                {
                    Console.WriteLine(line);
                }
            }
        }
    }
}
