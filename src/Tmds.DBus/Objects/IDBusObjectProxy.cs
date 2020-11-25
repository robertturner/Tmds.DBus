using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tmds.DBus.Objects
{
    public interface IDBusObjectProxy : IDisposable
    {
        ObjectPath ObjectPath { get; }
        string InterfaceName { get; }
        string Service { get; }

        Type Type { get; }

        IConnection Connection { get; }

        object ProxyInstance { get; }

        event Action<DBusException> ExceptionHook;

        /// <summary>
        /// Gives a boolean if the service goes up or down
        /// </summary>
        event Action<bool> ServiceUpHook;
    }

    public interface IDBusObjectProxy<T> : IDBusObjectProxy
    {
        new T ProxyInstance { get; }
    }
}
