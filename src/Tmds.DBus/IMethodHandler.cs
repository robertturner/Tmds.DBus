using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tmds.DBus.Objects;
using Tmds.DBus.Protocol;

namespace Tmds.DBus
{
    public interface IMethodHandler : IDisposable
    {
        bool CheckExposure(ObjectPath path);
        Task<Message> HandleMethodCall(Message message);
        InterfaceObjDef GetInterfaceDefinitions();
    }
}
