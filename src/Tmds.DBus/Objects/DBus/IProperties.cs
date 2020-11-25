using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tmds.DBus.Objects.DBus
{
    [DBusInterface("org.freedesktop.DBus.Properties")]
    [StaticProxy]
    public interface IProperties
    {
        [return: DBusArgName("value")]
        Task<object> Get(string interface_name, string property_name);

        Task Set(string interface_name, string property_name, object value);

        [return: DBusArgName("props")]
        Task<Dictionary<string, object>> GetAll(string interface_name);

        Task<IDisposable> WatchPropertiesChanged(PropertiesChangedHandler callback);
    }

    public delegate void PropertiesChangedHandler(string interface_name, Dictionary<string, object> changed_properties, string[] invalidated_properties, [DBusPath] ObjectPath path);

}
