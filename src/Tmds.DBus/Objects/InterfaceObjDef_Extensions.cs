using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace Tmds.DBus.Objects
{
    public static class InterfaceObjDef_Extensions
    {
        public static Type ReturnTypesAsType(this InterfaceObjDef.MethodDef methodDef)
        {
            if (methodDef == null)
                throw new ArgumentNullException(nameof(methodDef));

            var returnTypes = methodDef.ReturnTypes.ToArray();
            if (returnTypes.Length == 1)
                return returnTypes[0].Signature.AsType();

            return Signature.TypeOfValueTupleOf(returnTypes.Select(t => t.Signature.AsType()).ToArray());
        }

    }
}
