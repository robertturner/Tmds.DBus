using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace Tmds.DBus.Objects
{
    public class ReplyArgumentsDifferentFromExpectedException : Exception
    {
        public ReplyArgumentsDifferentFromExpectedException(MethodInfo method, Signature expectedSignature, Message replyMessage)
            : base($"Expected signature: {expectedSignature.Value}, but received: {replyMessage.Header.Signature}")
        {
            Method = method; ExpectedSignature = expectedSignature; ReplyMessage = replyMessage;
        }

        public MethodInfo Method { get; private set; }
        public Signature ExpectedSignature { get; private set; }
        public Message ReplyMessage { get; private set; }
    }
}
