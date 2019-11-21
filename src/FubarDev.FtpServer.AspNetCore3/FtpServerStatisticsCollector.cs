// <copyright file="FtpServerStatisticsCollector.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System.Threading;

namespace FubarDev.FtpServer
{
    /// <summary>
    /// Collector of statistics.
    /// </summary>
    internal class FtpServerStatisticsCollector
    {
        private readonly FtpServerStatistics _statistics = new FtpServerStatistics();

        /// <summary>
        /// Gets the current statistics.
        /// </summary>
        /// <returns>The statistics.</returns>
        public IFtpServerStatistics GetStatistics()
        {
            return _statistics;
        }

        /// <summary>
        /// Notifies that a new connection was established.
        /// </summary>
        public void AddConnection()
        {
            _statistics.AddConnection();
        }

        /// <summary>
        /// Notifies that a connection was closed.
        /// </summary>
        public void CloseConnection()
        {
            _statistics.CloseConnection();
        }

        /// <summary>
        /// Statistics about the FTP server.
        /// </summary>
        internal class FtpServerStatistics : IFtpServerStatistics
        {
            private long _totalConnections;
            private long _activeConnections;

            /// <inheritdoc />
            public long TotalConnections => _totalConnections;

            /// <inheritdoc />
            public long ActiveConnections => _activeConnections;

            public void AddConnection()
            {
                Interlocked.Increment(ref _totalConnections);
                Interlocked.Increment(ref _activeConnections);
            }

            public void CloseConnection()
            {
                Interlocked.Decrement(ref _activeConnections);
            }
        }
    }
}
