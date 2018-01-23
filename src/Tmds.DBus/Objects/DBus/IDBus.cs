// Copyright 2016 Tom Deseyn <tom.deseyn@gmail.com>
// This software is made available under the MIT License
// See COPYING for details

using System;
using System.Threading.Tasks;

namespace Tmds.DBus.Objects.DBus
{
    [DBusInterface(DBusConnection.DBusInterface)]
    public interface IDBus
    {
        Task<string> Hello();
        Task<RequestNameReply> RequestName(string name, RequestNameOptions flags);
        Task<ReleaseNameReply> ReleaseName(string name);
        Task<bool> NameHasOwner(string name);
        Task<string[]> ListNames();
        Task<string[]> ListActivatableNames();
        Task<ServiceStartResult> StartServiceByName(string name, uint flags);
        Task<string> GetNameOwner(string name);
        Task<string[]> ListQueuedOwners(string name);
        Task AddMatch(string rule);
        Task RemoveMatch(string rule);
        Task<string> GetId();

        // Signals
        Task<IDisposable> WatchNameAcquired(Action<string> callback);
        Task<IDisposable> WatchNameLost(Action<string> callback);
        Task<IDisposable> WatchNameOwnerChanged(Action<(string name, string old_owner, string new_owner)> callback);
    }
}
