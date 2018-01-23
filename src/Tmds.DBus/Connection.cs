// Copyright 2016 Tom Deseyn <tom.deseyn@gmail.com>
// This software is made available under the MIT License
// See COPYING for details

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;
using Tmds.DBus.Objects;
using Tmds.DBus.Objects.DBus;

namespace Tmds.DBus
{
    public class Connection : IConnection
    {
        TaskCompletionSource<IDBusConnection> baseConnection;
        TaskCompletionSource<IDBusConnection> newBaseConnection = new TaskCompletionSource<IDBusConnection>();
        public IDBusConnection BaseDBusConnection => baseConnection?.Task.Result;

        public string Address => ConnectionContext.ConnectionAddress;
        public string LocalName => BaseDBusConnection?.LocalName;
        public bool? RemoteIsBus => BaseDBusConnection?.RemoteIsBus;
        public IDBus DBus => BaseDBusConnection?.DBus;

        public ClientSetupResult ConnectionContext { get; private set; }

        public Connection(string address)
            : this(new ClientSetupResult
                {
                    ConnectionAddress = address ?? throw new ArgumentNullException(nameof(address)),
                    SupportsFdPassing = true,
                    UserId = Environment.UserId
                })
        { }

        public Connection(ClientSetupResult connectionContext)
        {
            ConnectionContext = connectionContext ?? throw new ArgumentNullException(nameof(connectionContext));
        }

        public async Task ConnectAsync(Action<Exception> onDisconnect = null, CancellationToken cancellationToken = default(CancellationToken), IClientObjectProvider objProvider = default(IClientObjectProvider))
        {
            if (Interlocked.CompareExchange(ref baseConnection, newBaseConnection, null) != null)
                throw new InvalidOperationException("Can only connect once");
            newBaseConnection = null;
            FinishConnect(await DBusConnection.ConnectAsync(ConnectionContext, onDisconnect, cancellationToken, new ClientProxyManager(this)));
        }

        public void Connect(IDBusConnection connection)
        {
            if (Interlocked.CompareExchange(ref baseConnection, newBaseConnection, null) != null)
                throw new InvalidOperationException("Can only connect once");
            newBaseConnection = null;
            FinishConnect(connection);
        }

        void FinishConnect(IDBusConnection connection)
        {
            baseConnection.SetResult(connection);
            try
            {
                IConnection_Extensions.RegisterObject(this, properties ?? (properties = new Properties(this)), null);
                IConnection_Extensions.RegisterObject(this, (introspectable = new Introspectable(this)), null);
                IConnection_Extensions.RegisterObject(this, peer ?? (peer = new Peer()), null);
            }
            catch (Exception)
            {
                BaseDBusConnection.Dispose();
                throw;
            }
        }

        #region Wellknown interfaces
        Introspectable introspectable;
        Peer peer;
        Properties properties;

        class Peer : IPeer
        {
            public Task<string> GetMachineId() { return Task.FromResult(Environment.MachineId); }
            public Task Ping() { return Task.CompletedTask; }
        }

        class Introspectable : ObjectContext, IIntrospectable
        {
            public Introspectable(Connection con) { Connection = con; }
            public readonly Connection Connection;

            public Task<string> Introspect()
            {
                var path = CallContext.CurrentPath ?? ObjectPath.Root;
                var writer = new IntrospectionWriter();
                writer.WriteDocType();
                writer.WriteNodeStart(path.Value);
                var interfaces = Connection.BaseDBusConnection.RegisteredPathHandlers(path);
                foreach (var @interface in interfaces)
                {
                    writer.WriteInterfaceStart(@interface.InterfaceName);
                    var def = @interface.Handler.GetInterfaceDefinitions();
                    foreach (var m in def.Methods)
                    {
                        writer.WriteMethodStart(m.Name);
                        foreach (var arg in m.ArgTypes)
                            writer.WriteInArg(arg.Name, arg.Signature);
                        foreach (var arg in m.ReturnTypes)
                            writer.WriteOutArg(arg.Name, arg.Signature);
                        writer.WriteMethodEnd();
                    }
                    foreach (var p in def.Properties)
                    {
                        writer.WriteProperty(p.Name, p.Type.Signature, p.Access);
                    }
                    foreach (var s in def.Signals)
                    {
                        writer.WriteSignalStart(s.Name);
                        foreach (var arg in s.ArgDefs)
                            writer.WriteArg(arg.Name, arg.Signature);
                        writer.WriteSignalEnd();
                    }
                    writer.WriteInterfaceEnd();
                }
                var children = Connection.BaseDBusConnection.RegisteredPathChildren(path);
                if (children.Any())
                {
                    foreach (var child in children)
                        writer.WriteChildNode(child);
                }
                writer.WriteNodeEnd();
                return Task.FromResult(writer.ToString());
            }
        }

        class Properties : ObjectContext, IProperties
        {
            public Properties(Connection connection) { Connection = connection; }
            public readonly Connection Connection;

            public override bool CheckExposure()
            {
                foreach (var handler in Connection.BaseDBusConnection.RegisteredPathHandlers(CallContext.CurrentPath, "org.freedesktop.DBus.Properties"))
                {
                    if (handler.Handler is ObjectAdapter adapter && adapter.Properties.Any())
                        return true;
                }
                return false;
            }

            public Task<object> Get(string interface_name, string property_name)
            {
                foreach (var handler in Connection.BaseDBusConnection.RegisteredPathHandlers(CallContext.CurrentPath))
                {
                    if (handler.InterfaceName == interface_name)
                    {
                        if (handler.Handler is ObjectAdapter adapter)
                        {
                            if (adapter.Properties.TryGetValue(property_name, out ObjectAdapter.PropertyDetails prop))
                            {
                                if (prop.Getter == null)
                                    throw new DBusException(DBusErrors.InvalidArgs, "Property not readable");
                                return Task.FromResult(prop.Getter(adapter.Instance));
                            }
                            else
                                throw new DBusException(DBusErrors.UnknownProperty, string.Empty);
                        }
                        else
                        {
                            // No way to currently pass property requests to 3rd party handlers. Pretty unlikely anyway
                        }
                    }
                }
                throw new DBusException(DBusErrors.UnknownInterface, "No properties found for interface");
            }

            public Task Set(string interface_name, string property_name, object value)
            {
                foreach (var handler in Connection.BaseDBusConnection.RegisteredPathHandlers(CallContext.CurrentPath))
                {
                    if (handler.InterfaceName == interface_name)
                    {
                        if (handler.Handler is ObjectAdapter adapter)
                        {
                            if (adapter.Properties.TryGetValue(property_name, out ObjectAdapter.PropertyDetails prop))
                            {
                                if (prop.Setter == null)
                                    throw new DBusException(DBusErrors.PropertyReadOnly, string.Empty);
                                prop.Setter(adapter.Instance, value);
                                return Task.CompletedTask;
                            }
                            else
                                throw new DBusException(DBusErrors.UnknownProperty, string.Empty);
                        }
                        else
                        {
                            // No way to currently pass property requests to 3rd party handlers. Pretty unlikely anyway
                        }
                    }
                }
                throw new DBusException(DBusErrors.UnknownInterface, "No properties found for interface");
            }

            public Task<Dictionary<string, object>> GetAll(string interface_name)
            {
                foreach (var handler in Connection.BaseDBusConnection.RegisteredPathHandlers(CallContext.CurrentPath))
                {
                    if (handler.InterfaceName == interface_name)
                    {
                        if (handler.Handler is ObjectAdapter adapter)
                        {
                            return Task.FromResult(adapter.Properties.Where(p => p.Value.Getter != null)
                                .ToDictionary(p => p.Key, p => p.Value.Getter(adapter.Instance)));
                        }
                        else
                        {
                            // No way to currently pass property requests to 3rd party handlers. Pretty unlikely anyway
                        }
                    }
                }
                throw new DBusException(DBusErrors.UnknownInterface, "No properties found for interface");
            }

            public Task<IDisposable> WatchPropertiesChanged(PropertiesChangedHandler callback)
            {
                PropertyChangedCallback = callback;
                return null;
            }

            public void RaisePropertyChanged(string interfaceName, string propertyName, object newValue, ObjectPath path)
            {
                PropertyChangedCallback?.Invoke(interfaceName, new Dictionary<string, object> { { propertyName, newValue } }, new string[0], path);
            }

            PropertiesChangedHandler PropertyChangedCallback;
        }
        #endregion

        public void Dispose()
        {
            BaseDBusConnection?.Dispose();
        }

        void CheckDisposed()
        {
            if (BaseDBusConnection != null && BaseDBusConnection.IsDisposed)
                throw new ObjectDisposedException("this");
        }

#if false
        public async Task<bool> UnregisterServiceAsync(string serviceName)
        {
            var reply = await _dbusConnection.ReleaseNameAsync(serviceName);
            return reply == ReleaseNameReply.ReplyReleased;
        }

        public async Task QueueServiceRegistrationAsync(string serviceName, Action onAquired = null, Action onLost = null, ServiceRegistrationOptions options = ServiceRegistrationOptions.Default)
        {
            if (!options.HasFlag(ServiceRegistrationOptions.AllowReplacement) && (onLost != null))
            {
                throw new ArgumentException($"{nameof(onLost)} can only be set when {nameof(ServiceRegistrationOptions.AllowReplacement)} is also set", nameof(onLost));
            }

            RequestNameOptions requestOptions = RequestNameOptions.None;
            if (options.HasFlag(ServiceRegistrationOptions.ReplaceExisting))
            {
                requestOptions |= RequestNameOptions.ReplaceExisting;
            }
            if (options.HasFlag(ServiceRegistrationOptions.AllowReplacement))
            {
                requestOptions |= RequestNameOptions.AllowReplacement;
            }
            var reply = await _dbusConnection.RequestNameAsync(serviceName, requestOptions, onAquired, onLost, SynchronizationContext.Current);
            switch (reply)
            {
                case RequestNameReply.PrimaryOwner:
                case RequestNameReply.InQueue:
                    return;
                case RequestNameReply.Exists:
                case RequestNameReply.AlreadyOwner:
                default:
                    throw new ProtocolException("Unexpected reply");
            }
        }

        public async Task RegisterServiceAsync(string name, Action onLost = null, ServiceRegistrationOptions options = ServiceRegistrationOptions.Default)
        {
            if (!options.HasFlag(ServiceRegistrationOptions.AllowReplacement) && (onLost != null))
            {
                throw new ArgumentException($"{nameof(onLost)} can only be set when {nameof(ServiceRegistrationOptions.AllowReplacement)} is also set", nameof(onLost));
            }

            RequestNameOptions requestOptions = RequestNameOptions.DoNotQueue;
            if (options.HasFlag(ServiceRegistrationOptions.ReplaceExisting))
            {
                requestOptions |= RequestNameOptions.ReplaceExisting;
            }
            if (options.HasFlag(ServiceRegistrationOptions.AllowReplacement))
            {
                requestOptions |= RequestNameOptions.AllowReplacement;
            }
            var reply = await _dbusConnection.RequestNameAsync(name, requestOptions, null, onLost, SynchronizationContext.Current);
            switch (reply)
            {
                case RequestNameReply.PrimaryOwner:
                    return;
                case RequestNameReply.Exists:
                    throw new InvalidOperationException("Service is registered by another connection");
                case RequestNameReply.AlreadyOwner:
                    throw new InvalidOperationException("Service is already registered by this connection");
                case RequestNameReply.InQueue:
                default:
                    throw new ProtocolException("Unexpected reply");
            }
        }

        public Task<string[]> ListActivatableServicesAsync()
        {
            ThrowIfNotConnected();
            ThrowIfRemoteIsNotBus();
            return DBus.ListActivatableNamesAsync();
        }

        public async Task<string> ResolveServiceOwnerAsync(string serviceName)
        {
            ThrowIfNotConnected();
            ThrowIfRemoteIsNotBus();
            try
            {
                return await DBus.GetNameOwnerAsync(serviceName);
            }
            catch (DBusException e) when (e.ErrorName == "org.freedesktop.DBus.Error.NameHasNoOwner")
            {
                return null;
            }
            catch
            {
                throw;
            }
        }

        public Task<ServiceStartResult> ActivateServiceAsync(string serviceName)
        {
            ThrowIfNotConnected();
            ThrowIfRemoteIsNotBus();
            return DBus.StartServiceByNameAsync(serviceName, 0);
        }

        public Task<bool> IsServiceActiveAsync(string serviceName)
        {
            ThrowIfNotConnected();
            ThrowIfRemoteIsNotBus();
            return DBus.NameHasOwnerAsync(serviceName);
        }

        public async Task<IDisposable> ResolveServiceOwnerAsync(string serviceName, Action<ServiceOwnerChangedEventArgs> handler)
        {
            ThrowIfNotConnected();
            ThrowIfRemoteIsNotBus();
            if (serviceName == "*")
            {
                serviceName = ".*";
            }

            var synchronizationContext = SynchronizationContext.Current;
            bool eventEmitted = false;
            var wrappedDisposable = new WrappedDisposable();
            var namespaceLookup = serviceName.EndsWith(".*");
            var emittedServices = namespaceLookup ? new List<string>() : null;

            wrappedDisposable.Disposable = await _dbusConnection.WatchNameOwnerChangedAsync(serviceName,
                e => {
                    bool first = false;
                    if (namespaceLookup)
                    {
                        first = emittedServices?.Contains(e.ServiceName) == false;
                        emittedServices?.Add(e.ServiceName);
                    }
                    else
                    {
                        first = eventEmitted == false;
                        eventEmitted = true;
                    }
                    if (first)
                    {
                        if (e.NewOwner == null)
                        {
                            return;
                        }
                        e.OldOwner = null;
                    }
                    if (synchronizationContext != null)
                    {
                        synchronizationContext.Post(o =>
                        {
                            if (!wrappedDisposable.IsDisposed)
                            {
                                handler(e);
                            }
                        }, null);
                    }
                    else
                    {
                        if (!wrappedDisposable.IsDisposed)
                        {
                            handler(e);
                        }
                    }
                });
            if (namespaceLookup)
            {
                serviceName = serviceName.Substring(0, serviceName.Length - 2);
            }
            try
            {
                if (namespaceLookup)
                {
                    var services = await ListServicesAsync();
                    foreach (var service in services)
                    {
                        if (service.StartsWith(serviceName)
                         && (   (service.Length == serviceName.Length)
                             || (service[serviceName.Length] == '.')
                             || (serviceName.Length == 0 && service[0] != ':')))
                        {
                            var currentName = await ResolveServiceOwnerAsync(service);
                            if (currentName != null && !emittedServices.Contains(serviceName))
                            {
                                emittedServices.Add(service);
                                var e = new ServiceOwnerChangedEventArgs(service, null, currentName);
                                handler(e);
                            }
                        }
                    }
                    emittedServices = null;
                }
                else
                {
                    var currentName = await ResolveServiceOwnerAsync(serviceName);
                    if (currentName != null && !eventEmitted)
                    {
                        eventEmitted = true;
                        var e = new ServiceOwnerChangedEventArgs(serviceName, null, currentName);
                        handler(e);
                    }
                }
                return wrappedDisposable;
            }
            catch
            {
                wrappedDisposable.Dispose();
                throw;
            }
        }

        public Task<string[]> ListServicesAsync()
        {
            ThrowIfNotConnected();
            ThrowIfRemoteIsNotBus();

            return DBus.ListNamesAsync();
        }
#endif

        /// <summary>
        /// Handle proxy call exceptions
        /// </summary>
        /// <param name="ex"></param>
        /// <returns>If exception was handled and no reply should be sent</returns>
        bool ProxyExceptionHandler(Exception ex)
        {
            return false;
        }

        public void RegisterObject(ObjectPath? path, string interfaceName, object instance, MemberExposure exposure)
        {
            CheckDisposed();
            var pathNonGlobal = path ?? ObjectPath.Root;
            var adapter = new ObjectAdapter(this, path, interfaceName, instance, exposure, ProxyExceptionHandler);
            BaseDBusConnection.AddMethodHandler(path, interfaceName, adapter);

            if (path.HasValue && adapter.Properties.Any())
                adapter.PropertyChanged += (ifceName, propertyName, newValue) => properties.RaisePropertyChanged(ifceName, propertyName, newValue, path.Value);
        }

        public void UnregisterObject(ObjectPath? path, string interfaceName)
        {
            CheckDisposed();
            var handler = BaseDBusConnection.RemoveMethodHandler(path, interfaceName);
            if (handler is ObjectAdapter adapter)
            {
                adapter.Dispose();
            }
        }
    }
}
