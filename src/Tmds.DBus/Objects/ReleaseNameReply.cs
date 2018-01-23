using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tmds.DBus.Objects
{
    public enum ReleaseNameReply : uint
    {
        ReplyReleased = 1,
        NonExistent,
        NotOwner
    }
}
