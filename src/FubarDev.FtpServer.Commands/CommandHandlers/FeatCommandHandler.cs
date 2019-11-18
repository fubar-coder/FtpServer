//-----------------------------------------------------------------------
// <copyright file="FeatCommandHandler.cs" company="Fubar Development Junker">
//     Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>
// <author>Mark Junker</author>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using FubarDev.FtpServer.Commands;

using Microsoft.AspNetCore.Connections;

namespace FubarDev.FtpServer.CommandHandlers
{
    /// <summary>
    /// Implements the <c>FEAT</c> command.
    /// </summary>
    [FtpCommandHandler("FEAT", isLoginRequired: false)]
    public class FeatCommandHandler : FtpCommandHandler
    {
        private readonly IFtpConnectionContextAccessor _connectionContextAccessor;
        private readonly IFeatureInfoProvider _featureInfoProvider;
        private readonly IFtpLoginStateMachine _loginStateMachine;

        /// <summary>
        /// Initializes a new instance of the <see cref="FeatCommandHandler"/> class.
        /// </summary>
        /// <param name="connectionContextAccessor">The FTP connection context accessor.</param>
        /// <param name="featureInfoProvider">Provider for feature information.</param>
        /// <param name="loginStateMachine">The login state machine.</param>
        public FeatCommandHandler(
            IFtpConnectionContextAccessor connectionContextAccessor,
            IFeatureInfoProvider featureInfoProvider,
            IFtpLoginStateMachine loginStateMachine)
        {
            _connectionContextAccessor = connectionContextAccessor;
            _featureInfoProvider = featureInfoProvider;
            _loginStateMachine = loginStateMachine;
        }

        /// <inheritdoc/>
        public override Task<IFtpResponse?> Process(FtpCommand command, CancellationToken cancellationToken)
        {
            var isAuthorized = _loginStateMachine.Status == SecurityStatus.Authorized;
            var features = _featureInfoProvider.GetFeatureInfoItems()
               .Where(x => IsFeatureAllowed(x, isAuthorized))
               .SelectMany(x => BuildInfo(_connectionContextAccessor.Context, x))
               .Distinct(StringComparer.OrdinalIgnoreCase)
               .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
               .ToList();

            if (features.Count == 0)
            {
                return Task.FromResult<IFtpResponse?>(new FtpResponse(211, T("No extensions supported")));
            }

            return Task.FromResult<IFtpResponse?>(
                new FtpResponseList(
                    211,
                    T("Extensions supported:"),
                    T("END"),
                    features.Distinct(StringComparer.OrdinalIgnoreCase)));
        }

        private IEnumerable<string> BuildInfo(
            ConnectionContext context,
            FoundFeatureInfo foundFeatureInfo)
        {
            if (foundFeatureInfo.IsCommandHandler)
            {
                return foundFeatureInfo.FeatureInfo.BuildInfo(foundFeatureInfo.CommandHandlerInfo.Type, context);
            }

            if (foundFeatureInfo.IsExtension)
            {
                return foundFeatureInfo.FeatureInfo.BuildInfo(foundFeatureInfo.ExtensionInfo.Type, context);
            }

            if (foundFeatureInfo.IsAuthenticationMechanism)
            {
                return foundFeatureInfo.FeatureInfo.BuildInfo(foundFeatureInfo.AuthenticationMechanism.GetType(), context);
            }

            throw new NotSupportedException("Unknown feature source.");
        }

        private bool IsFeatureAllowed(FoundFeatureInfo foundFeatureInfo, bool isAuthorized)
        {
            if (foundFeatureInfo.IsCommandHandler)
            {
                return isAuthorized || !foundFeatureInfo.CommandHandlerInfo.IsLoginRequired;
            }

            if (foundFeatureInfo.IsExtension)
            {
                return isAuthorized || !foundFeatureInfo.ExtensionInfo.IsLoginRequired;
            }

            if (foundFeatureInfo.IsAuthenticationMechanism)
            {
                return true;
            }

            throw new NotSupportedException("Unknown feature source.");
        }
    }
}
