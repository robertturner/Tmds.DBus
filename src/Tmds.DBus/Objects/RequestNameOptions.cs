using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tmds.DBus.Objects
{
    [Flags]
    public enum RequestNameOptions : uint
    {
        None = 0,
        AllowReplacement = 0x1,
        ReplaceExisting = 0x2,
        DoNotQueue = 0x4,
    }
}
