using BaseLibs.Types;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tmds.DBus.Objects
{
    public sealed class StaticProxyManager : IClientObjectProvider
    {
        public StaticProxyManager(IConnection connection)
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

        class InterceptorCollection : List<IDynamicInterceptor>, IDynamicInterceptorCollection
        {
            public InterceptorCollection(IEnumerable<IDynamicInterceptor> interceptors)
                : base(interceptors)
            { }
        }

        public IDBusConnection DBusConnection { get; set; }

        class ProxyContainer : IDBusObjectProxy
        {
            public ProxyContainer(StaticProxyManager parent, Type type, ObjectPath path, string interfaceName, string service)
            {
                Parent = parent;
                Type = type;
                TypeDescriptor = TypeDescription.GetOrCreateCached(type, MemberExposure.AllInterfaces);
                ObjectPath = path;
                InterfaceName = interfaceName;
                Service = service;

                FodyInterceptor = new Interceptor(this);

                interceptorHandler = new ProxyInterceptor(this, TypeDescriptor)
                {
                    DisposeHook = Dispose
                };
            }

            public readonly StaticProxyManager Parent;

            public TypeDescription TypeDescriptor { get; }

            public ObjectPath ObjectPath { get; }

            public string InterfaceName { get; }

            public string Service { get; }

            public Type Type { get; }

            public IConnection Connection => Parent.Connection;

            public object ProxyInstance { get; set; }

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

            public void Dispose()
            {
                FodyInterceptor?.Dispose();
                FodyInterceptor = null;
                interceptorHandler?.Dispose();
                interceptorHandler = null;
            }

            public Interceptor FodyInterceptor { get; private set; }
            ProxyInterceptor interceptorHandler;

            public sealed class Interceptor : IDynamicInterceptor
            {
                public Interceptor(ProxyContainer parent)
                {
                    parent_ = parent;
                }
                volatile ProxyContainer parent_;
                public void Intercept(IInvocation invocation)
                {
                    if (invocation.Method.Name == nameof(IDisposable.Dispose))
                    {
                        Dispose();
                        return;
                    }
                    var parent = parent_;
                    if (parent == null)
                        throw new ObjectDisposedException("parent");
                    invocation.ReturnValue = parent.interceptorHandler.Intercept(parent.Parent, invocation.Method, invocation.Arguments);
                }

                public void Dispose()
                {
                    parent_ = null;
                }
            }
        }

        class ProxyContainer<T> : ProxyContainer, IDBusObjectProxy<T>
        {
            public ProxyContainer(StaticProxyManager parent, Type type, ObjectPath path, string interfaceName, string service)
                : base(parent, type, path, interfaceName, service)
            { }

            T IDBusObjectProxy<T>.ProxyInstance => (T)ProxyInstance;
        }

        public T GetInstance<T>(ObjectPath path, string interfaceName, string serviceName, out IDBusObjectProxy<T> container)
        {
            if (string.IsNullOrEmpty(interfaceName))
                throw new ArgumentNullException(nameof(interfaceName));
            if (serviceName == null)
                throw new ArgumentNullException(nameof(serviceName));

            var type = typeof(T);
            if (!type.IsInterface)
                throw new ArgumentException("type must be interface");

            var (proxyType, proxyCtor, containerCtor) = GetKnownType(type);
            if (proxyType == null)
                throw new ArgumentException($"No static instance for {type}. Either use ClientProxyManager or are you missing StaticProxyAttribute");

            var interceptor = containerCtor(this, type, path, interfaceName, serviceName);
            var interceptorManager = new DynamicInterceptorManager(new InterceptorCollection(new[] { interceptor.FodyInterceptor }));

            interceptor.ProxyInstance = proxyCtor(interceptorManager);
            container = (IDBusObjectProxy<T>)interceptor;
            return (T)interceptor.ProxyInstance;
        }

        void ThrowIfDisposed()
        {
            if (IsDisposed)
                throw new ObjectDisposedException(typeof(StaticProxyManager).Name);
        }

        public bool IsDisposed => _connection == null;

        public void Dispose()
        {
            var c = Interlocked.Exchange(ref _connection, null);
            if (c != null)
            { }
        }

        static (Type type, ConstructorInvoker proxyConstructor, ConstructorInvoker<ProxyContainer> containerConstructor) GetKnownType(Type type)
        {
            return knownTypes.GetOrAdd(type, t =>
            {
                Type proxyType = null;
                try
                {
                    proxyType = StaticProxy.Interceptor.InterfaceProxy.InterfaceProxyHelpers.GetImplementationTypeOfInterface(t);
                }
                catch (InvalidOperationException)
                {
                    return (null, null, null);
                }
                var containerConstructor = typeof(ProxyContainer<>).MakeGenericType(t).GetConstructors()[0].DelegateForConstructor<ProxyContainer>();
                return (proxyType, proxyType.GetConstructors()[0].DelegateForConstructor(), containerConstructor);
            });
        }

        static System.Collections.Concurrent.ConcurrentDictionary<Type, (Type type, ConstructorInvoker constructor, ConstructorInvoker<ProxyContainer> containerConstructor)> knownTypes = new System.Collections.Concurrent.ConcurrentDictionary<Type, (Type type, ConstructorInvoker constructor, ConstructorInvoker<ProxyContainer> containerConstructor)>();
        public ProviderPreferences TypePreference<T>()
        {
            return (GetKnownType(typeof(T)).type != null) ? ProviderPreferences.Preferred : ProviderPreferences.UnableToGet;
        }
    }
}
