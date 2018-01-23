using System.IO;
using System.Threading.Tasks;
using Xunit;
using XunitSkip;

namespace Tmds.DBus.Tests
{
    public class TransportTests
    {
        [InlineData(DBusDaemonProtocol.Tcp)]
        [InlineData(DBusDaemonProtocol.Unix)]
        [InlineData(DBusDaemonProtocol.UnixAbstract)]
        [SkippableTheory]
        public async Task Transport(DBusDaemonProtocol protocol)
        {
            if (!File.Exists("dbus-daemon"))
                throw new SkipTestException("dbus-daemon not present");

            if (DBusDaemon.IsSELinux && protocol == DBusDaemonProtocol.Tcp)
            {
                throw new SkipTestException("Cannot provide SELinux context to DBus daemon over TCP");
            }
            using (var dbusDaemon = new DBusDaemon())
            {
                await dbusDaemon.StartAsync(protocol);
                var connection = new Connection(dbusDaemon.Address);
                await connection.ConnectAsync();

                Assert.StartsWith(":", connection.LocalName);
                Assert.Equal(true, connection.RemoteIsBus);
            }
        }

        [SkippableFact]
        public async Task TryMultipleAddresses()
        {
            if (!File.Exists("dbus-daemon"))
                throw new SkipTestException("dbus-daemon not present");

            using (var dbusDaemon = new DBusDaemon())
            {
                await dbusDaemon.StartAsync();

                string address = "unix:path=/does/not/exist;"
                                 + dbusDaemon.Address;

                var connection = new Connection(address);
                await connection.ConnectAsync();

                Assert.StartsWith(":", connection.LocalName);
                Assert.Equal(true, connection.RemoteIsBus);
            }
        }
    }
}