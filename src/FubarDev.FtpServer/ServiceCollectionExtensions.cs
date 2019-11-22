// <copyright file="ServiceCollectionExtensions.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System;
using System.Reflection;

using FubarDev.FtpServer;
using FubarDev.FtpServer.AccountManagement.Directories.SingleRootWithoutHome;
using FubarDev.FtpServer.Authentication;
using FubarDev.FtpServer.Authorization;
using FubarDev.FtpServer.BackgroundTransfer;
using FubarDev.FtpServer.CommandExtensions;
using FubarDev.FtpServer.CommandHandlers;
using FubarDev.FtpServer.Commands;
using FubarDev.FtpServer.DataConnection;
using FubarDev.FtpServer.FileSystem;
using FubarDev.FtpServer.Localization;
using FubarDev.FtpServer.ServerCommandHandlers;
using FubarDev.FtpServer.ServerCommands;

using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Extension methods for <see cref="IServiceCollection"/>.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds the FTP server services to the collection.
        /// </summary>
        /// <param name="services">The service collection to add the FTP server services to.</param>
        /// <param name="configure">Configuration of the FTP server services.</param>
        /// <returns>The service collection.</returns>
        public static IServiceCollection AddFtpServer(
            this IServiceCollection services,
            Action<IFtpServerBuilder> configure)
        {
            services.AddOptions();

            services.AddSingleton<IFtpServer, FtpServer>();
            services.AddSingleton<ITemporaryDataFactory, TemporaryDataFactory>();
            services.AddSingleton<IPasvListenerFactory, PasvListenerFactory>();
            services.AddSingleton<IPasvAddressResolver, SimplePasvAddressResolver>();
#pragma warning disable 618
            services.AddSingleton<IFtpConnectionAccessor, FtpConnectionAccessor>();
#pragma warning restore 618
            services.AddSingleton<IFtpConnectionContextAccessor, FtpConnectionContextAccessor>();
            services.AddSingleton<FtpServerStatisticsCollector>();

            // Statistics are always transient to get the current values.
            services.AddTransient(sp => sp.GetRequiredService<FtpServerStatisticsCollector>().GetStatistics());

            var commandAssembly = typeof(PassCommandHandler).GetTypeInfo().Assembly;

            // Command handlers
            services.AddSingleton<IFtpCommandHandlerScanner>(
                _ => new AssemblyFtpCommandHandlerScanner(commandAssembly));
            services.TryAddScoped<IFtpCommandHandlerProvider, DefaultFtpCommandHandlerProvider>();

            // Command handler extensions
            services.AddScoped<IFtpCommandHandlerExtensionScanner>(
                sp => new AssemblyFtpCommandHandlerExtensionScanner(
                    sp.GetRequiredService<IFtpCommandHandlerProvider>(),
                    sp.GetService<ILogger<AssemblyFtpCommandHandlerExtensionScanner>>(),
                    commandAssembly));
            services.TryAddScoped<IFtpCommandHandlerExtensionProvider, DefaultFtpCommandHandlerExtensionProvider>();

            // Activator for FTP commands (and extensions)
            services.AddScoped<IFtpCommandActivator, DefaultFtpCommandActivator>();

            // Feature provider
            services.AddScoped<IFeatureInfoProvider, DefaultFeatureInfoProvider>();

            services.AddScoped<IFtpLoginStateMachine, FtpLoginStateMachine>();
            services.AddScoped<IFtpCommandDispatcher, DefaultFtpCommandDispatcher>();

            services.AddScoped<IFtpHostSelector, SingleFtpHostSelector>();

            services.AddSingleton<IFtpCatalogLoader, DefaultFtpCatalogLoader>();
            services.TryAddSingleton<IFtpServerMessages, DefaultFtpServerMessages>();

            services.AddSingleton<IBackgroundTransferWorker, BackgroundTransferWorker>();

            services.AddSingleton(sp => (IFtpService)sp.GetRequiredService<IFtpServer>());
            services.AddSingleton(sp => (IFtpService)sp.GetRequiredService<IBackgroundTransferWorker>());

            services.AddSingleton<IFtpServerHost, FtpServerHost>();

            services.AddSingleton<ISslStreamWrapperFactory, DefaultSslStreamWrapperFactory>();

            services.TryAddSingleton<IAccountDirectoryQuery, SingleRootWithoutHomeAccountDirectoryQuery>();

            services.Scan(
                sel => sel.FromAssemblyOf<IAuthorizationAction>()
                   .AddClasses(filter => filter.AssignableTo<IAuthorizationAction>()).As<IAuthorizationAction>().WithSingletonLifetime());

            services.Scan(
                sel => sel.FromAssemblyOf<PasswordAuthorization>()
                   .AddClasses(filter => filter.AssignableTo<IAuthorizationMechanism>()).As<IAuthorizationMechanism>().WithScopedLifetime());

            services.Scan(
                sel => sel.FromAssemblyOf<TlsAuthenticationMechanism>()
                   .AddClasses(filter => filter.AssignableTo<IAuthenticationMechanism>()).As<IAuthenticationMechanism>().WithScopedLifetime());

            services
               .AddSingleton<IServerCommandExecutor, ReflectionServerCommandExecutor>();

            services
               .AddScoped<IServerCommandHandler<SendResponseServerCommand>, SendResponseServerCommandHandler>()
               .AddScoped<IServerCommandHandler<CloseConnectionServerCommand>, CloseConnectionServerCommandHandler>()
               .AddScoped<IServerCommandHandler<TlsEnableServerCommand>, TlsEnableServerCommandHandler>()
               .AddScoped<IServerCommandHandler<PauseConnectionServerCommand>, PauseConnectionServerCommandHandler>()
               .AddScoped<IServerCommandHandler<ResumeConnectionServerCommand>, ResumeConnectionServerCommandHandler>()
               .AddScoped<IServerCommandHandler<DataConnectionServerCommand>, DataConnectionServerCommandHandler>()
               .AddScoped<IServerCommandHandler<CloseDataConnectionServerCommand>, CloseDataConnectionServerCommandHandler>();

            services
               .AddSingleton<ActiveDataConnectionFeatureFactory>()
               .AddSingleton<PassiveDataConnectionFeatureFactory>()
               .AddSingleton<SecureDataConnectionWrapper>();

            services
               .AddSingleton<IFtpDataConnectionValidator, PromiscuousPasvDataConnectionValidator>();

            configure(new FtpServerBuilder(services).EnableDefaultChecks());

            return services;
        }

        private class FtpServerBuilder : IFtpServerBuilder
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="FtpServerBuilder"/> class.
            /// </summary>
            /// <param name="services">The service collection.</param>
            public FtpServerBuilder(IServiceCollection services)
            {
                Services = services;
            }

            /// <inheritdoc />
            public IServiceCollection Services { get; }
        }
    }
}
