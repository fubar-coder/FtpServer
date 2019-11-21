// <copyright file="TcpClientConnection.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using FubarDev.FtpServer.Features;
using FubarDev.FtpServer.Networking;

using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace FubarDev.FtpServer
{
    internal sealed class TcpClientConnection
        : ConnectionContext,
            IConnectionLifetimeFeature,
            IConnectionEndPointFeature,
            IConnectionTransportFeature,
            IConnectionIdFeature,
            IConnectionItemsFeature,
            ITcpClientFeature
    {
        private readonly CancellationTokenSource _connectionClosedTokenSource = new CancellationTokenSource();
        private readonly Pipe _socketCommandPipe = new Pipe();
        private readonly Pipe _socketResponsePipe = new Pipe();
        private readonly ConnectionClosingNetworkStreamReader _streamReaderService;
        private readonly StreamPipeWriterService _streamWriterService;

        public TcpClientConnection(
            TcpClient client,
            ILoggerFactory? loggerFactory = null)
        {
            var features = new FeatureCollection();

            features.Set<IConnectionLifetimeFeature>(this);
            features.Set<IConnectionEndPointFeature>(this);
            features.Set<IConnectionTransportFeature>(this);
            features.Set<IConnectionIdFeature>(this);
            features.Set<IConnectionItemsFeature>(this);
            features.Set<ITcpClientFeature>(this);

            LocalEndPoint = client.Client.LocalEndPoint;
            RemoteEndPoint = client.Client.RemoteEndPoint;
            ConnectionId = Guid.NewGuid().ToString("N");
            ConnectionClosed = _connectionClosedTokenSource.Token;
            Transport = new DuplexPipe(_socketCommandPipe.Reader, _socketResponsePipe.Writer);
            TcpClient = client;
            Features = features;

            var originalStream = client.GetStream();
            _streamReaderService = new ConnectionClosingNetworkStreamReader(
                originalStream,
                _socketCommandPipe.Writer,
                this,
                loggerFactory?.CreateLogger($"{typeof(TcpClientConnection).FullName}:Receive"));
            _streamWriterService = new StreamPipeWriterService(
                originalStream,
                _socketResponsePipe.Reader,
                ConnectionClosed,
                loggerFactory?.CreateLogger($"{typeof(TcpClientConnection).FullName}:Transmit"));
        }

        /// <inheritdoc />
        public TcpClient TcpClient { get; }

        /// <inheritdoc />
        public override string ConnectionId { get; set; }

        /// <inheritdoc />
        public override IFeatureCollection Features { get; }

        /// <inheritdoc />
        public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object>();

        /// <inheritdoc />
        public override IDuplexPipe Transport { get; set; }

        public async Task StartAsync()
        {
            await _streamWriterService.StartAsync(CancellationToken.None)
               .ConfigureAwait(false);
            await _streamReaderService.StartAsync(CancellationToken.None)
               .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override void Abort(ConnectionAbortedException abortReason)
        {
            ThreadPool.UnsafeQueueUserWorkItem(
                cts => ((CancellationTokenSource)cts!).Cancel(),
                _connectionClosedTokenSource);
        }

        /// <inheritdoc />
        public override async ValueTask DisposeAsync()
        {
            await _streamReaderService.StopAsync(CancellationToken.None)
               .ConfigureAwait(false);
            await _streamWriterService.StopAsync(CancellationToken.None)
               .ConfigureAwait(false);
            _connectionClosedTokenSource.Dispose();
        }

        private class ConnectionClosingNetworkStreamReader : StreamPipeReaderService
        {
            private readonly IConnectionLifetimeFeature _lifetimeFeature;

            public ConnectionClosingNetworkStreamReader(
                Stream stream,
                PipeWriter pipeWriter,
                IConnectionLifetimeFeature lifetimeFeature,
                ILogger? logger = null)
                : base(stream, pipeWriter, lifetimeFeature.ConnectionClosed, logger)
            {
                _lifetimeFeature = lifetimeFeature;
            }

            /// <inheritdoc />
            protected override async Task<int> ReadFromStreamAsync(byte[] buffer, int offset, int length, CancellationToken cancellationToken)
            {
                var readTask = Stream
                   .ReadAsync(buffer, offset, length, cancellationToken);

                // We ensure that this service can be closed ASAP with the help
                // of a Task.Delay.
                var resultTask = await Task.WhenAny(readTask, Task.Delay(-1, cancellationToken))
                   .ConfigureAwait(false);
                if (resultTask != readTask || cancellationToken.IsCancellationRequested)
                {
                    Logger?.LogTrace("Cancelled through Task.Delay");
                    return 0;
                }

                var result = readTask.Result;
                readTask.Dispose();
                return result;
            }

            /// <inheritdoc />
            protected override async Task OnCloseAsync(Exception? exception, CancellationToken cancellationToken)
            {
                await base.OnCloseAsync(exception, cancellationToken)
                   .ConfigureAwait(false);

                // Signal a closed connection.
                _lifetimeFeature.Abort();
            }
        }

        private class DuplexPipe : IDuplexPipe
        {
            public DuplexPipe(PipeReader input, PipeWriter output)
            {
                Input = input;
                Output = output;
            }

            /// <inheritdoc />
            public PipeReader Input { get; }

            /// <inheritdoc />
            public PipeWriter Output { get; }
        }
    }
}
