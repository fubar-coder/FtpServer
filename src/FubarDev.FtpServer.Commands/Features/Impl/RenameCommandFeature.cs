// <copyright file="RenameCommandFeature.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System.Threading;
using System.Threading.Tasks;

using FubarDev.FtpServer.FileSystem;

namespace FubarDev.FtpServer.Features.Impl
{
    /// <summary>
    /// Default feature implementation for the RNFR/RNTO commands.
    /// </summary>
    internal class RenameCommandFeature : IRenameCommandFeature, IResettableFeature
    {
        public RenameCommandFeature(SearchResult<IUnixFileSystemEntry> renameFrom)
        {
            RenameFrom = renameFrom;
        }

        /// <inheritdoc />
        public SearchResult<IUnixFileSystemEntry>? RenameFrom { get; set; }

        /// <inheritdoc />
        public Task ResetAsync(CancellationToken cancellationToken)
        {
            RenameFrom = null;
            return Task.CompletedTask;
        }
    }
}
