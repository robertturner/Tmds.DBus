// Copyright 2016 Tom Deseyn <tom.deseyn@gmail.com>
// This software is made available under the MIT License
// See COPYING for details

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus.Transports;
using Tmds.DBus.Protocol;
using Tmds.DBus.Objects;
using System.Linq;
using Tmds.DBus.Objects.DBus;
using BaseLibs.Collections;
using Tmds.DBus.Objects.Internal;

namespace Tmds.DBus
{
    public class DBusConnection : IDBusConnection
    {
        struct PendingSend
        {
            public Message Message;
            public TaskCompletionSource<bool> CompletionSource;
        }
        class SignalHandlerRegistration : IDisposable
        {
            public SignalHandlerRegistration(DBusConnection dbusConnection, SignalMatchRule rule, SignalHandler handler)
            {
                _connection = dbusConnection;
                _rule = rule;
                _handler = handler;
            }
            public void Dispose()
            {
                _connection.RemoveSignalHandler(_rule, _handler);
            }
            DBusConnection _connection;
            SignalMatchRule _rule;
            SignalHandler _handler;
        }
        class SignalMatchRule
        {
            public SignalMatchRule(string @interface, string member, ObjectPath? path, string sender)
            {
                Interface = @interface; Member = member; Path = path; Sender = sender;
            }

            public string Interface { get; private set; }
            public string Member { get; private set; }
            public ObjectPath? Path { get; private set; }
            public string Sender { get; private set; }

            public override int GetHashCode()
            {
                int hash = 17;
                hash = hash * 23 + (Interface == null ? 0 : Interface.GetHashCode());
                hash = hash * 23 + (Member == null ? 0 : Member.GetHashCode());
                hash = hash * 23 + Path.GetHashCode();
                hash = hash * 23 + (Sender == null ? 0 : Sender.GetHashCode());
                return hash;
            }

            public override bool Equals(object o)
            {
                var r = o as SignalMatchRule;
                if (o == null)
                    return false;
                return Interface == r.Interface &&
                    Member == r.Member &&
                    Path == r.Path &&
                    Sender == r.Sender;
            }

            public override string ToString()
            {
                var sb = new StringBuilder();
                Append(sb, "type", "signal");
                if (Interface != null)
                    Append(sb, "interface", Interface);
                if (Member != null)
                    Append(sb, "member", Member);
                if (Path != null)
                    Append(sb, "path", Path.Value);
                if (Sender != null)
                    Append(sb, "sender", Sender);
                return sb.ToString();
            }

            protected static void Append(StringBuilder sb, string key, object value)
            {
                Append(sb, key, value.ToString());
            }

            static void Append(StringBuilder sb, string key, string value)
            {
                if (sb.Length != 0)
                    sb.Append(',');
                sb.Append(key);
                sb.Append("='");
                sb.Append(value.Replace(@"\", @"\\").Replace(@"'", @"\'"));
                sb.Append('\'');
            }
        }

        public static readonly ObjectPath DBusObjectPath = new ObjectPath("/org/freedesktop/DBus");
        public const string DBusServiceName = "org.freedesktop.DBus";
        public const string DBusInterface = "org.freedesktop.DBus";

        public static async Task<DBusConnection> ConnectAsync(ClientSetupResult connectionContext, Action<Exception> onDisconnect, CancellationToken cancellationToken, IClientObjectProvider objectProvider)
        {
            var _entries = AddressEntry.ParseEntries(connectionContext.ConnectionAddress);
            if (_entries.Length == 0)
                throw new ArgumentException("No addresses were found", nameof(connectionContext.ConnectionAddress));

            Guid _serverId = Guid.Empty;
            IMessageStream stream = null;
            var index = 0;
            while (index < _entries.Length)
            {
                AddressEntry entry = _entries[index++];

                _serverId = entry.Guid;
                try
                {
                    stream = await Transport.ConnectAsync(entry, connectionContext, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    if (index < _entries.Length)
                        continue;
                    throw;
                }

                break;
            }
            return await CreateAndConnectAsync(stream, onDisconnect, objectProvider);
        }

        public static async Task<DBusConnection> CreateAndConnectAsync(IMessageStream stream, Action<Exception> onDisconnect, IClientObjectProvider objectProvider, bool sayHelloToServer = true)
        {
            var dbusConnection = new DBusConnection(stream, objectProvider);
            objectProvider.DBusConnection = dbusConnection;
            await dbusConnection.ConnectAsync(onDisconnect, default(CancellationToken), sayHelloToServer);
            return dbusConnection;
        }
        public static Task<DBusConnection> CreateAndConnectAsync(IMessageStream stream, Action<Exception> onDisconnect = null, bool sayHelloToServer = true)
        {
            return CreateAndConnectAsync(stream, onDisconnect, new ClientProxyManager(null), sayHelloToServer);
        }

        readonly IMessageStream _stream;
        readonly object _gate = new object();
        readonly Dictionary<SignalMatchRule, SignalHandler> _signalHandlers = new Dictionary<SignalMatchRule, SignalHandler>();
        Dictionary<uint, TaskCompletionSource<Message>> _pendingMethods = new Dictionary<uint, TaskCompletionSource<Message>>();
        MethodHandlerPathPart RootPath = new MethodHandlerPathPart(null, null);
        Dictionary<string, IMethodHandler> GlobalInterfaceHandlers = new Dictionary<string, IMethodHandler>();

        public interface IMethodHandlerPathPart
        {
            IReadOnlyDictionary<string, IMethodHandlerPathPart> SubParts { get; }
            IReadOnlyDictionary<string, IMethodHandler> InterfaceHandlers { get; }
            string PathPart { get; }
            ObjectPath Path { get; }
        }

        class MethodHandlerPathPart : IMethodHandlerPathPart
        {
            public MethodHandlerPathPart Parent { get; private set; }
            public MethodHandlerPathPart(MethodHandlerPathPart parent, string pathPart)
            {
                Parent = parent; PathPart = pathPart;
            }

            public readonly Dictionary<string, IMethodHandler> OurInterfaceHandlers = new Dictionary<string, IMethodHandler>();

            public IReadOnlyDictionary<string, MethodHandlerPathPart> OurSubParts => ourSubParts;

            Dictionary<string, MethodHandlerPathPart> ourSubParts = new Dictionary<string, MethodHandlerPathPart>();
            Dictionary<string, IMethodHandlerPathPart> externalSubParts = new Dictionary<string, IMethodHandlerPathPart>();
            public void AddOurSubPart(string part, MethodHandlerPathPart handler)
            {
                ourSubParts[part] = handler;
                UpdateSubParts();
            }
            public void AddExternalSubPart(string part, IMethodHandlerPathPart handler)
            {
                externalSubParts[part] = handler;
                UpdateSubParts();
            }
            public void RemoveOurSubPart(string part)
            {
                ourSubParts.Remove(part);
                UpdateSubParts();
            }

            void UpdateSubParts()
            {
                var subParts = ourSubParts.ToDictionary(sp => sp.Key, sp => (IMethodHandlerPathPart)sp.Value);
                foreach (var kvp in externalSubParts)
                    subParts[kvp.Key] = kvp.Value;
                SubParts = subParts;
            }

            public IReadOnlyDictionary<string, IMethodHandlerPathPart> SubParts { get; private set; } = new Dictionary<string, IMethodHandlerPathPart>();

            public IReadOnlyDictionary<string, IMethodHandler> InterfaceHandlers => OurInterfaceHandlers;

            public string PathPart { get; private set; }

            IEnumerable<string> PathSubPartsReversed()
            {
                var inst = this;
                while (inst.Parent != null)
                {
                    yield return inst.PathPart;
                    inst = inst.Parent;
                }
            }

            public ObjectPath Path => new ObjectPath(PathSubPartsReversed().Reverse());
        }

        private Task<string> _localName;
        private Action<Exception> _onDisconnect;
        private int _methodSerial;
        private ConcurrentQueue<PendingSend> _sendQueue;
        private SemaphoreSlim _sendSemaphore;
        private IClientObjectProvider _clientProvider;
        private IDBus _dbus;

        public string LocalName => _localName.Result;
        public bool? RemoteIsBus { get; private set; }

        public IDBus DBus => _dbus;

        public IClientObjectProvider ProxyProvider => _clientProvider;

        public bool IsDisposed => _pendingMethods == null;
        void ThrowIfDisposed()
        {
            if (IsDisposed)
                throw new ObjectDisposedException(nameof(DBusConnection));
        }

        Task TaskConnected => _localName;
        public bool IsConnected => (TaskConnected != null) && TaskConnected.IsCompleted;

        void ThrowIfNotConnected()
        {
            ThrowIfDisposed();
            if (!IsConnected)
                throw new InvalidOperationException("Not Connected");
        }

        private DBusConnection(IMessageStream stream, IClientObjectProvider clientProvider)
        {
            _stream = stream;
            _clientProvider = clientProvider;
            _sendQueue = new ConcurrentQueue<PendingSend>();
            _sendSemaphore = new SemaphoreSlim(1);
        }

        public async Task ConnectAsync(Action<Exception> onDisconnect = null, CancellationToken cancellationToken = default(CancellationToken), bool sayHelloToServer = true)
        {
            ThrowIfDisposed();
            lock (_gate)
            {
                if (_dbus != null)
                    throw new InvalidOperationException("Already connected");
                _dbus = _clientProvider.GetInstance<IDBus>(DBusObjectPath, DBusServiceName);
            }
            _onDisconnect = onDisconnect;

            ReceiveMessages(_stream, (_, e) => Dispose(e));

            if (sayHelloToServer)
                _localName = _dbus.Hello();
            else
                _localName = Task.FromResult("");
            RemoteIsBus = !string.IsNullOrEmpty(await _localName);
        }

        void Dispose(Exception disconnectReason)
        {
            Dictionary<uint, TaskCompletionSource<Message>> pendingMethods = null;
            lock (_gate)
            {
                pendingMethods = _pendingMethods;
                if (pendingMethods == null)
                    return;
                _pendingMethods = null;
                _localName = null;
                _stream.Dispose();
                _signalHandlers.Clear();
                _clientProvider.Dispose();
                foreach (var h in GlobalInterfaceHandlers)
                    h.Value.Dispose();
                GlobalInterfaceHandlers.Clear();
                Action<IMethodHandlerPathPart> di = null;
                di = mhpp =>
                {
                    foreach (var mh in mhpp.InterfaceHandlers)
                        mh.Value.Dispose();
                    foreach (var sp in mhpp.SubParts)
                        di(sp.Value);
                };
                di(RootPath);
                RootPath.OurInterfaceHandlers.Clear();
            }
            var e = (disconnectReason != null) ? 
                (Exception)new DisconnectedException(disconnectReason) : 
                new ObjectDisposedException(typeof(Connection).FullName);
            foreach (var tcs in pendingMethods.Values)
                tcs.SetException(e);
            _onDisconnect?.Invoke(disconnectReason);
        }
        public void Dispose()
        {
            Dispose(null);
        }

        public void EmitSignal(Message message)
        {
            ThrowIfNotConnected();
            message.Header.Serial = GenerateSerial();
            SendMessageAsync(message);
        }

        public void AddMethodHandler(ObjectPath? path, string interfaceName, IMethodHandler handler)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(interfaceName))
                throw new ArgumentNullException(nameof(interfaceName));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            lock (_gate)
            {
                if (path.HasValue)
                {
                    var pathParts = path.Value.Decomposed;
                    MethodHandlerPathPart pathHandlers = RootPath;
                    foreach (var part in pathParts)
                    {
                        if (!pathHandlers.OurSubParts.TryGetValue(part, out MethodHandlerPathPart next))
                        {
                            next = new MethodHandlerPathPart(pathHandlers, part);
                            pathHandlers.AddOurSubPart(part, next);
                        }
                        pathHandlers = next;
                    }
                    pathHandlers.OurInterfaceHandlers[interfaceName] = handler;
                }
                else
                    GlobalInterfaceHandlers[interfaceName] = handler;
            }
        }

        public IMethodHandler RemoveMethodHandler(ObjectPath? path, string interfaceName)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(interfaceName))
                throw new ArgumentNullException(nameof(interfaceName));
            lock (_gate)
            {
                if (path.HasValue)
                {
                    var pathParts = path.Value.Decomposed;
                    MethodHandlerPathPart pathHandlers = RootPath;
                    foreach (var part in pathParts)
                    {
                        if (!pathHandlers.OurSubParts.TryGetValue(part, out MethodHandlerPathPart next))
                            return null;
                        pathHandlers = next;
                    }
                    if (!pathHandlers.OurInterfaceHandlers.TryGetValue(interfaceName, out IMethodHandler handler))
                        return null;
                    pathHandlers.OurInterfaceHandlers.Remove(interfaceName);
                    // Remove any now empty path parts
                    while (pathHandlers.Parent != null && !pathHandlers.InterfaceHandlers.Any() && !pathHandlers.SubParts.Any())
                    {
                        pathHandlers.Parent.RemoveOurSubPart(pathHandlers.PathPart);
                        pathHandlers = pathHandlers.Parent;
                    }
                    return handler;
                }
                else
                {
                    if (!GlobalInterfaceHandlers.TryGetValue(interfaceName, out IMethodHandler handler))
                        return null;
                    GlobalInterfaceHandlers.Remove(interfaceName);
                    return handler;
                }
            }
        }

        private async void SendPendingMessages()
        {
            try
            {
                await _sendSemaphore.WaitAsync();
                while (_sendQueue.TryDequeue(out PendingSend pendingSend))
                {
                    try
                    {
                        await _stream.SendMessageAsync(pendingSend.Message);
                        pendingSend.CompletionSource.SetResult(true);
                    }
                    catch (System.Exception e)
                    {
                        pendingSend.CompletionSource.SetException(e);
                    }
                }
            }
            finally
            {
                _sendSemaphore.Release();
            }
        }

        private Task SendMessageAsync(Message message)
        {
            var tcs = new TaskCompletionSource<bool>();
            var pendingSend = new PendingSend()
            {
                Message = message,
                CompletionSource = tcs
            };
            _sendQueue.Enqueue(pendingSend);
            SendPendingMessages();
            return tcs.Task;
        }

        internal async void ReceiveMessages(IMessageStream peer, Action<IMessageStream, Exception> disconnectAction)
        {
            try
            {
                while (true)
                {
                    Message msg = await peer.ReceiveMessageAsync();
                    if (msg == null)
                        throw new IOException("Connection closed by peer");
                    HandleMessage(msg, peer);
                }
            }
            catch (Exception e)
            {
                disconnectAction?.Invoke(peer, e);
            }
        }

        private void HandleMessage(Message msg, IMessageStream peer)
        {
            uint? serial = msg.Header.ReplySerial;
            if (serial != null)
            {
                uint serialValue = (uint)serial;
                TaskCompletionSource<Message> pending = null;
                lock (_gate)
                {
                    if (_pendingMethods?.TryGetValue(serialValue, out pending) == true)
                        _pendingMethods.Remove(serialValue);
                }
                if (pending != null)
                    pending.SetResult(msg);
                else
                {
                    // Drop quietly
                    //throw new ProtocolException("Unexpected reply message received: MessageType = '" + msg.Header.MessageType + "', ReplySerial = " + serialValue);
                }
                return;
            }

            switch (msg.Header.MessageType)
            {
                case MessageType.MethodCall:
                    HandleMethodCall(msg, peer);
                    break;
                case MessageType.Signal:
                    HandleSignal(msg);
                    break;
                case MessageType.Error:
                    string errMsg = String.Empty;
                    if (msg.Header.Signature.Value.Value.StartsWith("s"))
                        errMsg = new MessageReader(msg).ReadString();
                    throw new DBusException(msg.Header.ErrorName, errMsg);
                case MessageType.Invalid:
                default:
                    throw new ProtocolException("Invalid message received: MessageType='" + msg.Header.MessageType + "'");
            }
        }

        public async Task<IDisposable> WatchSignalAsync(SignalHandler handler, string @interface, string signalName, string sender, ObjectPath? path = null)
        {
            ThrowIfNotConnected();
            if (RemoteIsBus.HasValue && RemoteIsBus.Value && sender.Trim()[0] != ':')
                sender = await _dbus.GetNameOwner(sender);

            var rule = new SignalMatchRule(
                @interface: @interface ?? throw new ArgumentNullException(nameof(@interface)),
                member: signalName ?? throw new ArgumentNullException(nameof(signalName)),
                path: path,
                sender: sender);

            Task task = null;
            lock (_gate)
            {
                if (_signalHandlers.TryGetValue(rule, out SignalHandler prevHandler))
                    _signalHandlers[rule] = (SignalHandler)Delegate.Combine(prevHandler, handler);
                else
                {
                    _signalHandlers[rule] = handler;
                    if (RemoteIsBus == true)
                        task = _dbus.AddMatch(rule.ToString());
                }
            }
            var registration = new SignalHandlerRegistration(this, rule, handler);
            try
            {
                if (task != null)
                    await task;
            }
            catch
            {
                registration.Dispose();
                throw;
            }
            return registration;
        }

        private void HandleSignal(Message msg)
        {
            var sender = msg.Header.Sender ?? string.Empty;
            var rule = new SignalMatchRule(@interface: msg.Header.Interface,
                member: msg.Header.Member,
                path: msg.Header.Path.Value,
                sender: sender);
            SignalHandler signalHandler = null;
            lock (_gate)
            {
                if (_signalHandlers.TryGetValue(rule, out SignalHandler handler))
                    signalHandler = handler;
            }
            if (signalHandler != null)
            {
                try
                {
                    signalHandler(msg);
                }
                catch (Exception e)
                {
                    throw new InvalidOperationException("Signal handler for " + msg.Header.Interface + "." + msg.Header.Member + " threw an exception", e);
                }
            }
        }

        public void SendMessage(Message message, IMessageStream peer)
        {
            if (message.Header.Serial == 0)
                message.Header.Serial = GenerateSerial();
            peer.TrySendMessage(message);
        }

        IMethodHandlerPathPart PartHandlerForPath(ObjectPath? path)
        {
            var p = ObjectPath.Root;
            if (path.HasValue)
                p = path.Value;
            IMethodHandlerPathPart pathHandler = RootPath;
            foreach (var part in p.Decomposed)
            {
                pathHandler = pathHandler.SubParts.GetValueOrDefault(part);
                if (pathHandler == null)
                    break;
            }
            return pathHandler;
        }

        async void HandleMethodCall(Message methodCall, IMessageStream peer)
        {
            bool pathKnown = false;
            IMethodHandler methodHandler = null;
            if (methodCall.Header.Path.HasValue && !string.IsNullOrEmpty(methodCall.Header.Interface))
            {
                var pathHandler = PartHandlerForPath(methodCall.Header.Path.Value);
                if (pathHandler != null)
                {
                    pathKnown = true;
                    if (!pathHandler.InterfaceHandlers.TryGetValue(methodCall.Header.Interface, out methodHandler))
                        methodHandler = GlobalInterfaceHandlers.GetValueOrDefault(methodCall.Header.Interface);
                    if (methodHandler != null && !methodHandler.CheckExposure(methodCall.Header.Path.Value))
                        methodHandler = null;
                }
            }
            if (methodHandler != null)
            {
                var reply = await methodHandler.HandleMethodCall(methodCall);
                if (reply != null)
                {
                    reply.Header.ReplySerial = methodCall.Header.Serial;
                    reply.Header.Destination = methodCall.Header.Sender;
                    SendMessage(reply, peer);
                }
                else if (methodCall.Header.ReplyExpected)
                    SendMessage(MessageHelper.ConstructReply(methodCall), peer);
            }
            else if (methodCall.Header.ReplyExpected)
            {
                if (!pathKnown)
                    SendErrorReply(methodCall, DBusErrors.UnknownObject, $"No objects at path {methodCall.Header.Path}", peer);
                else
                    SendErrorReply(methodCall, DBusErrors.UnknownInterface, $"No interface {methodCall.Header.Interface} at path {methodCall.Header.Path}", peer);
            }
        }

        void SendErrorReply(Message incoming, DBusErrors error, string errorMessage, IMessageStream peer)
        {
            SendMessage(MessageHelper.ConstructErrorReply(incoming, error, errorMessage), peer);
        }

        uint GenerateSerial()
        {
            return (uint)Interlocked.Increment(ref _methodSerial);
        }

        public Task<Message> CallMethodAsync(Message msg, bool checkConnected = true)
        {
            return CallMethodAsync(msg, checkConnected, checkReplyType: true);
        }

        public async Task<Message> CallMethodAsync(Message msg, bool checkConnected, bool checkReplyType)
        {
            msg.Header.ReplyExpected = true;
            var serial = GenerateSerial();
            msg.Header.Serial = serial;

            var pending = new TaskCompletionSource<Message>();
            lock (_gate)
            {
                if (checkConnected)
                    ThrowIfNotConnected();
                _pendingMethods[msg.Header.Serial] = pending;
            }

            try
            {
                await SendMessageAsync(msg);
            }
            catch
            {
                lock (_gate)
                {
                    _pendingMethods?.Remove(serial);
                }
                throw;
            }

            var reply = await pending.Task;
            if (checkReplyType)
            {
                switch (reply.Header.MessageType)
                {
                    case MessageType.MethodReturn:
                        return reply;
                    case MessageType.Error:
                        string errorMessage = String.Empty;
                        if (reply.Header.Signature?.Value?.StartsWith("s") == true)
                            errorMessage = new MessageReader(reply).ReadString();
                        throw new DBusException(reply.Header.ErrorName, errorMessage);
                    default:
                        throw new ProtocolException("Got unexpected message of type " + reply.Header.MessageType + " while waiting for a MethodReturn or Error");
                }
            }

            return reply;
        }

        Task RemoveSignalHandler(SignalMatchRule rule, SignalHandler dlg)
        {
            lock (_gate)
            {
                if (_signalHandlers.ContainsKey(rule))
                {
                    var sh = _signalHandlers[rule];
                    _signalHandlers[rule] = (SignalHandler)Delegate.Remove(sh, dlg);
                    if (_signalHandlers[rule] == null)
                    {
                        _signalHandlers.Remove(rule);
                        if (RemoteIsBus == true)
                            return _dbus.RemoveMatch(rule.ToString());
                    }
                }
            }
            return Task.CompletedTask;
        }

        public IEnumerable<string> RegisteredPathChildren(ObjectPath path)
        {
            lock (_gate)
            {
                var pathHandler = PartHandlerForPath(path);
                return (pathHandler != null) ? pathHandler.SubParts.Keys : Enumerable.Empty<string>();
            }
        }
        public IEnumerable<(string InterfaceName, IMethodHandler Handler)> RegisteredPathHandlers(ObjectPath? path, string maskInterface = null)
        {
            lock (_gate)
            {
                if (path.HasValue)
                {
                    var pathHandler = PartHandlerForPath(path);
                    if (pathHandler != null)
                    {
                        var globalHandlers = GlobalInterfaceHandlers.Where(ih => ih.Key != maskInterface && ih.Value.CheckExposure(path.Value)).Select(ih => (ih.Key, ih.Value));
                        var vals = pathHandler.InterfaceHandlers.Where(ih => ih.Key != maskInterface && ih.Value.CheckExposure(path.Value));
                        if (vals.Any())
                            return globalHandlers.Concat(vals.Select(v => (v.Key, v.Value)));
                        else if (path == ObjectPath.Root) // return global handlers at root
                            return globalHandlers;
                    }
                }
                else
                {
                    return GlobalInterfaceHandlers.Where(ih => ih.Key != maskInterface).Select(ih => (ih.Key, ih.Value));
                }
                return Enumerable.Empty<(string InterfaceName, IMethodHandler Handler)>();
            }
        }

    }
}