using System;
using System.Linq;
using Tmds.DBus.Protocol;

namespace Tmds.DBus.Objects
{
    public class InterfaceObjDef
    {
        public InterfaceObjDef(string name, MethodDef[] methods = null, PropertyDef[] properties = null, SignalDef[] signals = null)
        {
            InterfaceName = name;
            Methods = methods ?? new MethodDef[0];
            Properties = properties ?? new PropertyDef[0];
            Signals = signals ?? new SignalDef[0];
        }

        public class ArgDef
        {
            public ArgDef(Signature sig) : this(null, sig) { }
            public ArgDef(string name, Signature sig)
            {
                Name = name ?? string.Empty;
                Signature = sig;
            }
            public string Name { get; private set; }
            public Signature Signature { get; private set; }
            public override string ToString()
            {
                var sigStr = Signature.ToString();
                if (string.IsNullOrEmpty(Name))
                    return sigStr;
                return $"{{{Name}: {sigStr}}}";
            }
            public string GetTypeString()
            {
                var typeStr = Signature.AsType().ToString();
                if (string.IsNullOrEmpty(Name))
                    return typeStr;
                return $"{{{Name}: {typeStr}}}";
            }
        }

        public string InterfaceName { get; protected set; }
        public class MethodDef
        {
            public MethodDef(string name, ArgDef[] returnTypes = null, ArgDef[] argTypes = null)
            {
                Name = name;
                ReturnTypes = returnTypes ?? new ArgDef[0];
                ArgTypes = argTypes ?? new ArgDef[0];
            }
            protected MethodDef(string name) { Name = name; }
            public string Name { get; private set; }
            ArgDef[] returnTypes;
            public ArgDef[] ReturnTypes
            {
                get => returnTypes;
                protected set
                {
                    returnTypes = value;
                    ReturnSignature = new Signature(string.Concat(returnTypes.Select(a => a.Signature.Value)));
                }
            }
            public Signature ReturnSignature { get; private set; }

            ArgDef[] argTypes;
            public ArgDef[] ArgTypes
            {
                get => argTypes;
                protected set
                {
                    argTypes = value;
                    MethodSignature = new Signature(string.Concat(argTypes.Select(a => a.Signature.Value)));
                }
            }
            public Signature MethodSignature { get; private set; }
        }
        public virtual MethodDef[] Methods { get; private set; }
        public class PropertyDef
        {
            public enum AccessTypes
            {
                None,
                Read,
                Write,
                ReadWrite
            }
            public PropertyDef(string name, ArgDef type, AccessTypes access) { Name = name; Type = type; Access = access; }
            public string Name { get; private set; }
            public ArgDef Type { get; private set; }
            public AccessTypes Access { get; private set; }
        }
        public virtual PropertyDef[] Properties { get; private set; }
        public class SignalDef
        {
            public SignalDef(string name, ArgDef[] argTypes)
            {
                Name = name;
                ArgDefs = argTypes ?? new ArgDef[0];
            }
            public string Name { get; private set; }
            public ArgDef[] ArgDefs { get; private set; }
        }
        public virtual SignalDef[] Signals { get; private set; }

    }
}
