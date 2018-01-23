using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tmds.DBus.Objects
{
    public enum RequestNameReply : uint
    {
        PrimaryOwner = 1,
        InQueue,
        Exists,
        AlreadyOwner,
    }
}
