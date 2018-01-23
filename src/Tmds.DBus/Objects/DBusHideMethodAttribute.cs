using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tmds.DBus.Objects
{
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class DBusHideMethodAttribute : Attribute
    {
    }
}
