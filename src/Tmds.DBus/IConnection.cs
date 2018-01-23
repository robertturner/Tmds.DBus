// Copyright 2016 Tom Deseyn <tom.deseyn@gmail.com>
// This software is made available under the MIT License
// See COPYING for details

using Tmds.DBus.Objects;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus.Objects.DBus;

namespace Tmds.DBus
{
    public interface IConnection : IDisposable
    {
        bool? RemoteIsBus { get; }
        string LocalName { get; }
        IDBus DBus { get; }
        IDBusConnection BaseDBusConnection { get; }

        Task ConnectAsync(Action<Exception> onDisconnect = null, CancellationToken cancellationToken = default(CancellationToken), IClientObjectProvider objProvider = default(IClientObjectProvider));

        void RegisterObject(ObjectPath? path, string interfaceName, object instance, MemberExposure exposure);
        void UnregisterObject(ObjectPath? path, string interfaceName);
    }
}
