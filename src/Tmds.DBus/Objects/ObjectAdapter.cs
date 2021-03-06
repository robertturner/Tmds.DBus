﻿using BaseLibs.Tasks;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus.Objects.Internal;
using Tmds.DBus.Protocol;
using BaseLibs.Types;
using BaseLibs.Collections;
using BaseLibs.Tuples;
using System.Collections.Concurrent;

namespace Tmds.DBus.Objects
{
    public sealed class ObjectAdapter : IMethodHandler
    {
        IConnection connnection;

        sealed class MethodDetails
        {
            public MethodInvoker Invoker;
            public TypeDescription.MethodDef MethodDef;
        }
        public sealed class PropertyDetails
        {
            internal PropertyDetails(MemberGetter getter, MemberSetter setter, TypeDescription.PropertyDef def)
            { Getter = getter; Setter = setter; Property = def; }
            public readonly MemberGetter Getter;
            public readonly MemberSetter Setter;
            public readonly TypeDescription.PropertyDef Property;
        }

        sealed class SigInst : IDisposable
        {
            public SigInst(object disposer) { Disposer = disposer; }
            object Disposer; // Could be Task<IDisposable>, IDisposable or null
            public void Dispose()
            {
                var d = Disposer;
                Disposer = null;
                if (d == null)
                    return;
                if (d is Task<IDisposable> taskDisposer)
                {
                    taskDisposer.ContinueWith(r =>
                    {
                        if (!r.IsFaulted && r.Result != null)
                            r.Result.Dispose();
                    });
                }
                else if (d is IDisposable disposer)
                    disposer.Dispose();
                else
                    throw new InvalidOperationException("Internal bug. Disposer type not handled!");
                
            }
        }

        sealed class InterfaceInfo
        {
            public readonly string InterfaceName;
            public readonly TypeDescription Descriptor;

            public readonly Dictionary<(string name, Signature sig), MethodDetails> methodHandlers;
            public readonly Dictionary<string, PropertyDetails> propertyHandlers;
            public readonly Dictionary<string, MethodInvoker> signalExtras;

            public InterfaceInfo(TypeDescription descriptor, string interfaceName)
            {
                Descriptor = descriptor; InterfaceName = interfaceName;
                signalExtras = descriptor.SignalsForNames.ToDictionary(s => s.Key, s =>
                {
                    var instMthd = s.Value.MethodInfo;
                    if (instMthd.DeclaringType.IsInterface)
                    {
                        // Map interface method (uncallable) to instance method
                        var map = descriptor.Type.GetInterfaceMap(instMthd.DeclaringType);
                        for (int i = 0; i < map.InterfaceMethods.Length; ++i)
                        {
                            if (map.InterfaceMethods[i].Equals(instMthd))
                            {
                                instMthd = map.TargetMethods[i];
                                break;
                            }
                        }
                    }
                    return instMthd.DelegateForMethod();
                });
                methodHandlers = descriptor.MethodsForInfos.ToDictionary(m => (m.Key.Name, m.Value.MethodSignature),
                    m =>
                    {
                        if (m.Value.ReturnTypes.Length > 1)
                            throw new ArgumentException("Unable to handle more than one return value");
                        var instMthd = m.Value.MethodInfo;
                        if (instMthd.DeclaringType.IsInterface)
                        {
                            // Map interface method (uncallable) to instance method
                            var map = descriptor.Type.GetInterfaceMap(instMthd.DeclaringType);
                            for (int i = 0; i < map.InterfaceMethods.Length; ++i)
                            {
                                if (map.InterfaceMethods[i].Equals(instMthd))
                                {
                                    instMthd = map.TargetMethods[i];
                                    break;
                                }
                            }
                        }
                        return new MethodDetails { MethodDef = m.Value, Invoker = instMthd.DelegateForMethod() };
                    });
                propertyHandlers = descriptor.PropertiesForNames.ToDictionary(p => p.Key,
                    p =>
                    {
                        var propInfo = p.Value.PropertyInfo;
                        var sig = Signature.GetSig(p.Value.Type.Type);
                        var argNameAttr = propInfo.Attribute<DBusArgNameAttribute>();
                        return new PropertyDetails(propInfo.CanRead ? propInfo.DelegateForGetProperty() : null,
                            propInfo.CanWrite ? propInfo.DelegateForSetProperty() : null, p.Value);
                    });
            }

            public InterfaceObjDef InterfaceDef => Descriptor;
        }

        static readonly ConcurrentDictionary<(Type objType, string interfaceName, MemberExposure exposure), InterfaceInfo> ifceCache = new ConcurrentDictionary<(Type objType, string interfaceName, MemberExposure exposure), InterfaceInfo>();

        public ObjectAdapter(IConnection connnection, ObjectPath? path, string interfaceName, object instance, MemberExposure exposure, Func<Exception, bool> proxyExceptionHandler = null, SynchronizationContext syncCtx = null)
        {
            if (interfaceName == null)
                throw new ArgumentNullException(nameof(interfaceName));
            Instance = instance ?? throw new ArgumentNullException(nameof(instance));
            this.connnection = connnection ?? throw new ArgumentNullException(nameof(Connection));
            this.proxyExceptionHandler = proxyExceptionHandler;
            this.syncCtx = syncCtx ?? SynchronizationContext.Current;
            contextObj = instance as ObjectContext;

            var objType = instance.GetType();
            ifceInfo = ifceCache.GetOrAdd((objType, interfaceName, exposure), _ => new InterfaceInfo(TypeDescription.GetOrCreateCached(objType, exposure), interfaceName));
            
            // Register signals
            foreach (var sig in ifceInfo.Descriptor.SignalsForNames)
            {
                var sigDef = sig.Value;
                var sigExtras = ifceInfo.signalExtras[sig.Key];
                object SignalCallback(object[] callbackArgs)
                {
                    CheckDisposed();
                    var msgPath = path;
                    if (sig.Value.LastParamIsPath)
                    {
                        msgPath = (ObjectPath)callbackArgs[sig.Value.ArgDefs.Length];
                        callbackArgs = callbackArgs.Take(sig.Value.ArgDefs.Length).ToArray();
                    }
                    if (msgPath.HasValue)
                    {
                        // Emit signal
                        var msg = new Message(new Header(MessageType.Signal)
                        {
                            Path = msgPath,
                            Interface = InterfaceName,
                            Member = sig.Key
                        });
                        msg.WriteObjs(callbackArgs, sig.Value.ArgTypes);
                        connnection.BaseDBusConnection.EmitSignal(msg);
                    }
                    else // Globally registered object
                    {
                        // What should we do here? Can't think of any easy way to find/provide path
                    }
                    return null;
                }
                var callbackInst = sig.Value.CallbackType.BuildDynamicHandler(SignalCallback);
                var disposer = sigExtras(Instance, callbackInst);
                signals[sig.Key] = new SigInst(disposer);
            }
            if (ifceInfo.Descriptor.PropertiesForNames.Any() && instance is INotifyPropertyChanged notifyChanged)
                notifyChanged.PropertyChanged += NotifyChanged_PropertyChanged;
        }

        public delegate void PropertyChangedHandler(string interfaceName, string propertyName, object newValue);
        public event PropertyChangedHandler PropertyChanged;

        void NotifyChanged_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                var propName = e.PropertyName;
                if (ifceInfo.propertyHandlers.TryGetValue(propName, out PropertyDetails pd) && pd.Getter != null)
                {
                    var val = pd.Getter(Instance);
                    handler(InterfaceName, propName, val);
                }
            }
        }

        readonly Dictionary<string, SigInst> signals = new Dictionary<string, SigInst>();
        readonly InterfaceInfo ifceInfo;
        Func<Exception, bool> proxyExceptionHandler;
        readonly ObjectContext contextObj;

        public string InterfaceName => ifceInfo.InterfaceName;

        public object Instance { get; private set; }

        readonly SynchronizationContext syncCtx;

        MethodDetails GetMethod(Message methodCall)
        {
            var sig = methodCall.Header.Signature ?? Signature.Empty;

            if (!ifceInfo.methodHandlers.TryGetValue((methodCall.Header.Member, sig), out MethodDetails ret))
                ret = null;
            return ret;
        }

        public async Task<Message> HandleMethodCall(Message methodCall)
        {
            CheckDisposed();
            var method = GetMethod(methodCall);
            if (method == null)
                return MessageHelper.ConstructErrorReply(methodCall, DBusErrors.UnknownMethod, methodCall.Header.Member);
            var callArgs = methodCall.GetObjs(method.MethodDef.ArgTypes);
            if (method.MethodDef.LastArgIsPathObj)
                callArgs = callArgs.Concat(((object)methodCall.Header.Path.Value).SingleItemAsEnumerable());
            contextObj?.SetContext(new ProxyContext(connnection, methodCall.Header.Path.Value, methodCall));
            var objs = callArgs.ToArray();
            object ret = null;
            Message replyMsg = null;
            bool handled = false;
            try
            {
                ret = method.Invoker(Instance, objs);
                if (method.MethodDef.IsAsync)
                    await (Task)ret;
            }
            catch (DBusException ex)
            {
                replyMsg = MessageHelper.ConstructErrorReply(methodCall, ex.ErrorName, ex.ErrorMessage);
                handled = true;
            }
            catch (Exception ex)
            {
                if (proxyExceptionHandler == null || !proxyExceptionHandler(ex))
                {
                    if (ex is AggregateException aggrEx && aggrEx.InnerExceptions.Count == 1)
                        ex = aggrEx.InnerException;
                    replyMsg = MessageHelper.ConstructErrorReply(methodCall, DBusErrors.Failed, ex.Message);
                }
                handled = true;
            }
            finally
            {
                contextObj?.SetContext(null);
            }
            if (!handled && replyMsg == null)
            {
                if (method.MethodDef.IsAsync && method.MethodDef.HasReturnValue)
                {
                    var genT = ((Task)ret).TryGetAsGenericTask();
                    ret = genT.Result;
                }
                replyMsg = (!method.MethodDef.HasReturnValue) ?
                    MessageHelper.ConstructReply(methodCall) :
                    MessageHelper.ConstructReplyWithTypes(methodCall, (ret, method.MethodDef.ReturnType));
            }
            return replyMsg;
        }

        public InterfaceObjDef GetInterfaceDefinitions()
        {
            CheckDisposed();
            return ifceInfo.InterfaceDef;
        }

        public bool CheckExposure(ObjectPath path)
        {
            if (contextObj != null)
            {
                try
                {
                    contextObj.SetContext(new ProxyContext(connnection, path, null));
                    return contextObj.CheckExposure();
                }
                finally
                {
                    contextObj.SetContext(null);
                }
            }
            return true;
        }

        public IReadOnlyDictionary<string, PropertyDetails> Properties => ifceInfo.propertyHandlers;

        void CheckDisposed()
        {
            if (connnection == null)
                throw new ObjectDisposedException("this");
        }

        public void Dispose()
        {
            lock (this)
            {
                if (Instance != null && Instance is INotifyPropertyChanged propChanged)
                    propChanged.PropertyChanged -= NotifyChanged_PropertyChanged;
                PropertyChanged = null;
                proxyExceptionHandler = null;
                connnection = null;
                foreach (var sigInst in signals)
                    sigInst.Value.Dispose();
                signals.Clear();
            }
        }
    }
}
