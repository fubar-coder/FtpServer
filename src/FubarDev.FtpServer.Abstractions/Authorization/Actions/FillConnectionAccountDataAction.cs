// <copyright file="FillConnectionAccountDataAction.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Connections.Features;

namespace FubarDev.FtpServer.Authorization.Actions
{
    /// <summary>
    /// Fills the connection data upon successful authorization.
    /// </summary>
    public class FillConnectionAccountDataAction : IAuthorizationAction
    {
        private readonly IFtpConnectionContextAccessor _connectionContextAccessor;

        /// <summary>
        /// Initializes a new instance of the <see cref="FillConnectionAccountDataAction"/> class.
        /// </summary>
        /// <param name="connectionContextAccessor">The FTP connection context accessor.</param>
        public FillConnectionAccountDataAction(
            IFtpConnectionContextAccessor connectionContextAccessor)
        {
            _connectionContextAccessor = connectionContextAccessor;
        }

        /// <inheritdoc />
        public int Level { get; } = 1900;

        /// <inheritdoc />
        public Task AuthorizedAsync(IAccountInformation accountInformation, CancellationToken cancellationToken)
        {
            var features = _connectionContextAccessor.Context.Features;

            var connUserFeature = features.Get<IConnectionUserFeature>();
            connUserFeature.User = accountInformation.FtpUser;

            return Task.CompletedTask;
        }
    }
}
