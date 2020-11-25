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
using BaseLibs.Tasks;

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
            FinishConnect(await DBusConnection.ConnectAsync(ConnectionContext, onDisconnect, cancellationToken, new IClientObjectProvider[] { new StaticProxyManager(this), new ClientProxyManager(this) }));
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
                this.RegisterObject(properties ?? (properties = new Properties(this)), null);
                this.RegisterObject((introspectable = new Introspectable(this)), null);
                this.RegisterObject(peer ?? (peer = new Peer()), null);
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
                foreach (var (InterfaceName, Handler) in interfaces)
                {
                    writer.WriteInterfaceStart(InterfaceName);
                    var def = Handler.GetInterfaceDefinitions();
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
                foreach (var child in children)
                    writer.WriteChildNode(child);
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
                foreach (var (InterfaceName, Handler) in Connection.BaseDBusConnection.RegisteredPathHandlers(CallContext.CurrentPath, "org.freedesktop.DBus.Properties"))
                {
                    if (Handler is ObjectAdapter adapter && adapter.Properties.Any())
                        return true;
                }
                return false;
            }

            public Task<object> Get(string interface_name, string property_name)
            {
                foreach (var (InterfaceName, Handler) in Connection.BaseDBusConnection.RegisteredPathHandlers(CallContext.CurrentPath))
                {
                    if (InterfaceName == interface_name)
                    {
                        if (Handler is ObjectAdapter adapter)
                        {
                            if (adapter.Properties.TryGetValue(property_name, out ObjectAdapter.PropertyDetails prop))
                            {
                                if (prop.Getter == null)
                                    throw new DBusException(DBusErrors.InvalidArgs, "Property not readable");
                                var val = prop.Getter(adapter.Instance);
                                if (prop.Property.IsAsync)
                                    return (((Task)val).CastResultAs<object>());
                                else
                                    return Task.FromResult(val);
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
                foreach (var (InterfaceName, Handler) in Connection.BaseDBusConnection.RegisteredPathHandlers(CallContext.CurrentPath))
                {
                    if (InterfaceName == interface_name)
                    {
                        if (Handler is ObjectAdapter adapter)
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
                foreach (var (InterfaceName, Handler) in Connection.BaseDBusConnection.RegisteredPathHandlers(CallContext.CurrentPath))
                {
                    if (InterfaceName == interface_name)
                    {
                        if (Handler is ObjectAdapter adapter)
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
