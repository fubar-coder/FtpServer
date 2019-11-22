// <copyright file="DuplexPipe.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System.IO.Pipelines;

namespace FubarDev.FtpServer.Networking
{
    internal class DuplexPipe : IDuplexPipe
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
