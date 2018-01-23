using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Tmds.DBus.Objects.Internal
{
    [Serializable]
    public class NodeDef
    {
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
            [XmlElement("property")]
            public List<PropertyDef> Properties { get; set; }
            [XmlElement("signal")]
            public List<SignalDef> Signals { get; set; }

        }

        public class SubNodeDef
        {
            [XmlAttribute("name")]
            public string Name { get; set; }
        }

        [XmlElement("interface")]
        public List<InterfaceDef> Interfaces { get; set; }

        [XmlElement("node")]
        public List<SubNodeDef> Nodes { get; set; }
    }
}
