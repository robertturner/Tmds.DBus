using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Tmds.DBus.Protocol;
using Tmds.DBus.Objects.Internal;
using Tmds.DBus.Objects.DBus;

namespace Tmds.DBus.Objects
{
    public static class IIntrospectable_Extensions
    {
        public static async Task<(InterfaceObjDef[] Interfaces, string[] Nodes)> GetObjectDefs(this IIntrospectable obj)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));

            var xmlStr = await obj.Introspect();

            var ser = new XmlSerializer(typeof(NodeDef), new XmlRootAttribute("node"));
            using (var stream = new System.IO.StringReader(xmlStr))
            {
                var nodeDef = (NodeDef)ser.Deserialize(stream);

                var interfaces = nodeDef.Interfaces.Select(ifaceDef =>
                {
                    return new InterfaceObjDef(ifaceDef.Name, ifaceDef.Methods.Select(m =>
                    {
                        var argsIn = m.Args.Where(a => a.Dir == "in");
                        var argsOut = m.Args.Where(a => a.Dir == "out");
                        return new InterfaceObjDef.MethodDef(m.Name, 
                            argsOut.Select(a => new InterfaceObjDef.ArgDef(a.Name, new Signature(a.Type))).ToArray(),
                            argsIn.Select(a => new InterfaceObjDef.ArgDef(a.Name, new Signature(a.Type))).ToArray());
                    }).ToArray(), ifaceDef.Properties.Select(p =>
                    {
                        var access = InterfaceObjDef.PropertyDef.AccessTypes.None;
                        switch (p.Access)
                        {
                            case "read": access = InterfaceObjDef.PropertyDef.AccessTypes.Read; break;
                            case "write": access = InterfaceObjDef.PropertyDef.AccessTypes.Write; break;
                            case "readwrite": access = InterfaceObjDef.PropertyDef.AccessTypes.ReadWrite; break;
                        }
                        return new InterfaceObjDef.PropertyDef(p.Name, new InterfaceObjDef.ArgDef(p.Name, new Signature(p.Type)), access);
                    }).ToArray(), ifaceDef.Signals.Select(s =>
                    {
                        return new InterfaceObjDef.SignalDef(s.Name,
                            s.Args.Select(a => new InterfaceObjDef.ArgDef(a.Name, new Signature(a.Type))).ToArray());
                    }).ToArray());
                }).ToArray();
                return (interfaces, nodeDef.Nodes.Select(n => n.Name).ToArray());
            }
        }


        private class PathTreeDef : IPathTreeDef
        {
            public Connection Connection { get; set; }
            public string Service { get; set; }

            private PathTreeDef() { }

            public static async Task<PathTreeDef> GetDef(Connection connection, string service, ObjectPath path)
            {
                var introspectable = connection.CreateProxy<IIntrospectable>(service, path);
                var objDefs = await introspectable.GetObjectDefs();
                var treeDef = new PathTreeDef
                {
                    Connection = connection, Service = service, Path = path,
                    Interfaces = objDefs.Interfaces,
                    Nodes = objDefs.Nodes.Select(name =>
                    {
                        return (name, new Lazy<Task<IPathTreeDef>>(async () =>
                        {
                            var pathStr = path.ToString();
                            var newPath = path + ((pathStr[pathStr.Length - 1] == '/') ? name : ('/' + name));
                            return await GetDef(connection, service, newPath);
                        }));
                    }).ToList()
                };
                return treeDef;
            }

            public ObjectPath Path { get; private set; }

            public InterfaceObjDef[] Interfaces { get; set; }

            public IReadOnlyList<(string Name, Lazy<Task<IPathTreeDef>> Node)> Nodes { get; set; }
        }

        public static async Task<IPathTreeDef> GetPathTree(this Connection connection, string service, ObjectPath path)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));
            if (service == null)
                throw new ArgumentNullException(nameof(service));

            return await PathTreeDef.GetDef(connection, service, path);
        }

    }
}
