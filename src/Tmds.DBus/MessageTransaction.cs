using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace Tmds.DBus
{
    public sealed class MessageTransaction : TransactionBase
    {
        internal MessageTransaction() { }

        public Message Request { get; internal set; }

        public Message Reply { get; internal set; }

        public static implicit operator Message(MessageTransaction mt) => mt.Reply;
    }
}
