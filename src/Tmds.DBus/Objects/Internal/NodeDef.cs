using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Tmds.DBus.Protocol;

namespace Tmds.DBus.Objects.Internal
{
    [Serializable]
    public class NodeDef
    {
        [XmlAttribute("name")]
        public string Name { get; set; }

        public class ArgDef
        {
            [XmlAttribute("name")]
            public string Name { get; set; }
            [XmlAttribute("direction")]
            public string Dir { get; set; }
            [XmlAttribute("type")]
            public string Type { get; set; }
        }

        public class InterfaceDef
        {
            public class MethodDef
            {
                [XmlAttribute("name")]
                public string Name { get; set; }
                [XmlElement("arg")]
                public List<ArgDef> Args { get; set; }
            }
            public class PropertyDef
            {
                [XmlAttribute("name")]
                public string Name { get; set; }
                [XmlAttribute("type")]
                public string Type { get; set; }
                [XmlAttribute("access")]
                public string Access { get; set; }
            }

            public class SignalDef
            {
                [XmlAttribute("name")]
                public string Name { get; set; }
                [XmlElement("arg")]
                public List<ArgDef> Args { get; set; }
            }

            [XmlAttribute("name")]
            public string Name { get; set; }
            [XmlElement("method")]
            public List<MethodDef> Methods { get; set; }

            public InterfaceObjDef.MethodDef[] GetMethodDefs()
            {
                return Methods.Select(m =>
                {
                    var argsIn = m.Args.Where(a => a.Dir == "in");
                    var argsOut = m.Args.Where(a => a.Dir == "out");
                    return new InterfaceObjDef.MethodDef(m.Name,
                        argsOut.Select(a => new InterfaceObjDef.ArgDef(a.Name, new Signature(a.Type))).ToArray(),
                        argsIn.Select(a => new InterfaceObjDef.ArgDef(a.Name, new Signature(a.Type))).ToArray());
                }).ToArray();
            }


            [XmlElement("property")]
            public List<PropertyDef> Properties { get; set; }

            public InterfaceObjDef.PropertyDef[] GetPropertyDefs()
            {
                return Properties.Select(p =>
                {
                    var access = InterfaceObjDef.PropertyDef.AccessTypes.None;
                    switch (p.Access)
                    {
                        case "read": access = InterfaceObjDef.PropertyDef.AccessTypes.Read; break;
                        case "write": access = InterfaceObjDef.PropertyDef.AccessTypes.Write; break;
                        case "readwrite": access = InterfaceObjDef.PropertyDef.AccessTypes.ReadWrite; break;
                    }
                    return new InterfaceObjDef.PropertyDef(p.Name, new InterfaceObjDef.ArgDef(p.Name, new Signature(p.Type)), access);
                }).ToArray();
            }

            [XmlElement("signal")]
            public List<SignalDef> Signals { get; set; }

            public InterfaceObjDef.SignalDef[] GetSignalDefs()
            {
                return Signals.Select(s =>
                {
                    return new InterfaceObjDef.SignalDef(s.Name,
                        s.Args.Select(a => new InterfaceObjDef.ArgDef(a.Name, new Signature(a.Type))).ToArray());
                }).ToArray();
            }
        }

        [XmlElement("interface")]
        public List<InterfaceDef> Interfaces { get; set; }

        public InterfaceObjDef[] GetInterfaceDefs()
        {
            return Interfaces.Select(ifaceDef =>
            {
                return new InterfaceObjDef(ifaceDef.Name,
                    ifaceDef.GetMethodDefs(),
                    ifaceDef.GetPropertyDefs(),
                    ifaceDef.GetSignalDefs());
            }).ToArray();
        }

        [XmlElement("node")]
        public List<NodeDef> Nodes { get; set; }
    }
}
