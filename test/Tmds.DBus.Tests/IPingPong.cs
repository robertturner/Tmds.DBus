using System;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus.Objects;

namespace Tmds.DBus.Tests
{
    [DBusInterface("tmds.dbus.tests.PingPong")]
    public interface IPingPong : IDBusObject
    {
        Task<string> EchoAsync(string message);
        Task PingAsync(string message);
        Task<IDisposable> WatchPongAsync(Action<string> reply);
        Task<IDisposable> WatchPongNoArgAsync(Action reply);
        //Task<IDisposable> WatchPongWithExceptionAsync(Action<string> reply, Action<Exception> onError);
    }
}