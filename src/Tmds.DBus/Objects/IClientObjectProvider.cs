using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tmds.DBus.Objects
{
    public enum ProviderPreferences
    {
        UnableToGet,
        Preferred,
        Unpreffered
    }

    public interface IClientObjectProvider : IDisposable
    {
        IDBusConnection DBusConnection { get; set; }
        IConnection Connection { get; }

        ProviderPreferences TypePreference<T>();

        T GetInstance<T>(ObjectPath path, string interfaceName, string serviceName, out IDBusObjectProxy<T> container);
    }
}
