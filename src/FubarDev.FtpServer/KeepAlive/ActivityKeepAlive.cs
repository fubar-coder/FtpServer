// <copyright file="ActivityKeepAlive.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;

namespace FubarDev.FtpServer.KeepAlive
{
    /// <summary>
    /// An activity-based keep-alive detection.
    /// </summary>
    public class ActivityKeepAlive : IFtpConnectionKeepAlive
    {
        /// <summary>
        /// The lock to be acquired when the timeout information gets set or read.
        /// </summary>
        private readonly object _inactivityTimeoutLock = new object();

        /// <summary>
        /// The timeout for the detection of inactivity.
        /// </summary>
        private readonly TimeSpan _inactivityTimeout;

        /// <summary>
        /// The timestamp of the last activity on the connection.
        /// </summary>
        private DateTime _utcLastActiveTime;

        /// <summary>
        /// The timestamp where the connection expires.
        /// </summary>
        private DateTime? _expirationTimeout;

        /// <summary>
        /// Indicator if a data transfer is ongoing.
        /// </summary>
        private int _dataTransferCount;

        public ActivityKeepAlive(TimeSpan? inactivityTimeout)
        {
            _inactivityTimeout = inactivityTimeout ?? TimeSpan.MaxValue;
            UpdateLastActiveTime();
        }

        /// <inheritdoc />
        public bool IsAlive
        {
            get
            {
                lock (_inactivityTimeoutLock)
                {
                    if (_expirationTimeout == null)
                    {
                        return true;
                    }

                    if (_dataTransferCount != 0)
                    {
                        UpdateLastActiveTime();
                        return true;
                    }

                    return DateTime.UtcNow <= _expirationTimeout.Value;
                }
            }
        }

        /// <inheritdoc />
        public DateTime LastActivityUtc
        {
            get
            {
                lock (_inactivityTimeoutLock)
                {
                    return _utcLastActiveTime;
                }
            }
        }

        /// <inheritdoc />
        public IDisposable RegisterDataTransfer()
        {
            return new DataTransferHelper(this);
        }

        /// <inheritdoc />
        public void KeepAlive()
        {
            lock (_inactivityTimeoutLock)
            {
                UpdateLastActiveTime();
            }
        }

        private void UpdateLastActiveTime()
        {
            _utcLastActiveTime = DateTime.UtcNow;
            _expirationTimeout = (_inactivityTimeout == TimeSpan.MaxValue)
                ? (DateTime?)null
                : _utcLastActiveTime.Add(_inactivityTimeout);
        }

        private class DataTransferHelper : IDisposable
        {
            private readonly ActivityKeepAlive _keepAlive;

            public DataTransferHelper(ActivityKeepAlive keepAlive)
            {
                _keepAlive = keepAlive;
                Increment(1);
            }

            /// <inheritdoc />
            public void Dispose()
            {
                Increment(-1);
            }

            private void Increment(int step)
            {
                lock (_keepAlive._inactivityTimeoutLock)
                {
                    // Reset the expiration timeout when the data transfer status gets updated.
                    _keepAlive.UpdateLastActiveTime();
                    _keepAlive._dataTransferCount += step;
                }
            }
        }
    }
}
