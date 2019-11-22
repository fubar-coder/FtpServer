// <copyright file="PipeWrapperStream.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace FubarDev.FtpServer.Networking
{
    /// <summary>
    /// A stream that uses a pipe.
    /// </summary>
    internal class PipeWrapperStream : Stream
    {
        private readonly Stream _input;
        private readonly Stream _output;

        /// <summary>
        /// Initializes a new instance of the <see cref="PipeWrapperStream"/> class.
        /// </summary>
        /// <param name="duplexPipe">The duplex pipe to create the stream for.</param>
        public PipeWrapperStream(IDuplexPipe duplexPipe)
            : this(duplexPipe.Input, duplexPipe.Output)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PipeWrapperStream"/> class.
        /// </summary>
        /// <param name="input">The pipe reader to be used to read from.</param>
        /// <param name="output">The pipe writer to be used to write to.</param>
        public PipeWrapperStream(
            PipeReader input,
            PipeWriter output)
        {
            _input = input.AsStream();
            _output = output.AsStream();
        }

        /// <inheritdoc />
        public override bool CanRead => _input.CanRead;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => _output.CanWrite;

        /// <inheritdoc />
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc />
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count)
        {
            return _input.Read(buffer, offset, count);
        }

        /// <inheritdoc />
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _input.ReadAsync(buffer, offset, count, cancellationToken);
        }

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count)
        {
            _output.Write(buffer, offset, count);
        }

        /// <inheritdoc />
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _output.WriteAsync(buffer, offset, count, cancellationToken);
        }

        /// <inheritdoc />
        public override void Flush()
        {
            _output.Flush();
        }

        /// <inheritdoc />
        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return _output.FlushAsync(cancellationToken);
        }
    }
}
