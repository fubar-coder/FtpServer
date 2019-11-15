// <copyright file="FtpConnectionContextFactory.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using FubarDev.FtpServer.ConnectionHandlers;
using FubarDev.FtpServer.Features;
using FubarDev.FtpServer.FileSystem;
using FubarDev.FtpServer.Localization;
using FubarDev.FtpServer.ServerCommands;

using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;

using NGettext;

namespace FubarDev.FtpServer
{
    public class FtpConnectionContextFactory
    {
        public ConnectionContext Create(TcpClient client)
        {
            var context = new FtpConnectionContext();
            return context;
        }

        private class FtpConnectionContext
            : DefaultConnectionContext,
                IResettableFeature,
#pragma warning disable 618
                IConnectionFeature,
#pragma warning restore 618
                IServiceProvidersFeature,
                IFileSystemFeature,
                ILocalizationFeature,
                IEncodingFeature,
                ITransferConfigurationFeature,
                IServerCommandFeature,
                ISecureConnectionFeature,
#pragma warning disable 618
                IAuthorizationInformationFeature,
#pragma warning restore 618
                INetworkStreamFeature
        {
            private readonly CultureInfo _initialLanguage;
            private Stack<IUnixDirectoryEntry> _initialPath = new Stack<IUnixDirectoryEntry>();
            private Encoding? _encoding;
            private Encoding? _nlstEncoding;

            public FtpConnectionContext(
                IFtpCatalogLoader catalogLoader,
                Encoding defaultEncoding,
                ChannelWriter<IServerCommand> serverCommandWriter,
                IFtpSecureConnectionAdapterManager secureConnectionAdapterManager)
                : base(CreateConnectionId())
            {
                _initialLanguage = Language = catalogLoader.DefaultLanguage;
                Catalog = catalogLoader.DefaultCatalog;
                DefaultEncoding = defaultEncoding;
                ServerCommandWriter = serverCommandWriter;
                SecureConnectionAdapterManager = secureConnectionAdapterManager;
            }

            /// <inheritdoc />
            IPEndPoint IConnectionFeature.LocalEndPoint => (IPEndPoint)LocalEndPoint;

            /// <inheritdoc />
            IPEndPoint IConnectionFeature.RemoteEndPoint => (IPEndPoint)RemoteEndPoint;

            /// <inheritdoc />
            public IServiceProvider RequestServices { get; set; }


            /// <inheritdoc />
            public IUnixFileSystem FileSystem { get; set; } = new EmptyUnixFileSystem();

            /// <inheritdoc />
            public Stack<IUnixDirectoryEntry> Path { get; set; } = new Stack<IUnixDirectoryEntry>();

            /// <inheritdoc />
            public IUnixDirectoryEntry CurrentDirectory
            {
                get
                {
                    if (Path.Count == 0)
                    {
                        return FileSystem.Root;
                    }

                    return Path.Peek();
                }
            }

            /// <inheritdoc />
            public CultureInfo Language { get; set; }

            /// <inheritdoc />
            public ICatalog Catalog { get; set; }

            /// <inheritdoc />
            public Encoding DefaultEncoding { get; }

            /// <inheritdoc />
            public Encoding Encoding
            {
                get => _encoding ?? DefaultEncoding;
                set => _encoding = value;
            }

            /// <inheritdoc />
            public Encoding NlstEncoding
            {
                get => _nlstEncoding ?? DefaultEncoding;
                set => _nlstEncoding = value;
            }

            /// <inheritdoc />
            public FtpTransferMode TransferMode { get; set; } = new FtpTransferMode(FtpFileType.Ascii);

            /// <inheritdoc />
            public ChannelWriter<IServerCommand> ServerCommandWriter { get; }

            /// <inheritdoc />
            public CreateEncryptedStreamDelegate CreateEncryptedStream { get; set; } = Task.FromResult;

            /// <inheritdoc />
            public CloseEncryptedStreamDelegate CloseEncryptedControlStream { get; set; } = ct => Task.CompletedTask;

            /// <inheritdoc />
            public IFtpSecureConnectionAdapterManager SecureConnectionAdapterManager { get; }

            /// <inheritdoc />
            PipeWriter INetworkStreamFeature.Output => Transport.Output;

            /// <inheritdoc />
            ClaimsPrincipal? IAuthorizationInformationFeature.FtpUser
            {
                get => User;
                set => User = value;
            }

            /// <inheritdoc />
            public void SetInitialPath(Stack<IUnixDirectoryEntry> path)
            {
                _initialPath = path.Clone();
                Path = _initialPath.Clone();
            }

            /// <inheritdoc />
            public async Task ResetAsync(CancellationToken cancellationToken)
            {
                // File system feature
                switch (FileSystem)
                {
                    case IAsyncDisposable ad:
                        await ad.DisposeAsync().ConfigureAwait(false);
                        break;
                    case IDisposable d:
                        d.Dispose();
                        break;
                }

                FileSystem = new EmptyUnixFileSystem();
                SetInitialPath(new Stack<IUnixDirectoryEntry>());

                // User feature
                User = null;

                // Localization feature
                Language = _initialLanguage;

                // Encoding feature
                ((IEncodingFeature)this).Reset();

                // Transfer mode feature
                TransferMode = new FtpTransferMode(FtpFileType.Ascii);

                // Secure connection feature
                // This will also reset the network stream feature!
                await CloseEncryptedControlStream(cancellationToken).ConfigureAwait(false);
                CreateEncryptedStream = Task.FromResult;
                CloseEncryptedControlStream = ct => Task.CompletedTask;
            }

            /// <inheritdoc />
            void IEncodingFeature.Reset()
            {
                _nlstEncoding = _encoding = null;
            }

            /// <summary>
            /// Creates a new connection identifier.
            /// </summary>
            /// <returns>The new connection identifier.</returns>
            private static string CreateConnectionId()
            {
                return $"FTP-{Guid.NewGuid():N}";
            }
        }
    }
}
