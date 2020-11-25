using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Tmds.DBus.Protocol;
using Tmds.DBus.Objects.Internal;
using Tmds.DBus.Objects.DBus;
using BaseLibs.Collections;

namespace Tmds.DBus.Objects
{
    public static class IIntrospectable_Extensions
    {
        public static async Task<(InterfaceObjDef[] Interfaces, string[] Nodes, string NodePath)> GetObjectDefs(this IIntrospectable obj)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));

            var xmlStr = await obj.Introspect();

            var ser = new XmlSerializer(typeof(NodeDef), new XmlRootAttribute("node"));
            using (var stream = new System.IO.StringReader(xmlStr))
            {
                var nodeDef = (NodeDef)ser.Deserialize(stream);
                var interfaces = nodeDef.GetInterfaceDefs();
                return (interfaces, nodeDef.Nodes.Select(n => n.Name).ToArray(), nodeDef.Name);
            }
        }

        public static async Task<NodeDef> GetNodeDefs(this IIntrospectable obj)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));

            var xmlStr = await obj.Introspect();

            var ser = new XmlSerializer(typeof(NodeDef), new XmlRootAttribute("node"));
            using (var stream = new System.IO.StringReader(xmlStr))
            {
                return (NodeDef)ser.Deserialize(stream);
            }
        }

        static async Task<NodeDef> IntrospectPath(this Connection connection, string service, ObjectPath path)
        {
            var introspectable = connection.CreateProxy<IIntrospectable>(service, path);
            return await introspectable.GetNodeDefs();
        }

        private class PathTreeDef : IPathTreeDef
        {
            public readonly Connection Connection;
            public readonly string Service;

            public ObjectPath Path { get; private set; }

            public InterfaceObjDef[] Interfaces { get; set; } = new InterfaceObjDef[0];

            public IReadOnlyList<(string Name, Lazy<Task<IPathTreeDef>> Node)> Nodes { get; set; } = new List<(string Name, Lazy<Task<IPathTreeDef>> Node)>();

            private PathTreeDef(Connection connection, string service, ObjectPath path)
            {
                Connection = connection; Service = service; Path = path;
            }

            public static async Task<PathTreeDef> Introspect(Connection connection, string service)
            {
                var rootNodeDef = await connection.IntrospectPath(service, ObjectPath.Root);

                var rootDef = new PathTreeDef(connection, service, ObjectPath.Root);

                async Task nodeParse(PathTreeDef thisTreeDef, NodeDef currentNode, IEnumerable<string> currentPath)
                {
                    if (!string.IsNullOrEmpty(currentNode.Name))
                    {
                        var nodePath = new ObjectPath(currentNode.Name);
                        var nodePathParts = nodePath.Decomposed.ToArray();

                        var passedInTreeDef = thisTreeDef;
                        var intermediatePaths = currentPath;
                        bool skipFirst = false;
                        if (nodePath.IsAbsolute)
                        {
                            thisTreeDef = rootDef;
                            currentPath = nodePathParts;
                        }
                        else
                        {
                            var thisTreeDefPath = thisTreeDef.Path.Decomposed;
                            if (nodePathParts.Length > 0 && thisTreeDefPath.Any() && (nodePathParts[0] == thisTreeDefPath.Last()))
                                skipFirst = true;
                            currentPath = currentPath.Concat(skipFirst ? nodePathParts.Skip(1) : nodePathParts);
                            intermediatePaths = Enumerable.Empty<string>();
                        }
                        
                        for (int i = skipFirst ? 1 : 0; i < nodePathParts.Length; ++i)
                        {
                            var part = nodePathParts[i];
                            bool lastPart = i == (nodePathParts.Length - 1);

                            intermediatePaths = intermediatePaths.Concat(part.SingleItemAsEnumerable());
                            bool found = false;
                            foreach (var (Name, Node) in thisTreeDef.Nodes)
                            {
                                if (Name == part)
                                {
                                    if (!lastPart)
                                        thisTreeDef = (PathTreeDef)(await Node.Value);
                                    else
                                        thisTreeDef = passedInTreeDef;
                                    found = true;
                                    break;
                                }
                            }
                            if (!found)
                            {
                                var item = new PathTreeDef(connection, service, new ObjectPath(intermediatePaths));
                                var itemLazy = new Lazy<Task<IPathTreeDef>>(() => Task.FromResult<IPathTreeDef>(item));
                                var _ = itemLazy.Value;
                                ((List<(string Name, Lazy<Task<IPathTreeDef>> Node)>)thisTreeDef.Nodes).Add((part, itemLazy));
                                thisTreeDef = item;
                            }
                        }
                        
                    }
                    if (currentNode.Interfaces != null && currentNode.Interfaces.Count > 0)
                    {
                        var ifces = currentNode.GetInterfaceDefs();
                        thisTreeDef.Interfaces = ifces;
                    }

                    if (currentNode.Nodes != null)
                    {
                        foreach (var subNode in currentNode.Nodes)
                        {
                            if (string.IsNullOrEmpty(subNode.Name))
                                continue; // Badly formatted

                            // Shouldn't happen
                            for (int i = 0; i < thisTreeDef.Nodes.Count; ++i)
                            {
                                if (thisTreeDef.Nodes[i].Name == subNode.Name)
                                {
#if true
                                    throw new ArgumentException("Received Introspect node definition for an already-definied node!");
#else
                                    ((List<(string Name, Lazy<Task<IPathTreeDef>> Node)>)thisTreeDef.Nodes).RemoveAt(i);
                                    break;
#endif
                                }
                            }
                            var subPath = currentPath.Concat(subNode.Name.SingleItemAsEnumerable());
                            var subObjPath = new ObjectPath(subPath);

                            Lazy<Task<IPathTreeDef>> subNodeDef;
                            var subPathTreeDef = new PathTreeDef(connection, service, subObjPath);
                            if ((subNode.Interfaces == null || subNode.Interfaces.Count == 0) && 
                                (subNode.Nodes == null || subNode.Nodes.Count == 0))
                            {
                                subNodeDef = new Lazy<Task<IPathTreeDef>>(async () =>
                                {
                                    var subDef = await connection.IntrospectPath(service, subObjPath);
                                    await nodeParse(subPathTreeDef, subDef, subPath);
                                    return subPathTreeDef;
                                });
                            }
                            else
                            {
                                await nodeParse(subPathTreeDef, subNode, subPath);
                                subNodeDef = new Lazy<Task<IPathTreeDef>>(() => Task.FromResult<IPathTreeDef>(subPathTreeDef));
                                var _ = subNodeDef.Value; // force eval
                            }
                            ((List<(string Name, Lazy<Task<IPathTreeDef>> Node)>)thisTreeDef.Nodes).Add((subNode.Name, subNodeDef));
                        }
                    }
                }
                await nodeParse(rootDef, rootNodeDef, Enumerable.Empty<string>());
                return rootDef;
            }
        }

        public static async Task<IPathTreeDef> GetPathTree(this Connection connection, string service)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));
            if (service == null)
                throw new ArgumentNullException(nameof(service));

            return await PathTreeDef.Introspect(connection, service);
        }

    }
}
