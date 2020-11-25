using BaseLibs.Tasks;
using BaseLibs.Tuples;
using BaseLibs.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus.Objects.DBus;
using Tmds.DBus.Objects.Internal;
using Tmds.DBus.Protocol;
using System.Reactive.Linq;

namespace Tmds.DBus.Objects
{
    public sealed class ProxyInterceptor : IDisposable
    {
        public ProxyInterceptor(IDBusObjectProxy proxyInfo, TypeDescription typeDescription)
        {
            this.proxyInfo = proxyInfo;
            TypeDescriptor = typeDescription;
        }

        IDBusObjectProxy proxyInfo;

        public Action DisposeHook { get; set; }

        public TypeDescription TypeDescriptor { get; }

        public event Action<DBusException> ExceptionHook;


        IDisposable nameOwnerWatchDisposer;
        Action<bool> serviceUpHookCallback;
        public event Action<bool> ServiceUpHook
        {
            add
            {
                if (nameOwnerWatchDisposer == null)
                {
                    nameOwnerWatchDisposer = proxyInfo.Connection.ServiceFeed()
                        .Where(s => s.service == proxyInfo.Service && serviceUpHookCallback != null)
                        .Subscribe(args => serviceUpHookCallback(args.up));
                }
                serviceUpHookCallback += value;
            }
            remove
            {
                serviceUpHookCallback -= value;
                if (serviceUpHookCallback == null)
                {
                    nameOwnerWatchDisposer?.Dispose();
                    nameOwnerWatchDisposer = null;
                }
            }
        }

        IProperties propertyGetter;

        Dictionary<string, SignalEntry> signals;
        class SignalEntry
        {
            public Action<object[], ProxyContext> Callbacks;
            public Action Disposer;
        }

        class SigDisposable : IDisposable
        {
            Action onDispose;
            public void Dispose()
            {
                var onDisp = Interlocked.Exchange(ref onDispose, null);
                onDisp?.Invoke();
            }
            public void Set(Action onDisp) => onDispose = onDisp;
            public bool IsDisposed => onDispose == null;
        }

        void ThrowEx(string message) => throw new Exception(message);
        void ThrowArgumentEx(string message) => throw new ArgumentException(message);

        public void Dispose()
        {
            var pi = Interlocked.Exchange(ref proxyInfo, null);
            if (pi != null)
            {
                DisposeHook?.Invoke();
                ExceptionHook = null;
                signals.Clear();
                serviceUpHookCallback = null;
                nameOwnerWatchDisposer?.Dispose();
                nameOwnerWatchDisposer = null;
            }
        }

        void CheckDisposed()
        {
            if (proxyInfo == null)
                throw new ObjectDisposedException("this");
        }

        public object Intercept(IClientObjectProvider parent, MethodInfo method, object[] arguments)
        {
            if (signals == null)
            {
                if (TypeDescriptor.PropertiesForNames.Count > 0)
                    propertyGetter = parent.DBusConnection.GetInstance<IProperties>(proxyInfo.ObjectPath, proxyInfo.Service);
                signals = TypeDescriptor.Signals.ToDictionary(s => s.Name, s => new SignalEntry());
            }
            object returnValue = null;
            if (method.Name == nameof(IDisposable.Dispose))
            {
                Dispose();
                return returnValue;
            }
            CheckDisposed();
            if (method.IsSpecialName)
            {
                var methodName = method.Name;
                bool isGet = methodName.StartsWith("get_", StringComparison.Ordinal);
                if (isGet || methodName.StartsWith("set_", StringComparison.Ordinal))
                {
                    var propName = methodName.Substring(4);
                    if (!TypeDescriptor.PropertiesForNames.TryGetValue(propName, out TypeDescription.PropertyDef propEntry))
                        ThrowEx($"Property \"{propName}\" not found. Internal bug!");
                    if (isGet)
                    {
                        var getTask = propertyGetter.Get(proxyInfo.InterfaceName, propName);
                        if (propEntry.IsAsync)
                            returnValue = getTask.CastResultAs(propEntry.Type.Type);
                        else
                        {
                            getTask.Wait();
                            returnValue = getTask.Result;
                        }
                    }
                    else // set
                    {
                        var setTask = propertyGetter.Set(proxyInfo.InterfaceName, propName, arguments[0]);
                        setTask.Wait();
                    }
                }
                else
                    ThrowArgumentEx("Unknown/unexpected special method: " + method.Name);
            }
            else
            {
                if (TypeDescriptor.MethodsForInfos.TryGetValue(method, out TypeDescription.MethodDef m))
                {
                    try
                    {
                        var transaction = new ProxyTransaction();
                        TaskCompletionSourceGeneric ret = null;
                        if (m.IsAsync || m.HasReturnValue)
                        {
                            ret = TaskCompletionSourceGeneric.Create(m.HasReturnValue ? m.ReturnType : typeof(bool), transaction);
                            returnValue = ret.Task;
                        }
                        else
                            returnValue = null;
                        var reqMsg = new Message(new Header(MessageType.MethodCall)
                        {
                            Path = proxyInfo.ObjectPath,
                            Interface = proxyInfo.InterfaceName,
                            Member = m.Name,
                            Destination = proxyInfo.Service,
                        });
                        reqMsg.WriteObjs(arguments, m.ArgTypes);
                        parent.DBusConnection.CallMethodAsync(reqMsg, checkConnected: false)
                            .ContinueWith(reply =>
                            {
                                if (reply.IsFaulted)
                                {
                                    var ex = (reply.Exception.InnerExceptions.Count == 1) ? reply.Exception.InnerExceptions[0] : reply.Exception;
                                    if (ex is DBusException dbusEx)
                                        ExceptionHook?.Invoke(dbusEx);
                                    ret.SetException(ex);
                                }
                                else
                                {
                                    var replyRes = reply.Result;
                                    transaction.RequestSendTime = reply.Result.RequestSendTime;
                                    transaction.ReplyReceivedTime = reply.Result.ReplyReceivedTime;
                                    Message replyMsg = replyRes;

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
                            ret.Task.Wait(); // Exceptions caught below
                            if (m.HasReturnValue)
                                returnValue = ret.Task.TryGetAsGenericTask().Result;
                        }
                    }
                    catch (Exception ex)
                    {
                        bool isAex = false;
                        if (ex is AggregateException aex && aex.InnerExceptions.Count == 1)
                        {
                            isAex = true;
                            ex = aex.InnerExceptions[0];
                        }
                        if (ex is DBusException dbusEx)
                            ExceptionHook?.Invoke(dbusEx);
                        if (m.IsAsync)
                            returnValue = m.HasReturnValue ? ex.AsGenericTaskException(m.ReturnType) : Task.FromException(ex);
                        else if (isAex)
                            throw ex;
                        else throw;
                    }
                }
                else
                {
                    string sigName;
                    if (method.Name.StartsWith("Watch", StringComparison.Ordinal) && ((sigName = method.Name.Substring(5)).Length > 0) &&
                        TypeDescriptor.SignalsForNames.TryGetValue(sigName, out TypeDescription.SignalDef s))
                    {
                        var sigCont = signals[sigName];
                        var ret = new SigDisposable();

                        var delArg = (Delegate)arguments[0];
                        var target = delArg.Target;
                        var cb = delArg.Method.DelegateForMethod();

                        void CBUnsync(object[] objs, ProxyContext ctx)
                        {
                            if (ret.IsDisposed)
                                return;
                            ObjectContext objCtx = target as ObjectContext;
                            objCtx?.SetContext(ctx);
                            try
                            {
                                cb(target, objs);
                            }
                            finally
                            {
                                objCtx?.SetContext(null);
                            }
                        }

                        var syncCtx = SynchronizationContext.Current;
                        var callback = (syncCtx != null) ?
                            new Action<object[], ProxyContext>(((objs, ctx) => syncCtx.Post(_ => CBUnsync(objs, ctx), null))) : CBUnsync;

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
                            var tcs = new TaskCompletionSource<IDisposable>();
                            void handler(Message msg)
                            {
                                if (proxyInfo == null)
                                {
                                    if (sigCont.Callbacks == null && sigCont.Disposer != null)
                                        sigCont.Disposer();
                                    return;
                                }
                                bool senderMatches = string.IsNullOrEmpty(msg.Header.Sender) ||
                                            string.IsNullOrEmpty(proxyInfo.Service) ||
                                            (proxyInfo.Service[0] != ':' && msg.Header.Sender[0] == ':') ||
                                            proxyInfo.Service == msg.Header.Sender;
                                if (!senderMatches)
                                    return;
                                if (sigCont.Callbacks != null)
                                {
                                    var objs = (s.ArgTypes.Length > 0) ? msg.GetObjs(s.ArgTypes) : Enumerable.Empty<object>();
                                    if (s.LastParamIsPath)
                                        objs = objs.Concat(new object[] { msg.Header.Path ?? ObjectPath.Root });
                                    sigCont.Callbacks(s.ArgsAreInTuple ? new object[] { s.ArgTypes.AsValueTupleCreator()(objs.ToArray()) } : objs.ToArray(),
                                        new ProxyContext(parent.Connection, msg.Header.Path, msg));
                                }
                            }
                            try
                            {
                                parent.DBusConnection.WatchSignalAsync(handler, proxyInfo.InterfaceName, s.Name, proxyInfo.Service, proxyInfo.ObjectPath)
                                    .ContinueWith(t =>
                                    {
                                        if (t.IsFaulted)
                                        {
                                            try
                                            {
                                                ret.Dispose();
                                            }
                                            catch { }
                                            var ex = (t.Exception.InnerExceptions.Count == 1) ? t.Exception.InnerException : t.Exception;
                                            if (ex is DBusException dbusEx)
                                                ExceptionHook?.Invoke(dbusEx);
                                            tcs.SetException(ex);
                                        }
                                        else
                                        {
                                            var res = t.Result;
                                            sigCont.Disposer = () => res.Dispose();
                                            tcs.SetResult(ret);
                                        }
                                    });
                            }
                            catch (Exception ex)
                            {
                                if (ex is DBusException dbusEx)
                                    ExceptionHook?.Invoke(dbusEx);
                                tcs.SetException(ex);
                            }
                            returnValue = tcs.Task;
                        }
                        else
                            returnValue = Task.FromResult<IDisposable>(ret);
                    }
                    else
                        ThrowEx($"Method \"{method.Name}\" not found. Internal bug!");
                }
            }
            return returnValue;
        }
    }
}
