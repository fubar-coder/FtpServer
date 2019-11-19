// <copyright file="FtpConnectionContextAccessor.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;
using System.Threading;

using Microsoft.AspNetCore.Connections;

namespace FubarDev.FtpServer
{
    /// <summary>
    /// Default implementation of <see cref="IFtpConnectionContextAccessor"/>.
    /// </summary>
    public class FtpConnectionContextAccessor : IFtpConnectionContextAccessor
    {
        private static readonly AsyncLocal<Holder> _current = new AsyncLocal<Holder>();

        /// <inheritdoc />
        public ConnectionContext Context
        {
            get => _current.Value?.Value ?? throw new InvalidOperationException("Connection context not set.");
            set
            {
                var holder = _current.Value;
                if (holder != null)
                {
                    // Clear current IFtpConnection trapped in the AsyncLocals, as its done.
                    holder.Value = null;
                }

                if (value != null)
                {
                    // Use an object indirection to hold the IFtpConnection in the AsyncLocal,
                    // so it can be cleared in all ExecutionContexts when its cleared.
                    _current.Value = new Holder()
                    {
                        Value = value,
                    };
                }
            }
        }

        private class Holder
        {
            public ConnectionContext? Value { get; set; }
        }
    }
}
