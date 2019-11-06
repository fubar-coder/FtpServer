// <copyright file="WatchdocActivityStatus.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;

namespace FubarDev.FtpServer.KeepAlive
{
    /// <summary>
    /// Some simple watchdog-style activity tracker.
    /// </summary>
    public class WatchdogActivityStatus : IFtpConnectionActivityStatus
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

        public WatchdogActivityStatus(TimeSpan? inactivityTimeout)
        {
            _inactivityTimeout = inactivityTimeout ?? TimeSpan.MaxValue;
            UpdateActivity();
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

        /// <summary>
        /// Updates the activity information.
        /// </summary>
        public void UpdateActivity()
        {
            lock (_inactivityTimeoutLock)
            {
                _utcLastActiveTime = DateTime.UtcNow;
                _expirationTimeout = (_inactivityTimeout == TimeSpan.MaxValue)
                    ? (DateTime?)null
                    : _utcLastActiveTime.Add(_inactivityTimeout);
            }
        }
    }
}
