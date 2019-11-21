// <copyright file="MultiBindingTcpListener.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FubarDev.FtpServer
{
    /// <summary>
    /// Allows binding to a host name, which in turn may resolve to multiple IP addresses.
    /// </summary>
    internal class MultiBindingTcpListener
    {
        private readonly IConnectionListenerFactory _connectionListenerFactory;
        private readonly string? _address;
        private readonly int _port;
        private readonly ILogger? _logger;
        private Task<AcceptInfo>[] _acceptors = Array.Empty<Task<AcceptInfo>>();
        private IConnectionListener[] _listeners = Array.Empty<IConnectionListener>();

        /// <summary>
        /// Initializes a new instance of the <see cref="MultiBindingTcpListener"/> class.
        /// </summary>
        /// <param name="address">The address/host name to bind to.</param>
        /// <param name="port">The listener port.</param>
        /// <param name="serviceProvider">The service provider.</param>
        /// <param name="connectionListenerFactory">The connection listener factory.</param>
        /// <param name="loggerFactory">The logger factory.</param>
        /// <param name="logger">The logger.</param>
        public MultiBindingTcpListener(
            string? address,
            int port,
            IServiceProvider serviceProvider,
            IConnectionListenerFactory? connectionListenerFactory = null,
            ILoggerFactory? loggerFactory = null,
            ILogger? logger = null)
        {
            if (port < 0 || port > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(port), "The port argument is out of range");
            }

            if (connectionListenerFactory != null)
            {
                _connectionListenerFactory = connectionListenerFactory;
            }
            else
            {
                var adapters = serviceProvider.GetRequiredService<IEnumerable<IFtpControlStreamAdapter>>()
                   .ToList();
                _connectionListenerFactory = new TcpListenerConnectionListenerFactory(adapters, loggerFactory);
            }

            _address = address;
            Port = _port = port;
            _logger = logger;
        }

        /// <summary>
        /// Gets the port this listener is bound to.
        /// </summary>
        public int Port { get; private set; }

        /// <summary>
        /// Start all listeners.
        /// </summary>
        /// <returns>the task.</returns>
        public async Task StartAsync()
        {
            _logger?.LogDebug("Server configured for listening on {address}:{port}", _address, _port);

            List<IPAddress> addresses;
            if (string.IsNullOrEmpty(_address) || _address == IPAddress.Any.ToString())
            {
                // "0.0.0.0"
                addresses = new List<IPAddress> { IPAddress.Any };
            }
            else if (_address == IPAddress.IPv6Any.ToString())
            {
                // "::"
                addresses = new List<IPAddress> { IPAddress.IPv6Any };
            }
            else if (_address == "*")
            {
                addresses = new List<IPAddress> { IPAddress.Any, IPAddress.IPv6Any };
            }
            else
            {
                var dnsAddresses = await Dns.GetHostAddressesAsync(_address).ConfigureAwait(false);
                addresses = dnsAddresses
                    .Where(x => x.AddressFamily == AddressFamily.InterNetwork ||
                                x.AddressFamily == AddressFamily.InterNetworkV6)
                    .ToList();
            }

            try
            {
                Port = await StartListeningAsync(addresses, _port).ConfigureAwait(false);
            }
            catch
            {
                await StopAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Stops all listeners.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public async Task StopAsync()
        {
            foreach (var listener in _listeners)
            {
                await listener.UnbindAsync().ConfigureAwait(false);
            }

            _listeners = Array.Empty<IConnectionListener>();
            _acceptors = Array.Empty<Task<AcceptInfo>>();
            Port = 0;
            _logger?.LogInformation("Listener stopped");
        }

        /// <summary>
        /// Wait for any client on all listeners.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The new TCP client.</returns>
        public async Task<ConnectionContext> WaitAnyTcpClientAsync(CancellationToken token)
        {
            // The task that just waits indefinitely for a triggered cancellation token
            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Build the list of awaitable tasks
            var tasks = new Task[_acceptors.Length + 1];
            Array.Copy(_acceptors, tasks, _acceptors.Length);

            // Add the cancellation task as last task
            tasks[_acceptors.Length] = tcs.Task;

            ConnectionContext? result;
            using (token.Register(
                state => { ((TaskCompletionSource<object?>)state!).TrySetResult(null); },
                tcs))
            {
                do
                {
                    // Wait for any task to be finished
                    var retVal = await Task.WhenAny(tasks).ConfigureAwait(false);

                    // Test if the cancellation token was triggered
                    token.ThrowIfCancellationRequested();

                    // It was a listener task when the cancellation token was not triggered
                    var acceptInfo = ((Task<AcceptInfo>)retVal).Result;
                    retVal.Dispose();

                    // Avoid indexed access into the list of acceptors
                    var index = acceptInfo.Index;

                    // Gets the result of the finished task.
                    result = acceptInfo.Connection;

                    // Start accepting the next TCP client for the
                    // listener whose task was finished.
                    var listener = _listeners[index];
                    var newAcceptor = AcceptForListenerAsync(listener, index);

                    // Start accepting the next TCP client for the
                    // listener whose task was finished.
                    tasks[index] = _acceptors[index] = newAcceptor;
                }
                while (result == null);
            }

            return result;
        }

        /// <summary>
        /// Start the asynchronous accept operation for all listeners.
        /// </summary>
        public void StartAccepting()
        {
            _acceptors = _listeners.Select(AcceptForListenerAsync).ToArray();
        }

        private async Task<AcceptInfo> AcceptForListenerAsync(IConnectionListener listener, int index)
        {
            try
            {
                var client = await listener.AcceptAsync().ConfigureAwait(false);
                return new AcceptInfo(client, index);
            }
            catch (ObjectDisposedException)
            {
                // Ignore the exception. This happens when the listener gets stopped.
                return new AcceptInfo(null, index);
            }
        }

        private async Task<int> StartListeningAsync(IEnumerable<IPAddress> addresses, int port)
        {
            var selectedPort = port;

            var listeners = new List<IConnectionListener>();
            foreach (var address in addresses)
            {
                var endPoint = new IPEndPoint(address, selectedPort);
                var listener = await _connectionListenerFactory.BindAsync(endPoint, CancellationToken.None)
                   .ConfigureAwait(false);
                if (selectedPort == 0)
                {
                    selectedPort = ((IPEndPoint)listener.EndPoint).Port;
                }

                _logger?.LogInformation("Started listening on {address}:{port}", address, selectedPort);

                listeners.Add(listener);
            }

            _listeners = listeners.ToArray();

            return selectedPort;
        }

        private struct AcceptInfo
        {
            public AcceptInfo(ConnectionContext? connection, int index)
            {
                Connection = connection;
                Index = index;
            }

            public ConnectionContext? Connection { get; }
            public int Index { get; }
        }
    }
}
