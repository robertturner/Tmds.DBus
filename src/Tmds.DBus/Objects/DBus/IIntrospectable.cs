using Tmds.DBus.Objects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tmds.DBus.Objects
{
    [DBusInterface("org.freedesktop.DBus.Introspectable")]
    public interface IIntrospectable
    {
        [return: DBusArgName("xml_data")]
        Task<string> Introspect();
    }
}
