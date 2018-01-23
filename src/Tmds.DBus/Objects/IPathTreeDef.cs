using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Tmds.DBus.Objects
{
    public interface IPathTreeDef
    {
        ObjectPath Path { get; }

        InterfaceObjDef[] Interfaces { get; }

        IReadOnlyList<(string Name, Lazy<Task<IPathTreeDef>> Node)> Nodes { get; }

    }
}
