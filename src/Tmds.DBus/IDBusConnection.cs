// Copyright 2016 Tom Deseyn <tom.deseyn@gmail.com>
// This software is made available under the MIT License
// See COPYING for details

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;
using Tmds.DBus.Objects;
using Tmds.DBus.Objects.DBus;

namespace Tmds.DBus
{
    public interface IDBusConnection : IDisposable
    {
        Task<Message> CallMethodAsync(Message message, bool checkConnected = true);
        bool? RemoteIsBus { get; }
        string LocalName { get; }
        IDBus DBus { get; }
        IClientObjectProvider ProxyProvider { get; }

        Task<IDisposable> WatchSignalAsync(SignalHandler handler, string @interface, string signalName, string sender, ObjectPath? path = null);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="path">Path to register interface at, or null to register for every level (global)</param>
        /// <param name="interfaceName"></param>
        /// <param name="handler"></param>
        void AddMethodHandler(ObjectPath? path, string interfaceName, IMethodHandler handler);

        IMethodHandler RemoveMethodHandler(ObjectPath? path, string interfaceName);

        void EmitSignal(Message message);

        IEnumerable<string> RegisteredPathChildren(ObjectPath path);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="path"></param>
        /// <returns>Interfaces. Returned as IEnumerable as there may be multiple interfaces of same name</returns>
        IEnumerable<(string InterfaceName, IMethodHandler Handler)> RegisteredPathHandlers(ObjectPath? path, string maskInterface = null);

        bool IsDisposed { get; }
    }
}