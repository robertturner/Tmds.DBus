using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Tmds.DBus.Objects
{
    public static class IClientObjectProvider_Extensions
    {
        public static T GetInstance<T>(this IClientObjectProvider provider, ObjectPath path, string serviceName)
        {
            var interfaceAttribute = typeof(T).GetCustomAttribute<DBusInterfaceAttribute>(false);
            if (interfaceAttribute == null)
                throw new ArgumentException($"{nameof(DBusInterfaceAttribute)} missing");
            return provider.GetInstance<T>(path, interfaceAttribute.Name, serviceName);
        }
    }
}
