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

        IDBusObjectProxy ProxyInstance { get; }
    }

    public interface IDBusObjectProxy<T> : IDBusObjectProxy
    {
    }
}
