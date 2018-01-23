using BaseLibs.Collections;
using BaseLibs.Tuples;
using BaseLibs.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace Tmds.DBus.Objects
{
    public class TypeDescription : InterfaceObjDef
    {
        public const string DefaultMethodReturnArgName = "value";

        static Dictionary<(Type type, MemberExposure exposure), TypeDescription> typesCache = new Dictionary<(Type type, MemberExposure exposure), TypeDescription>();
        static object staticLock = new object();

        public static TypeDescription GetDescriptor(Type type, MemberExposure exposure = MemberExposure.OnlyDBusInterfaces)
        {
            lock (staticLock)
            {
                var accessor = (type, exposure);
                if (!typesCache.TryGetValue(accessor, out TypeDescription desc))
                {
                    desc = new TypeDescription(type, exposure);
                    typesCache[accessor] = desc;
                }
                return desc;
            }
        }

        public readonly Type Type;

        static readonly MethodInfo[] objectMethodNames = typeof(object).GetMethods(BindingFlags.Instance | BindingFlags.Public);
        private TypeDescription(Type objType, MemberExposure exposure)
            : base(null)
        {
            Type = objType;
            IEnumerable<Type> ifces = objType.GetInterfaces();
            if (Type.IsInterface)
                ifces = ifces.Concat(Type.SingleItemAsEnumerable());
            IEnumerable<MethodInfo> methods;
            IEnumerable<PropertyInfo> properties;
            switch (exposure)
            {
                case MemberExposure.ExposeAll:
                    {
                        methods = objType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                        properties = objType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                        var ifceAttr = objType.GetCustomAttribute<DBusInterfaceAttribute>(inherit: true);
                        if (ifceAttr != null)
                            InterfaceName = ifceAttr.Name;
                    }
                    break;
                case MemberExposure.OnlyDBusInterfaces:
                case MemberExposure.AllInterfaces:
                    var classMembers = (exposure == MemberExposure.OnlyDBusInterfaces) && !ifces.Any();
                    if (classMembers)
                        ifces = new[] { objType }; // Support class implementing
                    var ms = new List<MethodInfo>();
                    var ps = new List<PropertyInfo>();
                    foreach (var ifce in ifces)
                    {
                        var ifceAttr = ifce.GetCustomAttribute<DBusInterfaceAttribute>(inherit: true);
                        if ((exposure == MemberExposure.AllInterfaces && !classMembers) || ifceAttr != null)
                        {
                            ps.AddRange(ifce.GetProperties(BindingFlags.Instance | BindingFlags.Public));
                            var ifceMethods = ifce.GetMethods(BindingFlags.Instance | BindingFlags.Public);
                            if (!classMembers)
                                ms.AddRange(ifceMethods);
                            else
                            {
                                // Have to ignore object methods
                                foreach (var m in ifceMethods)
                                {
                                    var mToAdd = m;
                                    foreach (var objMethod in objectMethodNames)
                                    {
                                        if (objMethod.Name == m.Name)
                                        {
                                            if (objMethod.ReturnType == m.ReturnType && !m.IsGenericMethod)
                                                mToAdd = null;
                                            break;
                                        }
                                    }
                                    if (mToAdd != null)
                                        ms.Add(mToAdd);
                                }
                            }
                        }
                        if (ifceAttr != null)
                        {
                            if (InterfaceName != null)
                                throw new ArgumentException("Ambiguous interface name. Implments multiple interfaces with DBusInterfaceAttributes");
                            InterfaceName = ifceAttr.Name;
                        }
                    }
                    if (exposure == MemberExposure.OnlyDBusInterfaces && InterfaceName == null)
                        throw new ArgumentException("Instance does not implement DBusInterface attribute");
                    methods = ms;
                    properties = ps;
                    break;
                default:
                    throw new ArgumentException($"Unknown {nameof(MemberExposure)} member: {exposure}");
            }

            foreach (var m in methods)
            {
                if (m.HasAttribute<DBusHideMethodAttribute>())
                    continue;

                if (m.Name.StartsWith("Watch") && m.Name.Length > 5)
                {
                    var sigName = m.Name.Substring(5);
                    var paramObjs = m.GetParameters();
                    var retType = m.ReturnType;

                    Type[] args = null;
                    string[] argNames = null;
                    Type tupleArg = null;
                    Type callbackType = null;
                    bool lastParamIsPath = false;
                    if ((retType == typeof(Task<IDisposable>) || retType == typeof(IDisposable) || retType == typeof(void)) && paramObjs.Length == 1)
                    {
                        callbackType = paramObjs[0].ParameterType;
                        if (callbackType.IsGenericType && callbackType.GetGenericTypeDefinition().FullName.StartsWith("System.Action`"))
                        {
                            var genArgs = callbackType.GenericTypeArguments;
                            if (genArgs.Length == 1 && genArgs[0].FullName.StartsWith("System.ValueTuple`"))
                            {
                                tupleArg = genArgs[0];
                                args = tupleArg.GenericTypeArguments;
                            }
                            else
                                args = genArgs;
                        }
                        else if (callbackType == typeof(Action))
                            args = new Type[0];
                        else if (typeof(Delegate).IsAssignableFrom(callbackType))
                        {
                            var invokeMethod = callbackType.GetMethod("Invoke");
                            var delParams = invokeMethod.GetParameters();
                            if (delParams.Where((p, i) => p.HasAttribute<DBusPathAttribute>() && i < (delParams.Length - 1)).Any())
                                throw new ArgumentException($"{objType.Name}:{m.Name}: Signal arguments with a {nameof(DBusPathAttribute)} parameter must be the last parameter");
                            if (delParams[delParams.Length - 1].HasAttribute<DBusPathAttribute>())
                            {
                                if (delParams[delParams.Length - 1].ParameterType != typeof(ObjectPath))
                                    throw new ArgumentException($"{objType.Name}:{m.Name}: Parameter {delParams.Length} is marked with {nameof(DBusPathAttribute)} but parameter type is not {nameof(ObjectPath)}");
                                lastParamIsPath = true;
                                delParams = delParams.Take(delParams.Length - 1).ToArray();
                            }
                            args = delParams.Select(dp => dp.ParameterType).ToArray();
                            argNames = delParams.Select(dp => dp.Name).ToArray();
                        }
                        else
                            continue;
                    }
                    if (args == null)
                        throw new TypeLoadException($"Signal method \"{m.Name}\" signature is incorrect. Should be Task<IDisposable> {m.Name}(Action<arguments>)");
                    if (argNames == null)
                        argNames = Enumerable.Range(0, args.Length).Select(i => $"Item{i}").ToArray();

                    var sig = new SignalDef(sigName, 
                        args.Select((a, i) => new ArgDef(argNames[i], a)).ToArray(),
                        tupleArg != null,
                        lastParamIsPath,
                        callbackType,
                        m);
                    signals.Add(sigName, sig);
                }
                else
                    this.methods.Add(m, new MethodDef(m));
            }

            foreach (var p in properties)
            {
                if (p.HasAttribute<DBusHideMethodAttribute>())
                    continue;
                var argNameAttr = p.Attribute<DBusArgNameAttribute>();
                var arg = new ArgDef((argNameAttr != null) ? argNameAttr.Name : string.Empty, p.PropertyType);
                this.properties.Add(p.Name, new PropertyDef(p, arg));
            }
        }

        Dictionary<MethodInfo, MethodDef> methods = new Dictionary<MethodInfo, MethodDef>();
        Dictionary<string, PropertyDef> properties = new Dictionary<string, PropertyDef>();
        Dictionary<string, SignalDef> signals = new Dictionary<string, SignalDef>();

        public override InterfaceObjDef.MethodDef[] Methods => MethodsForInfos.Values.ToArray();
        public override InterfaceObjDef.PropertyDef[] Properties => PropertiesForNames.Values.ToArray();
        public override InterfaceObjDef.SignalDef[] Signals => SignalsForNames.Values.ToArray();

        public IReadOnlyDictionary<MethodInfo, MethodDef> MethodsForInfos => methods;
        public IReadOnlyDictionary<string, SignalDef> SignalsForNames => signals;
        public IReadOnlyDictionary<string, PropertyDef> PropertiesForNames => properties;

        public new class ArgDef : InterfaceObjDef.ArgDef
        {
            public ArgDef(string name, Type type, Signature sig)
                : base(name, sig)
            {
                Type = type;
            }
            public ArgDef(string name, Type type)
                : base(name, Signature.GetSig(type))
            {
                Type = type;
            }
            public ArgDef(string name)
                : base(name, Signature.Empty)
            {
                Type = typeof(void);
            }
            public ArgDef(Type type)
                : base(string.Empty, Signature.GetSig(type))
            {
                Type = type;
            }
            public Type Type { get; private set; }
            public bool HasResult => Type != typeof(void);

        }

        public new class MethodDef : InterfaceObjDef.MethodDef
        {
            public MethodDef(MethodInfo method)
                : base(method.Name)
            {
                MethodInfo = method;
                var retType = method.ReturnType;
                IsAsync = typeof(Task).IsAssignableFrom(retType);
                if (IsAsync)
                {
                    if (retType == typeof(Task))
                        retType = typeof(void);
                    else
                        retType = retType.GetGenericArguments()[0];
                }
                if (retType == typeof(void))
                    ReturnTypes = new ArgDef[0];
                else
                {
                    if (retType.IsGenericType && retType.Name.StartsWith("ValueTuple'"))
                    {
                        // Need to split tuple parts up into individual arguments in reply
                        // Set retArgNames
                        throw new NotImplementedException("Tuple return types not yet implemented");
                    }
                    else
                    {
                        var retArgNameAttr = method.ReturnParameter.Attribute<DBusArgNameAttribute>();
                        ReturnTypes = new[] { new ArgDef(retArgNameAttr != null ? retArgNameAttr.Name : DefaultMethodReturnArgName, retType) };
                    }
                    ReturnType = retType;
                }
                base.ReturnTypes = ReturnTypes;

                var paramObjs = method.GetParameters();
                var args = new List<ArgDef>(paramObjs.Length);
                paramObjs.ForEach((p, i) =>
                {
                    if (p.HasAttribute<DBusPathAttribute>())
                    {
                        if (i != (paramObjs.Length - 1)) // last
                            throw new ArgumentException($"{method.DeclaringType.Name}:{method.Name}: Methods with a {nameof(DBusPathAttribute)} parameter must be the last parameter");
                        if (p.ParameterType != typeof(ObjectPath))
                            throw new ArgumentException($"{method.DeclaringType.Name}:{method.Name}: Parameter {i} is marked with {nameof(DBusPathAttribute)} but parameter type is not {nameof(ObjectPath)}");
                        LastArgIsPathObj = true;
                    }
                    else
                    {
                        var argNameAttr = p.Attribute<DBusArgNameAttribute>();
                        args.Add(new ArgDef((argNameAttr != null) ? argNameAttr.Name : p.Name, p.ParameterType));
                    }
                });
                ArgDefs = args.ToArray();
                ArgTypes = ArgDefs.Select(a => a.Type).ToArray();
                base.ArgTypes = ArgDefs;
            }

            public MethodInfo MethodInfo { get; private set; }

            public new ArgDef[] ReturnTypes { get; private set; }
            public bool HasReturnValue => ReturnTypes.Length > 0;
            /// <summary>
            /// c# method return value type. Single type for single signature, or ValueTuple for multipart signature
            /// </summary>
            public Type ReturnType { get; private set; }

            public bool IsAsync { get; private set; }

            public ArgDef[] ArgDefs { get; private set; }
            public new Type[] ArgTypes { get; private set; }

            public bool LastArgIsPathObj { get; private set; }
        }

        public new class SignalDef : InterfaceObjDef.SignalDef
        {
            public SignalDef(string name, ArgDef[] args, bool argsAsTuple, bool lastParamIsPath, Type callbackType, MethodInfo method)
                : base(name, args)
            {
                ArgDefs = args;
                ArgTypes = args.Select(a => a.Type).ToArray();
                ArgsAsTuple = argsAsTuple;
                LastParamIsPath = lastParamIsPath; CallbackType = callbackType;
                MethodInfo = method;
            }
            public new ArgDef[] ArgDefs { get; private set; }
            public readonly Type[] ArgTypes;
            public readonly bool ArgsAsTuple;
            public readonly bool LastParamIsPath;
            public readonly Type CallbackType;
            public readonly MethodInfo MethodInfo;
        }

        public new class PropertyDef : InterfaceObjDef.PropertyDef
        {
            public PropertyDef(PropertyInfo propInfo, ArgDef arg)
                : base(propInfo.Name, arg, propInfo.CanRead ? (propInfo.CanWrite ? AccessTypes.ReadWrite : AccessTypes.Read) : (propInfo.CanWrite ? AccessTypes.Write : AccessTypes.None))
            {
                PropertyInfo = propInfo;
            }
            public PropertyInfo PropertyInfo { get; private set; }
        }
    }
}
