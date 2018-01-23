using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus.Objects;
using Xunit;
using XunitSkip;

namespace Tmds.DBus.Tests
{
    public class ConnectionTests
    {
        [Fact]
        public async Task Method()
        {
            var connections = await PairedConnection.CreateConnectedPairAsync();
            var conn1 = connections.Item1;
            var conn2 = connections.Item2;
            var proxy = conn1.CreateProxy<IStringOperations>("servicename", StringOperations.Path);
            //await conn2.RegisterObject(new StringOperations());
            conn2.RegisterObject(new StringOperations());
            var reply = await proxy.Concat("hello ", "world");
            Assert.Equal("hello world", reply);
        }

        [Fact]
        public async Task Signal()
        {
            var connections = await PairedConnection.CreateConnectedPairAsync();
            var conn1 = connections.Item1;
            var conn2 = connections.Item2;
            var proxy = conn1.CreateProxy<IPingPong>("", PingPong.Path);
            var tcs = new TaskCompletionSource<string>();
            await proxy.WatchPongAsync(message => tcs.SetResult(message));
            conn2.RegisterObject(new PingPong());
            await proxy.PingAsync("hello world");
            var reply = await tcs.Task;
            Assert.Equal("hello world", reply);
        }

        [Fact]
        public async Task SignalNoArg()
        {
            var connections = await PairedConnection.CreateConnectedPairAsync();
            var conn1 = connections.Item1;
            var conn2 = connections.Item2;
            var proxy = conn1.CreateProxy<IPingPong>("", PingPong.Path);
            var tcs = new TaskCompletionSource<string>();
            await proxy.WatchPongNoArgAsync(() => tcs.SetResult(null));
            conn2.RegisterObject(new PingPong());
            await proxy.PingAsync("hello world");
            var reply = await tcs.Task;
            Assert.Equal(null, reply);
        }

        /*[Fact]
        public async Task SignalWithException()
        {
            var connections = await PairedConnection.CreateConnectedPairAsync();
            var conn1 = connections.Item1;
            var conn2 = connections.Item2;
            var proxy = conn1.CreateProxy<IPingPong>("servicename", PingPong.Path);
            var tcs = new TaskCompletionSource<string>();
            await proxy.WatchPongWithExceptionAsync(message => tcs.SetResult(message), null);
            await conn2.RegisterObject(new PingPong());
            await proxy.PingAsync("hello world");
            var reply = await tcs.Task;
            Assert.Equal("hello world", reply);
        }*/

        [Fact]
        public async Task Properties()
        {
            var connections = await PairedConnection.CreateConnectedPairAsync();
            var conn1 = connections.Item1;
            var conn2 = connections.Item2;
            var proxy = conn1.CreateProxy<IPropertyObject>("", PropertyObject.Path);
            var dictionary = new Dictionary<string, object>{{"key1", 1}, {"key2", 2}};
            conn2.RegisterObject(new PropertyObject(dictionary));

            var properties = await proxy.GetAll();
            Assert.Equal(dictionary, properties);

            var val1 = await proxy.Get("key1");
            Assert.Equal(1, val1);

            var tcs = new TaskCompletionSource<(string Name, object Value)>();
            await proxy.WatchProperties(_ => tcs.SetResult(_));
            await proxy.Set("key1", "changed");

            var val1Changed = await proxy.Get("key1");
            Assert.Equal("changed", val1Changed);

            var changes = await tcs.Task;
            Assert.Equal("key1", changes.Name);
            Assert.Equal("changed", changes.Value);
        }

        /*[InlineData("tcp:host=localhost,port=1")]
        [InlineData("unix:path=/does/not/exist")]
        [SkippableTheory]
        public async Task UnreachableAddress(string address)
        {
            if (address.StartsWith("unix:"))
                throw new SkipTestException("Not unix");
            using (var connection = new Connection(address))
            {
                await Assert.ThrowsAsync<Exception>(() => connection.ConnectAsync());
            }
        }*/

        [DBusInterface("tmds.dbus.tests.Throw")]
        public interface IThrow : IDBusObject
        {
            Task ThrowAsync();
        }

        public class Throw : IThrow
        {
            public static readonly ObjectPath Path = new ObjectPath("/tmds/dbus/tests/throw");
            public static readonly string ExceptionMessage = "throwing";

            public ObjectPath ObjectPath => Path;

            public Task ThrowAsync()
            {
                throw new Exception(ExceptionMessage);
            }
        }

        [Fact]
        public async Task PassException()
        {
            var connections = await PairedConnection.CreateConnectedPairAsync();
            var conn1 = connections.Item1;
            var conn2 = connections.Item2;
            var proxy = conn1.CreateProxy<IThrow>("servicename", Throw.Path);
            conn2.RegisterObject(new Throw());
            var exception = await Assert.ThrowsAsync<DBusException>(proxy.ThrowAsync);
            Assert.Equal(Throw.ExceptionMessage, exception.ErrorMessage);
        }
    }
}