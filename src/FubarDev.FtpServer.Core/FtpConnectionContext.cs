// <copyright file="FtpConnectionContext.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using FubarDev.FtpServer.Authentication;
using FubarDev.FtpServer.ConnectionChecks;
using FubarDev.FtpServer.ConnectionHandlers;
using FubarDev.FtpServer.Features;
using FubarDev.FtpServer.FileSystem;
using FubarDev.FtpServer.Localization;
using FubarDev.FtpServer.Networking;
using FubarDev.FtpServer.ServerCommands;
using FubarDev.FtpServer.Statistics;

using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NGettext;

namespace FubarDev.FtpServer
{
    /// <summary>
    /// The FTP connection context.
    /// </summary>
    public sealed class FtpConnectionContext
        : ConnectionContext,
            IConnectionIdFeature,
            IConnectionItemsFeature,
            IConnectionTransportFeature,
            IConnectionUserFeature,
            IConnectionLifetimeFeature,
            IConnectionEndPointFeature,
            IResettableFeature,
#pragma warning disable 618
            IConnectionFeature,
            IAuthorizationInformationFeature,
#pragma warning restore 618
            IServiceProvidersFeature,
            IFileSystemFeature,
            ILocalizationFeature,
            IEncodingFeature,
            ITransferConfigurationFeature,
            IServerCommandFeature,
            ISecureConnectionFeature,
            INetworkStreamFeature,
            IFtpStatisticsCollectorFeature,
            IFtpConnectionStatusCheck,
            IDisposable
    {
        private readonly List<IFtpStatisticsCollector> _collectors = new List<IFtpStatisticsCollector>();
        private readonly CultureInfo _initialLanguage;
        private readonly CancellationTokenSource _connectionClosedTokenSource = new CancellationTokenSource();
        private Stack<IUnixDirectoryEntry> _initialPath = new Stack<IUnixDirectoryEntry>();
        private ImmutableList<IFtpConnectionCheck> _checks = ImmutableList<IFtpConnectionCheck>.Empty;
        private Encoding? _encoding;
        private Encoding? _nlstEncoding;
        private ClaimsPrincipal? _user;

        /// <summary>
        /// Initializes a new instance of the <see cref="FtpConnectionContext"/> class.
        /// </summary>
        /// <param name="catalogLoader">The catalog loader for the FTP server.</param>
        /// <param name="defaultEncoding">The default file list encoding.</param>
        /// <param name="serverCommandWriter">Writer for server commands.</param>
        /// <param name="socketPipe">The pipe from the socket.</param>
        /// <param name="sslStreamWrapperFactory">The SSL stream wrapper factory.</param>
        /// <param name="applicationInputPipe">The pipe that's used to receive data from the connection.</param>
        /// <param name="applicationOutputPipe">The pipe that's used to send data to the connection.</param>
        /// <param name="localEndPoint">The local end point.</param>
        /// <param name="remoteEndPoint">The remote end point.</param>
        /// <param name="requestServices">The service provider.</param>
        public FtpConnectionContext(
            IFtpCatalogLoader catalogLoader,
            Encoding defaultEncoding,
            ChannelWriter<IServerCommand> serverCommandWriter,
            IDuplexPipe socketPipe,
            ISslStreamWrapperFactory sslStreamWrapperFactory,
            Pipe applicationInputPipe,
            Pipe applicationOutputPipe,
            EndPoint localEndPoint,
            EndPoint remoteEndPoint,
            IServiceProvider requestServices)
        {
            _initialLanguage = catalogLoader.DefaultLanguage;

            var features = new FeatureCollection();
            features.Set<IConnectionUserFeature>(this);
            features.Set<IConnectionItemsFeature>(this);
            features.Set<IConnectionIdFeature>(this);
            features.Set<IConnectionTransportFeature>(this);
            features.Set<IConnectionLifetimeFeature>(this);
            features.Set<IConnectionEndPointFeature>(this);
            features.Set<IResettableFeature>(this);
#pragma warning disable 618
            features.Set<IConnectionFeature>(this);
            features.Set<IAuthorizationInformationFeature>(this);
#pragma warning restore 618
            features.Set<IServiceProvidersFeature>(this);
            features.Set<IFileSystemFeature>(this);
            features.Set<ILocalizationFeature>(this);
            features.Set<IEncodingFeature>(this);
            features.Set<ITransferConfigurationFeature>(this);
            features.Set<IServerCommandFeature>(this);
            features.Set<ISecureConnectionFeature>(this);
            features.Set<INetworkStreamFeature>(this);
            features.Set<IFtpStatisticsCollectorFeature>(this);
            features.Set<IFtpConnectionStatusCheck>(this);

            var connectionPipe = new DuplexPipe(applicationOutputPipe.Reader, applicationInputPipe.Writer);
            var transportPipe = new DuplexPipe(applicationInputPipe.Reader, applicationOutputPipe.Writer);

            ConnectionClosed = _connectionClosedTokenSource.Token;
            ConnectionId = CreateConnectionId();
            Features = features;
            Language = _initialLanguage;
            Catalog = catalogLoader.DefaultCatalog;
            DefaultEncoding = defaultEncoding;
            ServerCommandWriter = serverCommandWriter;
            SecureConnectionAdapterManager = new SecureConnectionAdapterManager(
                socketPipe,
                connectionPipe,
                transportPipe,
                sslStreamWrapperFactory,
                this,
                _connectionClosedTokenSource.Token,
                requestServices.GetService<ILoggerFactory>());
            Transport = socketPipe;
            LocalEndPoint = localEndPoint;
            RemoteEndPoint = remoteEndPoint;
            RequestServices = requestServices;
        }

        /// <summary>
        /// Gets or sets a unique identifier to represent this connection in trace logs.
        /// </summary>
        public override string ConnectionId { get; set; }

        /// <inheritdoc />
        public override IFeatureCollection Features { get; }

        /// <summary>
        /// Gets or sets a key/value collection that can be used to share data within the scope of this connection.
        /// </summary>
        public override IDictionary<object, object> Items { get; set; } = new ConnectionItems();

        /// <summary>
        /// Gets or sets the <see cref="IDuplexPipe"/> that can be used to read or write data on this connection.
        /// </summary>
        public override IDuplexPipe Transport { get; set; }

        /// <summary>
        /// Gets or sets the cancellation token that gets triggered when the client connection is closed.
        /// </summary>
        public override CancellationToken ConnectionClosed { get; set; }

        /// <summary>
        /// Gets or sets the local endpoint for this connection.
        /// </summary>
        public override EndPoint LocalEndPoint { get; set; }

        /// <summary>
        /// Gets or sets the remote endpoint for this connection.
        /// </summary>
        public override EndPoint RemoteEndPoint { get; set; }

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
            get => _user;
            set => SetUser(value);
        }

        /// <inheritdoc />
#nullable disable
        public ClaimsPrincipal User
        {
            get => _user;
            set => SetUser(value);
        }
#nullable restore

        /// <inheritdoc />
        public override ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _connectionClosedTokenSource.Dispose();
        }

        /// <inheritdoc />
        public override void Abort(ConnectionAbortedException abortReason)
        {
            try
            {
                _connectionClosedTokenSource.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Ignore
            }
        }

        /// <inheritdoc />
        public void SetInitialPath(Stack<IUnixDirectoryEntry> path)
        {
            _initialPath = path.Clone();
            Path = _initialPath.Clone();
        }

        /// <summary>
        /// Sets the checks that must be performed.
        /// </summary>
        /// <param name="checks">The idle checks to be performed.</param>
        public void SetChecks(IEnumerable<IFtpConnectionCheck> checks)
        {
            _checks = ImmutableList<IFtpConnectionCheck>.Empty.AddRange(checks);
        }

        /// <inheritdoc />
        public bool CheckIfAlive()
        {
            var context = new FtpConnectionCheckContext(Features);
            var checkResults = _checks
               .Select(x => x.Check(context))
               .ToArray();
            return checkResults.Select(x => x.IsUsable)
               .Aggregate(true, (pv, item) => pv && item);
        }

        /// <inheritdoc />
        public void ForEach(Action<IFtpStatisticsCollector> action)
        {
            lock (_collectors)
            {
                foreach (var collector in _collectors)
                {
                    action(collector);
                }
            }
        }

        /// <inheritdoc />
        public IDisposable Register(IFtpStatisticsCollector collector)
        {
            lock (_collectors)
            {
                _collectors.Add(collector);
            }

            return new CollectorRegistration(this, collector);
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
            SetUser(null);

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

        private void SetUser(ClaimsPrincipal? user)
        {
            _user = user;
            ForEach(collector => collector.UserChanged(user));
        }

        private void Remove(IFtpStatisticsCollector collector)
        {
            lock (_collectors)
            {
                _collectors.Remove(collector);
            }
        }

        private class CollectorRegistration : IDisposable
        {
            private readonly FtpConnectionContext _feature;
            private readonly IFtpStatisticsCollector _collector;

            public CollectorRegistration(
                FtpConnectionContext feature,
                IFtpStatisticsCollector collector)
            {
                _feature = feature;
                _collector = collector;
            }

            /// <inheritdoc />
            public void Dispose()
            {
                _feature.Remove(_collector);
            }
        }
    }
}
