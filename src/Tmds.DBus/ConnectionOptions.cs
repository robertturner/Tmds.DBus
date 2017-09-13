// Copyright 2017 Tom Deseyn <tom.deseyn@gmail.com>
// This software is made available under the MIT License
// See COPYING for details

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tmds.DBus
{
    /// <summary>
    /// Options that configure the behavior of a Connection.
    /// </summary>
    public abstract class ConnectionOptions
    {
        /// <summary>
        /// SynchronizationContext used for event handlers and callbacks.
        /// </summary>
        public SynchronizationContext SynchronizationContext { get; set; }

        /// <summary>
        /// Automatically connect and re-connect the Connection.
        /// </summary>
        public bool AutoConnect { get; set; }

        /// <summary>
        /// Sets up tunnel/connects to the remote peer.
        /// </summary>
        protected internal abstract Task<ConnectionContext> SetupAsync();

        /// <summary>
        /// Action to clean up resources created during succesfull execution of SetupAsync.
        /// </summary>
        protected internal virtual void Teardown(object token)
        {}
    }
}