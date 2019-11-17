// <copyright file="FeatureCollectionExtensions.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;
using System.Threading;
using System.Threading.Tasks;

using FubarDev.FtpServer.Features;

using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace FubarDev.FtpServer
{
    /// <summary>
    /// Extension methods for <see cref="IFeatureCollection"/>.
    /// </summary>
    internal static class FeatureCollectionExtensions
    {
        /// <summary>
        /// Gets the service provider from the features.
        /// </summary>
        /// <param name="features">The features to get the service provider from.</param>
        /// <returns>The service provider.</returns>
        public static IServiceProvider GetServiceProvider(this IFeatureCollection features)
        {
            return features.Get<IServiceProvidersFeature>()?.RequestServices
                   ?? throw new InvalidOperationException("Service provider not available");
        }

        /// <summary>
        /// Reset all features.
        /// </summary>
        /// <param name="features">The features to reset.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="logger">The logger.</param>
        /// <returns>The task.</returns>
        public static async ValueTask ResetAsync(
            this IFeatureCollection features,
            CancellationToken cancellationToken,
            ILogger? logger = null)
        {
            foreach (var featureItem in features)
            {
                var remove = false;
                try
                {
                    switch (featureItem.Value)
                    {
                        case IResettableFeature f:
                            await f.ResetAsync(cancellationToken).ConfigureAwait(false);
                            break;
                        case IFtpDataConnectionFeature f:
                            remove = true;
                            await f.DisposeAsync().ConfigureAwait(false);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    // Ignore exceptions
                    logger?.LogWarning(ex, "Failed to dispose feature of type {featureType}: {errorMessage}", featureItem.Key, ex.Message);
                }

                if (remove)
                {
                    // Remove from features collection
                    features[featureItem.Key] = null;
                }
            }
        }
    }
}
