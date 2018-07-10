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

        static ProxyGenerator generator = new ProxyGenerator();

        class DBusObjectBase : IDBusObjectProxy
        {
            public DBusObjectBase(ClientProxyManager parent, Type type, ObjectPath path, string interfaceName, string service)
            {
                Parent = parent;
                Type = type;
                TypeDescriptor = TypeDescription.GetDescriptor(Type, MemberExposure.AllInterfaces);
                ObjectPath = path;
                InterfaceName = interfaceName;
                Service = service;
                interceptor = new Interceptor(this);
                var componentIType = typeof(IDBusObjectProxy<>).MakeGenericType(type);
                //ProxyInstance = (IDBusObjectProxy)generator.CreateInterfaceProxyWithTarget(componentIType, new[] { type }, this, interceptor);
                ProxyInstance = (IDBusObjectProxy)generator.CreateInterfaceProxyWithTargetInterface(componentIType, new[] { type }, this, interceptor);
            }

            public readonly ClientProxyManager Parent;

            public ObjectPath ObjectPath { get; private set; }

            public string InterfaceName { get; private set; }

            public string Service { get; private set; }

            public Type Type { get; private set; }

            public TypeDescription TypeDescriptor { get; private set; }

            public IConnection Connection => Parent.Connection;

            public IDBusObjectProxy ProxyInstance { get; private set; }

            Interceptor interceptor;

            public sealed class Interceptor : IInterceptor, IDisposable
            {
                public Interceptor(DBusObjectBase parent)
                {
                    Parent = parent;
                    if (parent.TypeDescriptor.PropertiesForNames.Count > 0)
                        propertyGetter = parent.Parent.DBusConnection.ProxyProvider.GetInstance<IProperties>(parent.ObjectPath, parent.Service);
                    signals = parent.TypeDescriptor.Signals.ToDictionary(s => s.Name, s => new SignalEntry());
                }
                public DBusObjectBase Parent { get; private set; }
                object @lock = new object();
                public void Dispose()
                {
                    lock (@lock)
                    {
                        Parent = null;
                    }
                }

                IProperties propertyGetter;

                Dictionary<string, SignalEntry> signals = new Dictionary<string, SignalEntry>();
                class SignalEntry
                {
                    public ConstructorInvoker TupleCreator;
                    public Action<object[], ProxyContext> Callbacks;
                    public Action Disposer;
                }

                class SigDisposable : IDisposable
                {
                    object @lock = new object();
                    Action onDispose;
                    public void Dispose()
                    {
                        Action onDispose = null;
                        lock (@lock)
                        {
                            if (IsDisposed)
                                return;
                            IsDisposed = true;
                            onDispose = this.onDispose;
                        }
                        onDispose?.Invoke();
                    }
                    public void Set(Action onDispose)
                    {
                        lock (@lock)
                        {
                            this.onDispose = onDispose;
                        }
                    }
                    public bool IsDisposed { get; private set; }
                }

                private bool SenderMatches(Message message)
                {
                    return string.IsNullOrEmpty(message.Header.Sender) ||
                         string.IsNullOrEmpty(Parent.Service) ||
                         (Parent.Service[0] != ':' && message.Header.Sender[0] == ':') ||
                         Parent.Service == message.Header.Sender;
                }

                public void Intercept(IInvocation invocation)
                {
                    CheckDisposed();
                    if (invocation.InvocationTarget == Parent)
                        invocation.Proceed();
                    else
                    {
                        if (invocation.Method.IsSpecialName)
                        {
                            var methodName = invocation.Method.Name;
                            bool isGet = methodName.StartsWith("get_");
                            if (isGet || methodName.StartsWith("set_"))
                            {
                                var propName = methodName.Substring(4);
                                if (!Parent.TypeDescriptor.PropertiesForNames.TryGetValue(propName, out TypeDescription.PropertyDef propEntry))
                                    throw new Exception($"Property \"{propName}\" not found. Internal bug!");
                                if (isGet)
                                {
                                    var getTask = propertyGetter.Get(Parent.InterfaceName, propName);
                                    getTask.Wait();
                                    invocation.ReturnValue = getTask.Result;
                                }
                                else // set
                                {
                                    var setTask = propertyGetter.Set(Parent.InterfaceName, propName, invocation.Arguments[0]);
                                    setTask.Wait();
                                }
                            }
                            else
                                throw new ArgumentException("Unknown/unexpected special method: " + invocation.Method.Name);
                        }
                        else
                        {
                            if (Parent.TypeDescriptor.MethodsForInfos.TryGetValue(invocation.Method, out TypeDescription.MethodDef m))
                            {
                                try
                                {
                                    TaskCompletionSourceGeneric ret = null;
                                    if (m.IsAsync || m.HasReturnValue)
                                    {
                                        ret = new TaskCompletionSourceGeneric(m.HasReturnValue ? m.ReturnType : typeof(bool));
                                        invocation.ReturnValue = ret.Task;
                                    }
                                    else
                                        invocation.ReturnValue = null;
                                    var reqMsg = new Message(new Header(MessageType.MethodCall)
                                    {
                                        Path = Parent.ObjectPath,
                                        Interface = Parent.InterfaceName,
                                        Member = m.Name,
                                        Destination = Parent.Service,
                                    });
                                    reqMsg.WriteObjs(invocation.Arguments, m.ArgTypes);
                                    Parent.Parent.DBusConnection.CallMethodAsync(reqMsg, checkConnected: false)
                                        .ContinueWith(reply =>
                                        {
                                            if (reply.IsFaulted)
                                                ret.SetException((reply.Exception.InnerExceptions.Count == 1) ? reply.Exception.InnerException : reply.Exception);
                                            else
                                            {
                                                var replyMsg = reply.Result;

                                                // Check returned signature
                                                bool returnSigGood = false;
                                                if (m.ReturnSignature == Signature.Empty)
                                                    returnSigGood = !replyMsg.Header.Signature.HasValue || (replyMsg.Header.Signature.Value == Signature.Empty);
                                                else
                                                    returnSigGood = replyMsg.Header.Signature.HasValue && (replyMsg.Header.Signature.Value == m.ReturnSignature);
                                                if (!returnSigGood)
                                                    ret.SetException(new ReplyArgumentsDifferentFromExpectedException(m.MethodInfo, m.ReturnSignature, replyMsg));
                                                else
                                                {
                                                    if (m.ReturnSignature == Signature.Empty)
                                                        ret.SetResult(true);
                                                    else
                                                    {
                                                        var objs = replyMsg.GetObjs(new[] { m.ReturnType }).ToArray();
                                                        if (objs.Length == 0)
                                                            ret.SetResult(null);
                                                        else if (objs.Length == 1)
                                                            ret.SetResult(objs[0]);
                                                        else
                                                            ret.SetResult(objs);
                                                    }
                                                }
                                            }
                                        });
                                    if (!m.IsAsync && m.HasReturnValue)
                                    {
                                        ret.Task.Wait();
                                        invocation.ReturnValue = ret.Task.TryGetAsGenericTask().Result;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    if (m.IsAsync)
                                        invocation.ReturnValue = m.HasReturnValue ? ex.AsGenericTaskException(m.ReturnType) : Task.FromException(ex);
                                    else
                                        throw;
                                }
                            }
                            else
                            {
                                string sigName;
                                if (invocation.Method.Name.StartsWith("Watch") && ((sigName = invocation.Method.Name.Substring(5)).Length > 0) &&
                                    Parent.TypeDescriptor.SignalsForNames.TryGetValue(sigName, out TypeDescription.SignalDef s))
                                {
                                    var sigCont = signals[sigName];
                                    var ret = new SigDisposable();

                                    var delArg = (Delegate)invocation.Arguments[0];
                                    var target = delArg.Target;
                                    var cb = delArg.Method.DelegateForMethod();

                                    if (s.ArgsAsTuple && sigCont.TupleCreator == null)
                                        sigCont.TupleCreator = s.ArgTypes.AsValueTupleCreator();

                                    Action<object[], ProxyContext> cbUnsync = (objs, ctx) =>
                                    {
                                        if (ret.IsDisposed)
                                            return;
                                        ObjectContext objCtx = target as ObjectContext;
                                        objCtx?.SetContext(ctx);
                                        try
                                        {
                                            if (sigCont.TupleCreator != null)
                                            {
                                                var tupleObj = sigCont.TupleCreator(objs);
                                                cb(target, tupleObj);
                                            }
                                            else
                                                cb(target, objs);
                                        }
                                        finally
                                        {
                                            objCtx?.SetContext(null);
                                        }
                                    };

                                    var syncCtx = SynchronizationContext.Current;
                                    Action<object[], ProxyContext> callback = (syncCtx != null) ? ((objs, ctx) => syncCtx.Post(_ => cbUnsync(objs, ctx), null)) : cbUnsync;

                                    bool doSubscribe = sigCont.Callbacks == null;
                                    sigCont.Callbacks += callback;
                                    ret.Set(() =>
                                    {
                                        sigCont.Callbacks -= callback;
                                        if (sigCont.Callbacks == null && sigCont.Disposer != null)
                                            sigCont.Disposer();
                                    });

                                    if (doSubscribe)
                                    {
                                        SignalHandler handler = msg =>
                                        {
                                            if (!SenderMatches(msg))
                                                return;
                                            if (sigCont.Callbacks != null)
                                            {
                                                var path = msg.Header.Path ?? ObjectPath.Root;
                                                var objs = (s.ArgTypes.Length > 0) ? msg.GetObjs(s.ArgTypes) : Enumerable.Empty<object>();
                                                if (s.LastParamIsPath)
                                                    objs = objs.Concat(new object[] { path });
                                                sigCont.Callbacks(objs.ToArray(), new ProxyContext(Parent.Connection, msg.Header.Path, msg));
                                            }
                                        };
                                        Parent.Parent.DBusConnection.WatchSignalAsync(handler, Parent.InterfaceName, s.Name, Parent.Service, Parent.ObjectPath)
                                            .ContinueWith(t => sigCont.Disposer = () => t.Result.Dispose());
                                    }
                                    invocation.ReturnValue = Task.FromResult<IDisposable>(ret);
                                }
                                else
                                    throw new Exception($"Method \"{invocation.Method.Name}\" not found. Internal bug!");
                            }


                        }
                    }
                    
                }

                void CheckDisposed() { if (Parent == null) throw new ObjectDisposedException("this"); }
            }

            public void Dispose() => interceptor.Dispose();
        }

        class DBusObjectBase<T> : DBusObjectBase, IDBusObjectProxy<T>
        {
            public DBusObjectBase(ClientProxyManager parent, Type type, ObjectPath path, string interfaceName, string service)
                : base(parent, type, path, interfaceName, service)
            { }
        }

        Dictionary<(Type type, string interfaceName, ObjectPath path, string serviceName), DBusObjectBase> instances = new Dictionary<(Type type, string interfaceName, ObjectPath path, string serviceName), DBusObjectBase>();
        object @lock = new object();

        IDBusObjectProxy IClientObjectProvider.GetInstance(Type type, ObjectPath path, string interfaceName, string serviceName)
        {
            ThrowIfDisposed();
            if (type == null)
                throw new ArgumentNullException(nameof(type));
            if (string.IsNullOrEmpty(interfaceName))
                throw new ArgumentNullException(nameof(interfaceName));
            if (serviceName == null)
                throw new ArgumentNullException(nameof(serviceName));

            if (!type.IsInterface)
                throw new ArgumentException("type must be interface");

            lock (@lock)
            {
                var inst = instances.GetOrSet((type, interfaceName, path, serviceName), () =>
                {
                    var componentType = typeof(DBusObjectBase<>).MakeGenericType(type);
                    var componentIType = typeof(IDBusObjectProxy<>).MakeGenericType(type);
                    return (DBusObjectBase)Activator.CreateInstance(componentType, this, type, path, interfaceName, serviceName);
                });
                return inst.ProxyInstance;
            }
        }

        T IClientObjectProvider.GetInstance<T>(ObjectPath path, string interfaceName, string serviceName)
        {
            return (T)((IClientObjectProvider)this).GetInstance(typeof(T), path, interfaceName, serviceName);
        }

        void ThrowIfDisposed()
        {
            if (IsDisposed)
                throw new ObjectDisposedException(typeof(ClientProxyManager).Name);
        }

        public bool IsDisposed => _connection == null;

        public void Dispose()
        {
            lock (@lock)
            {
                if (_connection == null)
                    return;
                _connection = null;
                foreach (var inst in instances)
                    inst.Value.Dispose();
                instances = null;
            }
        }
    }
}
