using System;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus.Objects;
using Tmds.DBus.Protocol;

namespace Tmds.DBus.Tests
{
    [DBusInterface("tmds.dbus.tests.StringOperations")]
    public interface IStringOperations : IDBusObject
    {
        Task<string> Concat(string s1, string s2);
    }
}