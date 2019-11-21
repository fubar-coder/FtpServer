// <copyright file="ImplicitFtpsControlConnectionStreamAdapterOptions.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using System.Security.Cryptography.X509Certificates;

namespace TestFtpServer.Utilities
{
    internal class ImplicitFtpsControlConnectionStreamAdapterOptions
    {
        public ImplicitFtpsControlConnectionStreamAdapterOptions(X509Certificate certificate)
        {
            Certificate = certificate;
        }

        public X509Certificate Certificate { get; }
    }
}
