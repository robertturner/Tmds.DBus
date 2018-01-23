using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tmds.DBus.Objects
{
    public interface IClientObjectProvider : IDisposable
    {
        IDBusConnection DBusConnection { get; set; }
        IDBusObjectProxy GetInstance(Type type, ObjectPath path, string interfaceName, string serviceName);
        T GetInstance<T>(ObjectPath path, string interfaceName, string serviceName);
        //void DisposeInstance(object instance);
    }
}
