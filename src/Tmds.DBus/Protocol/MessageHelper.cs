// Copyright 2006 Alp Toker <alp@atoker.com>
// Copyright 2010 Alan McGovern <alan.mcgovern@gmail.com>
// Copyright 2016 Tom Deseyn <tom.deseyn@gmail.com>
// This software is made available under the MIT License
// See COPYING for details

using BaseLibs.Enums;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Tmds.DBus.Protocol
{
    internal static class MessageHelper
    {
        public static Message ConstructErrorReply(Message incoming, string errorName, string errorMessage)
        {
            MessageWriter writer = new MessageWriter(incoming.Header.Endianness);
            writer.WriteString(errorMessage);
            var replyMessage = new Message(new Header(MessageType.Error)
                {
                    ErrorName = errorName,
                    ReplySerial = incoming.Header.Serial,
                    Signature = Signature.StringSig,
                    Destination = incoming.Header.Sender
                }, writer.ToArray());
            return replyMessage;
        }
        public static Message ConstructErrorReply(Message incoming, DBusErrors error, string errorMessage)
        {
            return ConstructErrorReply(incoming, error.GetDescription(), errorMessage);
        }


        public static Message ConstructReply(Message msg, params object[] vals)
        {
            var replyMsg = new Message(new Header(MessageType.MethodReturn)
            {
                ReplySerial = msg.Header.Serial
            });
            replyMsg.WriteObjs(vals);
            if (msg.Header.Sender != null)
                replyMsg.Header.Destination = msg.Header.Sender;

            return replyMsg;
        }
        public static Message ConstructReplyWithTypes(Message msg, params (object Obj, Type Type)[] vals)
        {
            var replyMsg = new Message(new Header(MessageType.MethodReturn)
            {
                ReplySerial = msg.Header.Serial
            });
            replyMsg.WriteObjs(vals);
            if (!string.IsNullOrEmpty(msg.Header.Sender))
                replyMsg.Header.Destination = msg.Header.Sender;
            return replyMsg;
        }

    }
}
