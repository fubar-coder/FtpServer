// <copyright file="PamSessionAuthorizationAction.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System.Threading;
using System.Threading.Tasks;

using FubarDev.FtpServer.Authorization;

namespace FubarDev.FtpServer.MembershipProvider.Pam
{
    /// <summary>
    /// Action that opens a PAM session upon authentication.
    /// </summary>
    public class PamSessionAuthorizationAction : IAuthorizationAction
    {
        private readonly IFtpConnectionContextAccessor _connectionContextAccessor;

        /// <summary>
        /// Initializes a new instance of the <see cref="PamSessionAuthorizationAction"/> class.
        /// </summary>
        /// <param name="connectionContextAccessor">The FTP connection context accessor.</param>
        public PamSessionAuthorizationAction(
            IFtpConnectionContextAccessor connectionContextAccessor)
        {
            _connectionContextAccessor = connectionContextAccessor;
        }

        /// <inheritdoc />
        public int Level => 1850;

        /// <inheritdoc />
        public Task AuthorizedAsync(IAccountInformation accountInformation, CancellationToken cancellationToken)
        {
            if (!accountInformation.FtpUser.IsUnixUser())
            {
                return Task.CompletedTask;
            }

            var features = _connectionContextAccessor.Context.Features;
            var pamSessionFeature = features.Get<PamSessionFeature>();
            if (pamSessionFeature == null)
            {
                return Task.CompletedTask;
            }

            try
            {
                pamSessionFeature.OpenSession();
            }
            catch
            {
                // Ignore errors...
            }

            return Task.CompletedTask;
        }
    }
}
