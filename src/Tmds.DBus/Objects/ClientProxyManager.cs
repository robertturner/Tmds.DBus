using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using Tmds.DBus.Protocol;
using System.Threading;
using Tmds.DBus.Objects.Internal;
using BaseLibs.Tasks;
using BaseLibs.Types;
using BaseLibs.Collections;
using Tmds.DBus.Objects.DBus;
using BaseLibs.Tuples;

namespace Tmds.DBus.Objects
{
    public sealed class ClientProxyManager : IClientObjectProvider
    {
        public ClientProxyManager(IConnection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        IConnection _connection;
        public IConnection Connection
        {
            get
            {
                ThrowIfDisposed();
                return _connection;
            }
        }

        public IDBusConnection DBusConnection { get; set; }

        static readonly ProxyGenerator generator = new ProxyGenerator();

        class DBusObjectBase : IDBusObjectProxy
        {
            public DBusObjectBase(ClientProxyManager parent, Type type, ObjectPath path, string interfaceName, string service)
            {
                Parent = parent;
                Type = type;
                TypeDescriptor = TypeDescription.GetOrCreateCached(type, MemberExposure.AllInterfaces);
                ObjectPath = path;
                InterfaceName = interfaceName;
                Service = service;
                castleInterceptor = new Interceptor(this);
                var componentIType = typeof(IDBusObjectProxy<>).MakeGenericType(type);
                //ProxyInstance = (IDBusObjectProxy)generator.CreateInterfaceProxyWithTarget(componentIType, new[] { type }, this, interceptor);
                ProxyInstance = generator.CreateInterfaceProxyWithTargetInterface(componentIType, new[] { type }, this, castleInterceptor);

                interceptorHandler = new ProxyInterceptor(this, TypeDescriptor)
                {
                    DisposeHook = Dispose
                };
            }

            public readonly ClientProxyManager Parent;

            public TypeDescription TypeDescriptor { get; }

            public ObjectPath ObjectPath { get; }

            public string InterfaceName { get; }

            public string Service { get;  }

            public Type Type { get;  }

            public event Action<DBusException> ExceptionHook
            {
                add => interceptorHandler.ExceptionHook += value;
                remove => interceptorHandler.ExceptionHook -= value;
            }

            public event Action<bool> ServiceUpHook
            {
                add => interceptorHandler.ServiceUpHook += value;
                remove => interceptorHandler.ServiceUpHook -= value;
            }

            public IConnection Connection => Parent.Connection;

            public object ProxyInstance { get; }

            Interceptor castleInterceptor;
            ProxyInterceptor interceptorHandler;

            sealed class Interceptor : IInterceptor
            {
                public Interceptor(DBusObjectBase parent)
                {
                    parent_ = parent;
                }
                DBusObjectBase parent_;

                public void Intercept(Castle.DynamicProxy.IInvocation invocation)
                {
                    if (invocation.Method.Name == nameof(IDisposable.Dispose))
                    {
                        Dispose();
                        return;
                    }
                    var parent = parent_;
                    if (invocation.InvocationTarget == parent)
                        invocation.Proceed();
                    else
                        invocation.ReturnValue = parent.interceptorHandler.Intercept(parent.Parent, invocation.Method, invocation.Arguments);
                }

                public void Dispose()
                {
                    parent_ = null;
                }
            }

            public void Dispose()
            {
                castleInterceptor?.Dispose();
                castleInterceptor = null;
                interceptorHandler?.Dispose();
                interceptorHandler = null;
            }
        }

        class DBusObjectBase<T> : DBusObjectBase, IDBusObjectProxy<T>
        {
            public DBusObjectBase(ClientProxyManager parent, Type type, ObjectPath path, string interfaceName, string service)
                : base(parent, type, path, interfaceName, service)
            { }

            T IDBusObjectProxy<T>.ProxyInstance => (T)ProxyInstance;
        }

        static System.Collections.Concurrent.ConcurrentDictionary<Type, ConstructorInvoker<DBusObjectBase>> typeConstructors = new System.Collections.Concurrent.ConcurrentDictionary<Type, ConstructorInvoker<DBusObjectBase>>();

        public T GetInstance<T>(ObjectPath path, string interfaceName, string serviceName, out IDBusObjectProxy<T> container)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(interfaceName))
                throw new ArgumentNullException(nameof(interfaceName));
            if (serviceName == null)
                throw new ArgumentNullException(nameof(serviceName));

            var type = typeof(T);
            if (!type.IsInterface)
                throw new ArgumentException("type must be interface");

            var c = typeConstructors.GetOrAdd(type, t =>
                typeof(DBusObjectBase<>).MakeGenericType(t).GetConstructors()[0].DelegateForConstructor<DBusObjectBase>());
            var inst = c(this, type, path, interfaceName, serviceName);
            container = (IDBusObjectProxy<T>)inst;
            return (T)container.ProxyInstance;
        }

        void ThrowIfDisposed()
        {
            if (IsDisposed)
                throw new ObjectDisposedException(typeof(ClientProxyManager).Name);
        }

        public bool IsDisposed => _connection == null;

        public void Dispose()
        {
            var c = Interlocked.Exchange(ref _connection, null);
            if (c != null)
            { }
        }

        public ProviderPreferences TypePreference<T>() => ProviderPreferences.Unpreffered;
    }
}
