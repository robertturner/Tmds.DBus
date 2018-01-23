using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tmds.DBus.Objects
{
    public interface ICallContext
    {
        IConnection Connection { get; }
        ObjectPath? CurrentPath { get; }
        Protocol.Message MessageReceived { get; }
    }
}
