using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tmds.DBus.Protocol
{
    public static class Message_Extensions
    {
        public static IEnumerable<object> GetObjs(this Message msg)
        {
            var reader = new MessageReader(msg);
            if (!msg.Header.Signature.HasValue)
                return Enumerable.Empty<object>();
            var sig = msg.Header.Signature.Value;
            return sig.GetParts().Select(p => reader.Read(p.ToType()));
        }
        public static IEnumerable<object> GetObjs(this Message msg, params Type[] types)
        {
            var reader = new MessageReader(msg);
            if (!msg.Header.Signature.HasValue)
            {
                if (types.Length > 0)
                    throw new ArgumentException($"Message signature parts count (0) is different to types array length ({types.Length})");
                return Enumerable.Empty<object>();
            }
            var sig = msg.Header.Signature.Value;
            var sigParts = sig.GetParts().ToArray();
            if (sigParts.Length != types.Length)
                throw new ArgumentException($"Message signature parts count ({sigParts.Length}) is different to types array length ({types.Length})");
            return types.Select(t => reader.Read(t));
        }

        public static void WriteObjs(this Message msg, IEnumerable<(object Obj, Type Type)> objs)
        {
            var sig = new Signature();
            var writer = new MessageWriter();
            bool any = false;
            foreach (var obj in objs)
            {
                any = true;
                var t = obj.Type ?? obj.Obj.GetType();
                var sigPart = Signature.GetSig(t);
                sig = Signature.Concat(sig, sigPart);
                writer.Write(t, obj.Obj);
            }
            if (any)
            {
                msg.Body = writer.ToArray();
                msg.Header.Signature = sig;
            }
        }
        public static void WriteObjs(this Message msg, object[] objs, Type[] types = null)
        {
            if (objs == null || objs.Length == 0)
            {
                if (types != null && types.Length > 0)
                    throw new ArgumentException("types must be same length as objs");
                return;
            }
            if (types != null && types.Length != objs.Length)
                throw new ArgumentException("types must be same length as objs");
            var writer = new MessageWriter();
            for (int i = 0; i < objs.Length; ++i)
                writer.Write((types != null) ? types[i] : objs[i].GetType(), objs[i]);
            msg.Body = writer.ToArray();
            msg.Header.Signature = Signature.GetSig(types);
        }

    }
}
