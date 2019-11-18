// <copyright file="FtpContext.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;
using System.Threading.Channels;

using FubarDev.FtpServer.Compatibility;
using FubarDev.FtpServer.ServerCommands;

using Microsoft.AspNetCore.Http.Features;

namespace FubarDev.FtpServer
{
    /// <summary>
    /// The context in which the command gets executed.
    /// </summary>
    public class FtpContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FtpContext"/> class.
        /// </summary>
        /// <param name="command">The FTP command.</param>
        /// <param name="serverCommandWriter">The FTP response writer.</param>
        /// <param name="connection">The FTP connection.</param>
        [Obsolete("Use the overload with the features parameter.")]
        public FtpContext(
            FtpCommand command,
            ChannelWriter<IServerCommand> serverCommandWriter,
            IFtpConnection connection)
        {
            Command = command;
            ServerCommandWriter = serverCommandWriter;
            Connection = connection;
            Features = connection.Features;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FtpContext"/> class.
        /// </summary>
        /// <param name="command">The FTP command.</param>
        /// <param name="serverCommandWriter">The FTP response writer.</param>
        /// <param name="features">The FTP connection features.</param>
        public FtpContext(
            FtpCommand command,
            ChannelWriter<IServerCommand> serverCommandWriter,
            IFeatureCollection features)
        {
            Command = command;
            ServerCommandWriter = serverCommandWriter;
            Features = features;
#pragma warning disable 618
            Connection = new FtpConnectionCompat(features);
#pragma warning restore 618
        }

        /// <summary>
        /// Gets the FTP command to be executed.
        /// </summary>
        public FtpCommand Command { get; }

        /// <summary>
        /// Gets the FTP connection.
        /// </summary>
        [Obsolete("Access the features directly.")]
        public IFtpConnection Connection { get; }

        /// <summary>
        /// Gets the connection features.
        /// </summary>
        public IFeatureCollection Features { get; }

        /// <summary>
        /// Gets the response writer.
        /// </summary>
        public ChannelWriter<IServerCommand> ServerCommandWriter { get; }
    }
}
