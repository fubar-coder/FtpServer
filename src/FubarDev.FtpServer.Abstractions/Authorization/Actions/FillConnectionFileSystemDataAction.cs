// <copyright file="FillConnectionFileSystemDataAction.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using FubarDev.FtpServer.Features;
using FubarDev.FtpServer.FileSystem;

namespace FubarDev.FtpServer.Authorization.Actions
{
    /// <summary>
    /// Fills the connection data upon successful authorization.
    /// </summary>
    public class FillConnectionFileSystemDataAction : IAuthorizationAction
    {
        private readonly IFtpConnectionContextAccessor _connectionContextAccessor;
        private readonly IFileSystemClassFactory _fileSystemFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="FillConnectionFileSystemDataAction"/> class.
        /// </summary>
        /// <param name="connectionContextAccessor">The FTP connection context accessor.</param>
        /// <param name="fileSystemFactory">The file system factory.</param>
        public FillConnectionFileSystemDataAction(
            IFtpConnectionContextAccessor connectionContextAccessor,
            IFileSystemClassFactory fileSystemFactory)
        {
            _connectionContextAccessor = connectionContextAccessor;
            _fileSystemFactory = fileSystemFactory;
        }

        /// <inheritdoc />
        public int Level { get; } = 1800;

        /// <inheritdoc />
        public async Task AuthorizedAsync(IAccountInformation accountInformation, CancellationToken cancellationToken)
        {
            var features = _connectionContextAccessor.Context.Features;
            var fsFeature = features.Get<IFileSystemFeature>();
            fsFeature.FileSystem = await _fileSystemFactory
               .Create(accountInformation)
               .ConfigureAwait(false);

            fsFeature.SetInitialPath(new Stack<IUnixDirectoryEntry>());
        }
    }
}
