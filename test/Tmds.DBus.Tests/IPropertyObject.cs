using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus.Objects;

namespace Tmds.DBus.Tests
{
    [DBusInterface("tmds.dbus.tests.PropertyObject")]
    public interface IPropertyObject : IDBusObject
    {
        Task<IDictionary<string, object>> GetAll();
        Task<object> Get(string prop);
        Task Set(string prop, object val);
        Task<IDisposable> WatchProperties(Action<(string name, object value)> handler);
    }
}