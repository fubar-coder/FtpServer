// <copyright file="EmptyTableConfiguration.cs" company="Fubar Development Junker">
// Copyright (c) Fubar Development Junker. All rights reserved.
// </copyright>

using BetterConsoleTables;

namespace TestFtpServer.Shell
{
    /// <summary>
    /// Configuration for a table without borders.
    /// </summary>
    public class EmptyTableConfiguration : TableConfiguration
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EmptyTableConfiguration"/> class.
        /// </summary>
        public EmptyTableConfiguration()
        {
            hasHeaderRow = false;
            hasInnerRows = false;
            hasBottomRow = false;
            hasInnerColumns = false;
            hasOuterColumns = false;
            hasTopRow = false;
            innerColumnDelimiter = ' ';
        }
    }
}
