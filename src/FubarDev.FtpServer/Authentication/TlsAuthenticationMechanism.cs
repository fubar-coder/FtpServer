// <copyright file="TlsAuthenticationMechanism.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FubarDev.FtpServer.Features;
using FubarDev.FtpServer.ServerCommands;

using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FubarDev.FtpServer.Authentication
{
    /// <summary>
    /// Implementation for the <c>AUTH TLS</c> command.
    /// </summary>
    [FtpFeatureFunction(nameof(CreateAuthTlsFeatureString))]
    public class TlsAuthenticationMechanism : AuthenticationMechanism
    {
        private readonly ISslStreamWrapperFactory _sslStreamWrapperFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="TlsAuthenticationMechanism"/> class.
        /// </summary>
        /// <param name="connectionContextAccessor">The FTP connection context accessor.</param>
        /// <param name="sslStreamWrapperFactory">The SslStream wrapper factory.</param>
        public TlsAuthenticationMechanism(
            IFtpConnectionContextAccessor connectionContextAccessor,
            ISslStreamWrapperFactory sslStreamWrapperFactory)
            : base(connectionContextAccessor.Context)
        {
            _sslStreamWrapperFactory = sslStreamWrapperFactory;
        }

        /// <summary>
        /// Build a string to be returned by the <c>FEAT</c> command handler.
        /// </summary>
        /// <param name="connectionContext">The FTP connection context.</param>
        /// <returns>The string(s) to be returned.</returns>
        public static IEnumerable<string> CreateAuthTlsFeatureString(ConnectionContext connectionContext)
        {
            var serviceProvider = connectionContext.Features.GetServiceProvider();
            var hostSelector = serviceProvider.GetRequiredService<IFtpHostSelector>();
            if (hostSelector.SelectedHost.Certificate != null)
            {
                var authTlsOptions = serviceProvider.GetRequiredService<IOptions<AuthTlsOptions>>();
                if (authTlsOptions.Value.ImplicitFtps)
                {
                    return new[] { "PBSZ", "PROT" };
                }

                return new[] { "AUTH TLS", "PBSZ", "PROT" };
            }

            return new string[0];
        }

        /// <inheritdoc />
        public override void Reset()
        {
        }

        /// <inheritdoc />
        public override bool CanHandle(string methodIdentifier)
        {
            return string.Equals(methodIdentifier.Trim(), "TLS", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(methodIdentifier.Trim(), "SSL", StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        public override async Task<IFtpResponse> HandleAuthAsync(string methodIdentifier, CancellationToken cancellationToken)
        {
            var serverCommandWriter = Features.Get<IServerCommandFeature>().ServerCommandWriter;
            var hostSelector = Features.GetServiceProvider().GetRequiredService<IFtpHostSelector>();

            if (hostSelector.SelectedHost.Certificate == null)
            {
                return new FtpResponse(500, T("Syntax error, command unrecognized."));
            }

            await serverCommandWriter.WriteAsync(
                    new PauseConnectionServerCommand(),
                    cancellationToken)
               .ConfigureAwait(false);

            var enableTlsResponse = new FtpResponse(234, T("Enabling TLS Connection"));
            await serverCommandWriter.WriteAsync(
                    new SendResponseServerCommand(enableTlsResponse),
                    cancellationToken)
               .ConfigureAwait(false);

            await serverCommandWriter.WriteAsync(
                    new TlsEnableServerCommand(),
                    cancellationToken)
               .ConfigureAwait(false);

            await serverCommandWriter.WriteAsync(
                    new ResumeConnectionServerCommand(),
                    cancellationToken)
               .ConfigureAwait(false);

            return new FtpResponse(234, null);
        }

        /// <inheritdoc />
        public override Task<IFtpResponse> HandleAdatAsync(byte[] data, CancellationToken cancellationToken)
        {
            return Task.FromResult<IFtpResponse>(new FtpResponse(421, T("Service not available")));
        }

        /// <inheritdoc />
        public override Task<IFtpResponse> HandlePbszAsync(long size, CancellationToken cancellationToken)
        {
            IFtpResponse response;

            var hostSelector = Features.GetServiceProvider().GetRequiredService<IFtpHostSelector>();
            if (hostSelector.SelectedHost.Certificate == null)
            {
                response = new FtpResponse(500, T("Syntax error, command unrecognized."));
            }
            else if (size != 0)
            {
                response = new FtpResponse(501, T("A protection buffer size other than 0 is not supported. Use PBSZ=0 instead."));
            }
            else
            {
                response = new FtpResponse(200, T("Protection buffer size set to {0}.", size));
            }

            return Task.FromResult(response);
        }

        /// <inheritdoc />
        public override Task<IFtpResponse> HandleProtAsync(string protCode, CancellationToken cancellationToken)
        {
            IFtpResponse response;

            var hostSelector = Features.GetServiceProvider().GetRequiredService<IFtpHostSelector>();
            if (hostSelector.SelectedHost.Certificate == null)
            {
                response = new FtpResponse(500, T("Syntax error, command unrecognized."));
            }
            else
            {
                var secureConnectionFeature = Features.Get<ISecureConnectionFeature>();
                switch (protCode.ToUpperInvariant())
                {
                    case "C":
                        secureConnectionFeature.CreateEncryptedStream = Task.FromResult;
                        response = new FtpResponse(200, T("Data channel protection level set to {0}.", protCode));
                        break;
                    case "P":
                        secureConnectionFeature.CreateEncryptedStream = stream => CreateSslStream(hostSelector.SelectedHost, stream);
                        response = new FtpResponse(200, T("Data channel protection level set to {0}.", protCode));
                        break;
                    default:
                        response = new FtpResponse(
                            SecurityActionResult.RequestedProtLevelNotSupported,
                            T("A data channel protection level other than C, or P is not supported."));
                        break;
                }
            }

            return Task.FromResult(response);
        }

        private async Task<Stream> CreateSslStream(
            IFtpHost host,
            Stream unencryptedStream)
        {
            if (host.Certificate == null)
            {
                throw new InvalidOperationException(T("No server certificate configured."));
            }

            var sslStream = await _sslStreamWrapperFactory.WrapStreamAsync(
                    unencryptedStream,
                    false,
                    host.Certificate,
                    CancellationToken.None)
               .ConfigureAwait(false);
            return sslStream;
        }
    }
}
