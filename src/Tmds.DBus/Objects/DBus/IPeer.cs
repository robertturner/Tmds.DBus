using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tmds.DBus.Objects.DBus
{
    [DBusInterface("org.freedesktop.DBus.Peer")]
    [StaticProxy]
    public interface IPeer
    {
        [return: DBusArgName("machine_uuid")]
        Task<string> GetMachineId();
        Task Ping();
    }
}
