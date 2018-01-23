using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace Tmds.DBus.Objects.Internal
{
    internal class ProxyContext : ICallContext
    {
        internal ProxyContext(IConnection connection, ObjectPath? path, Message msg)
        {
            Connection = connection; CurrentPath = path; MessageReceived = msg;
        }
        public IConnection Connection { get; internal set; }
        public ObjectPath? CurrentPath { get; internal set; }
        public Message MessageReceived { get; internal set; }
    }
}
