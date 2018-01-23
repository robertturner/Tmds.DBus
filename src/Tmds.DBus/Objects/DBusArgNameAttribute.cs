using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tmds.DBus.Objects
{
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = false, Inherited = true)]
    public class DBusArgNameAttribute : Attribute
    {
        public DBusArgNameAttribute(string name = "") { Name = name ?? string.Empty; }
        public string Name { get; private set; }
    }
}
