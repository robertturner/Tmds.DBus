using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tmds.DBus
{
    public abstract class TransactionBase
    {
        public DateTime? RequestSendTime { get; internal set; }
        public DateTime? ReplyReceivedTime { get; internal set; }

        public TimeSpan? TransactionTime => (RequestSendTime.HasValue && ReplyReceivedTime.HasValue)
            ? (ReplyReceivedTime.Value - RequestSendTime.Value) : (TimeSpan?)null;
    }
}
