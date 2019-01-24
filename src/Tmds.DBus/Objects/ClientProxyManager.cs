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
            public DBusObjectBase(ClientProxyManager parent, Type type, TypeDescription typeDescription, ObjectPath path, string interfaceName, string service)
            {
                Parent = parent;
                Type = type;
                TypeDescriptor = typeDescription;
                ObjectPath = path;
                InterfaceName = interfaceName;
                Service = service;
                interceptor = new Interceptor(this);
                var componentIType = typeof(IDBusObjectProxy<>).MakeGenericType(type);
                //ProxyInstance = (IDBusObjectProxy)generator.CreateInterfaceProxyWithTarget(componentIType, new[] { type }, this, interceptor);
                ProxyInstance = (IDBusObjectProxy)generator.CreateInterfaceProxyWithTargetInterface(componentIType, new[] { type }, this, interceptor);
            }

            public readonly ClientProxyManager Parent;

            public ObjectPath ObjectPath { get; }

            public string InterfaceName { get; }

            public string Service { get;  }

            public Type Type { get;  }

            public TypeDescription TypeDescriptor { get; }

            public IConnection Connection => Parent.Connection;

            public IDBusObjectProxy ProxyInstance { get; }

            readonly Interceptor interceptor;

            public sealed class Interceptor : IInterceptor, IDisposable
            {
                public Interceptor(DBusObjectBase parent)
                {
                    parent_ = parent;
                    if (parent.TypeDescriptor.PropertiesForNames.Count > 0)
                        propertyGetter = parent.Parent.DBusConnection.ProxyProvider.GetInstance<IProperties>(parent.ObjectPath, parent.Service);
                    signals = parent.TypeDescriptor.Signals.ToDictionary(s => s.Name, s => new SignalEntry());
                }

                volatile DBusObjectBase parent_;
                public DBusObjectBase Parent => parent_;
                public void Dispose() => parent_ = null;

                readonly IProperties propertyGetter;

                readonly Dictionary<string, SignalEntry> signals;
                class SignalEntry
                {
                    public ConstructorInvoker TupleCreator;
                    public Action<object[], ProxyContext> Callbacks;
                    public Action Disposer;
                }

                class SigDisposable : IDisposable
                {
                    readonly object @lock = new object();
                    Action onDispose;
                    public void Dispose()
                    {
                        Action onDisp = null;
                        lock (@lock)
                        {
                            if (IsDisposed)
                                return;
                            IsDisposed = true;
                            onDisp = onDispose;
                        }
                        onDisp?.Invoke();
                    }
                    public void Set(Action onDisp)
                    {
                        lock (@lock)
                        {
                            this.onDispose = onDisp;
                        }
                    }
                    public bool IsDisposed { get; private set; }
                }

                void ThrowEx(string message) => throw new Exception(message);
                void ThrowArgumentEx(string message) => throw new ArgumentException(message);

                public void Intercept(IInvocation invocation)
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
                    {
                        CheckDisposed(parent);
                        if (invocation.Method.IsSpecialName)
                        {
                            var methodName = invocation.Method.Name;
                            bool isGet = methodName.StartsWith("get_", StringComparison.Ordinal);
                            if (isGet || methodName.StartsWith("set_", StringComparison.Ordinal))
                            {
                                var propName = methodName.Substring(4);
                                if (!parent.TypeDescriptor.PropertiesForNames.TryGetValue(propName, out TypeDescription.PropertyDef propEntry))
                                    ThrowEx($"Property \"{propName}\" not found. Internal bug!");
                                if (isGet)
                                {
                                    var getTask = propertyGetter.Get(parent.InterfaceName, propName);
                                    getTask.Wait();
                                    invocation.ReturnValue = getTask.Result;
                                }
                                else // set
                                {
                                    var setTask = propertyGetter.Set(parent.InterfaceName, propName, invocation.Arguments[0]);
                                    setTask.Wait();
                                }
                            }
                            else
                                ThrowArgumentEx("Unknown/unexpected special method: " + invocation.Method.Name);
                        }
                        else
                        {
                            if (parent.TypeDescriptor.MethodsForInfos.TryGetValue(invocation.Method, out TypeDescription.MethodDef m))
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
                                        Path = parent.ObjectPath,
                                        Interface = parent.InterfaceName,
                                        Member = m.Name,
                                        Destination = parent.Service,
                                    });
                                    reqMsg.WriteObjs(invocation.Arguments, m.ArgTypes);
                                    parent.Parent.DBusConnection.CallMethodAsync(reqMsg, checkConnected: false)
                                        .ContinueWith(reply =>
                                        {
                                            if (reply.IsFaulted)
                                                ret.SetException((reply.Exception.InnerExceptions.Count == 1) ? reply.Exception.InnerException : reply.Exception);
                                            else
                                            {
                                                var replyMsg = reply.Result;

                                                // Check returned signature
                                                bool returnSigGood = false;
                                                bool wrapInTuple = false;
                                                if (m.ReturnSignature == Signature.Empty)
                                                    returnSigGood = !replyMsg.Header.Signature.HasValue || (replyMsg.Header.Signature.Value == Signature.Empty);
                                                else if (replyMsg.Header.Signature.HasValue)
                                                {
                                                    var replySig = replyMsg.Header.Signature.Value;
                                                    returnSigGood = replySig == m.ReturnSignature;
                                                    if (!returnSigGood)
                                                    {
                                                        if (!replySig.IsSingleCompleteType && m.ReturnSignature.IsSingleCompleteType) 
                                                        {
                                                            // Possibly need to wrap multiple return args in tuple (struct)
                                                            wrapInTuple = returnSigGood = m.ReturnSignature == Signature.MakeStruct(replySig);
                                                        }
                                                    }
                                                }
                                                if (!returnSigGood)
                                                    ret.SetException(new ReplyArgumentsDifferentFromExpectedException(m.MethodInfo, m.ReturnSignature, replyMsg));
                                                else
                                                {
                                                    if (m.ReturnSignature == Signature.Empty)
                                                        ret.SetResult(true);
                                                    else
                                                    {
                                                        object[] objs;
                                                        if (wrapInTuple)
                                                        {
                                                            objs = replyMsg.GetObjs(m.ReturnType.GenericTypeArguments).ToArray(); // ValueTuple<> individual arguments
                                                            // Rewrap in ValueTuple
                                                            objs = new object[] { m.ReturnType.GenericTypeArguments.AsValueTupleCreator()(objs) };
                                                        }
                                                        else
                                                            objs = replyMsg.GetObjs(new[] { m.ReturnType }).ToArray();

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
                                    if (!m.IsAsync)
                                    {
                                        ret.Task.Wait();
                                        if (m.HasReturnValue)
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
                                if (invocation.Method.Name.StartsWith("Watch", StringComparison.Ordinal) && ((sigName = invocation.Method.Name.Substring(5)).Length > 0) &&
                                    parent.TypeDescriptor.SignalsForNames.TryGetValue(sigName, out TypeDescription.SignalDef s))
                                {
                                    var sigCont = signals[sigName];
                                    var ret = new SigDisposable();

                                    var delArg = (Delegate)invocation.Arguments[0];
                                    var target = delArg.Target;
                                    var cb = delArg.Method.DelegateForMethod();

                                    if (s.ArgsAsTuple && sigCont.TupleCreator == null)
                                        sigCont.TupleCreator = s.ArgTypes.AsValueTupleCreator();

                                    void cbUnsync(object[] objs, ProxyContext ctx)
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
                                    }

                                    var syncCtx = SynchronizationContext.Current;
                                    var callback = (syncCtx != null) ? 
                                        new Action<object[], ProxyContext>(((objs, ctx) => syncCtx.Post(_ => cbUnsync(objs, ctx), null))) : cbUnsync;

                                    bool doSubscribe = sigCont.Callbacks == null;
                                    sigCont.Callbacks += callback;
                                    ret.Set(() =>
                                    {
                                        if (sigCont.Callbacks != null)
                                        {
                                            sigCont.Callbacks -= callback;
                                            if (sigCont.Callbacks == null && sigCont.Disposer != null)
                                                sigCont.Disposer();
                                        }
                                    });

                                    if (doSubscribe)
                                    {
                                        void handler(Message msg)
                                        {
                                            bool senderMatches = string.IsNullOrEmpty(msg.Header.Sender) ||
                                                     string.IsNullOrEmpty(parent.Service) ||
                                                     (parent.Service[0] != ':' && msg.Header.Sender[0] == ':') ||
                                                     parent.Service == msg.Header.Sender;
                                            if (!senderMatches)
                                                return;
                                            if (sigCont.Callbacks != null)
                                            {
                                                var path = msg.Header.Path ?? ObjectPath.Root;
                                                var objs = (s.ArgTypes.Length > 0) ? msg.GetObjs(s.ArgTypes) : Enumerable.Empty<object>();
                                                if (s.LastParamIsPath)
                                                    objs = objs.Concat(new object[] { path });
                                                sigCont.Callbacks(objs.ToArray(), new ProxyContext(parent.Connection, msg.Header.Path, msg));
                                            }
                                        }
                                        parent.Parent.DBusConnection.WatchSignalAsync(handler, parent.InterfaceName, s.Name, parent.Service, parent.ObjectPath)
                                            .ContinueWith(t => sigCont.Disposer = () => t.Result.Dispose());
                                    }
                                    invocation.ReturnValue = Task.FromResult<IDisposable>(ret);
                                }
                                else
                                    ThrowEx($"Method \"{invocation.Method.Name}\" not found. Internal bug!");
                            }
                        }
                    }
                }

                void CheckDisposed(DBusObjectBase p = null)
                {
                    if (p == null)
                        p = parent_;
                    if (p == null)
                        throw new ObjectDisposedException("this");
                }
            }

            public void Dispose() => interceptor.Dispose();
        }

        class DBusObjectBase<T> : DBusObjectBase, IDBusObjectProxy<T>
        {
            public DBusObjectBase(ClientProxyManager parent, Type type, TypeDescription typeDescription, ObjectPath path, string interfaceName, string service)
                : base(parent, type, typeDescription, path, interfaceName, service)
            { }
        }

        static System.Collections.Concurrent.ConcurrentDictionary<Type, (ConstructorInvoker<DBusObjectBase> constructor, TypeDescription typeDesc)> typeConstructors = new System.Collections.Concurrent.ConcurrentDictionary<Type, (ConstructorInvoker<DBusObjectBase> constructor, TypeDescription typeDesc)>();

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

            var (c, td) = typeConstructors.GetOrAdd(type, t =>
                (typeof(DBusObjectBase<>).MakeGenericType(t).GetConstructors()[0].DelegateForConstructor<DBusObjectBase>(),
                    TypeDescription.GetDescriptor(t, MemberExposure.AllInterfaces)));
            var inst = c(this, type, td, path, interfaceName, serviceName);
            return inst.ProxyInstance;
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
            var c = Interlocked.Exchange(ref _connection, null);
            if (c != null)
            {
            }
        }
    }
}
