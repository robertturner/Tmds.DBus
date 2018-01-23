using System;

namespace Tmds.DBus.Protocol
{
    public static class Signature_Extensions
    {

        public static Type AsType(this Signature sig)
        {
            if (sig.IsSingleCompleteType)
                return sig.ToType();
            throw new Exception("Non-single-complete data types not supported yet");
        }
        public static Type AsType(this Signature? sig)
        {
            if (!sig.HasValue)
                return typeof(void);
            if (sig.Value.IsSingleCompleteType)
                return sig.Value.ToType();
            throw new Exception("Non-single-complete data types not supported yet");
        }

    }
}
