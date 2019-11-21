// <copyright file="ImplicitFtpsControlConnectionStreamAdapter.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System.IO;
using System.Threading;
using System.Threading.Tasks;

using FubarDev.FtpServer;
using FubarDev.FtpServer.Authentication;

namespace TestFtpServer.Utilities
{
    internal class ImplicitFtpsControlConnectionStreamAdapter : IFtpControlStreamAdapter
    {
        private readonly ImplicitFtpsControlConnectionStreamAdapterOptions _options;
        private readonly ISslStreamWrapperFactory _sslStreamWrapperFactory;

        public ImplicitFtpsControlConnectionStreamAdapter(
            ImplicitFtpsControlConnectionStreamAdapterOptions options,
            ISslStreamWrapperFactory sslStreamWrapperFactory)
        {
            _options = options;
            _sslStreamWrapperFactory = sslStreamWrapperFactory;
        }

        /// <inheritdoc />
        public Task<Stream> WrapAsync(Stream stream, CancellationToken cancellationToken)
        {
            return _sslStreamWrapperFactory.WrapStreamAsync(stream, false, _options.Certificate, cancellationToken);
        }
    }
}
