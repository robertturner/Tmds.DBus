using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tmds.DBus.Protocol
{
    public enum DBusErrors
    {
        [Description("org.freedesktop.DBus.Error.Failed")]
        Failed,
        [Description("org.freedesktop.DBus.Error.NoMemory")]
        NoMemory,
        [Description("org.freedesktop.DBus.Error.ServiceUnknown")]
        ServiceUnknown,
        [Description("org.freedesktop.DBus.Error.NameHasNoOwner")]
        NameHasNoOwner,
        [Description("org.freedesktop.DBus.Error.NoReply")]
        NoReply,
        [Description("org.freedesktop.DBus.Error.IOError")]
        IOError,
        [Description("org.freedesktop.DBus.Error.BadAddress")]
        BadAddress,
        [Description("org.freedesktop.DBus.Error.NotSupported")]
        NotSupported,
        [Description("org.freedesktop.DBus.Error.LimitsExceeded")]
        LimitsExceeded,
        [Description("org.freedesktop.DBus.Error.AccessDenied")]
        AccessDenied,
        [Description("org.freedesktop.DBus.Error.AuthFailed")]
        AuthFailed,
        [Description("org.freedesktop.DBus.Error.NoServer")]
        NoServer,
        [Description("org.freedesktop.DBus.Error.Timeout")]
        Timeout,
        [Description("org.freedesktop.DBus.Error.NoNetwork")]
        NoNetwork,
        [Description("org.freedesktop.DBus.Error.AddressInUse")]
        AddressInUse,
        [Description("org.freedesktop.DBus.Error.Disconnected")]
        Disconnected,
        [Description("org.freedesktop.DBus.Error.InvalidArgs")]
        InvalidArgs,
        [Description("org.freedesktop.DBus.Error.FileNotFound")]
        FileNotFound,
        [Description("org.freedesktop.DBus.Error.FileExists")]
        FileExists,
        [Description("org.freedesktop.DBus.Error.UnknownMethod")]
        UnknownMethod,
        [Description("org.freedesktop.DBus.Error.UnknownObject")]
        UnknownObject,
        [Description("org.freedesktop.DBus.Error.UnknownInterface")]
        UnknownInterface,
        [Description("org.freedesktop.DBus.Error.UnknownProperty")]
        UnknownProperty,
        [Description("org.freedesktop.DBus.Error.PropertyReadOnly")]
        PropertyReadOnly,
        [Description("org.freedesktop.DBus.Error.UnixProcessIdUnknown")]
        UnixProcessIdUnknown,
        [Description("org.freedesktop.DBus.Error.InvalidSignature")]
        InvalidSignature,
        [Description("org.freedesktop.DBus.Error.InconsistentMessage")]
        InconsistentMessage,
        [Description("org.freedesktop.DBus.Error.MatchRuleNotFound")]
        MatchRuleNotFound,
        [Description("org.freedesktop.DBus.Error.MatchRuleInvalid")]
        MatchRuleInvalid,
        [Description("org.freedesktop.DBus.Error.InteractiveAuthorizationRequired")]
        InteractiveAuthorizationRequired,
    }
}
