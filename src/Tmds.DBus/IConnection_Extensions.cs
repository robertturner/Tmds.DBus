// Copyright 2016 Tom Deseyn <tom.deseyn@gmail.com>
// This software is made available under the MIT License
// See COPYING for details

using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus.Objects;
using Tmds.DBus.Protocol;

namespace Tmds.DBus
{
    public static class IConnection_Extensions
    {
        public static async Task<RequestNameReply> RegisterServiceCallback(this IConnection connection, string serviceName, ServiceRegistrationOptions options = ServiceRegistrationOptions.Default, Action<string> onAquired = null, Action<string> onLost = null)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));
            if (string.IsNullOrEmpty(serviceName))
                throw new ArgumentNullException(nameof(serviceName));

            IDisposable acquireDisposer = null;
            if (onAquired != null)
            {
                acquireDisposer = await connection.DBus.WatchNameAcquired(name =>
                {
                    if (name == serviceName)
                    {
                        onAquired(name);
                        var c = acquireDisposer;
                        if (c != null)
                            c.Dispose();
                    }
                });
            }
            IDisposable lostDisposer = null;
            if (onLost != null)
            {
                lostDisposer = await connection.DBus.WatchNameLost(name =>
                {
                    if (name == serviceName)
                    {
                        onLost(name);
                        var c = lostDisposer;
                        if (c != null)
                            c.Dispose();
                    }
                });
            }
            var requestOptions = RequestNameOptions.None;
            if (options.HasFlag(ServiceRegistrationOptions.ReplaceExisting))
                requestOptions |= RequestNameOptions.ReplaceExisting;
            if (options.HasFlag(ServiceRegistrationOptions.AllowReplacement))
                requestOptions |= RequestNameOptions.AllowReplacement;

            try
            {
                return await connection.DBus.RequestName(serviceName, requestOptions);
            }
            catch (Exception)
            {
                if (acquireDisposer != null)
                    acquireDisposer.Dispose();
                if (lostDisposer != null)
                    lostDisposer.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Register service name and wait (Task) until name acquired.
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="serviceName"></param>
        /// <param name="options"></param>
        /// <param name="cancellationToken"></param>
        /// <param name="onLost"></param>
        /// <returns></returns>
        public static async Task RegisterServiceWait(this IConnection connection, string serviceName, ServiceRegistrationOptions options = ServiceRegistrationOptions.Default, Action<string> onLost = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));
            if (string.IsNullOrEmpty(serviceName))
                throw new ArgumentNullException(nameof(serviceName));

            var tcs = new TaskCompletionSource<bool>();
            IDisposable acquireDisposer = null;
            acquireDisposer = await connection.DBus.WatchNameAcquired(name =>
            {
                if (name == serviceName)
                {
                    tcs.TrySetResult(true);
                    var c = acquireDisposer;
                    if (c != null)
                        c.Dispose();
                }
            });
            IDisposable lostDisposer = null;
            if (onLost != null)
            {
                lostDisposer = await connection.DBus.WatchNameLost(name =>
                {
                    if (name == serviceName)
                    {
                        onLost(name);
                        var c = lostDisposer;
                        if (c != null)
                            c.Dispose();
                        tcs.TrySetResult(false);
                    }
                });
            }
            var requestOptions = RequestNameOptions.None;
            if (options.HasFlag(ServiceRegistrationOptions.ReplaceExisting))
                requestOptions |= RequestNameOptions.ReplaceExisting;
            if (options.HasFlag(ServiceRegistrationOptions.AllowReplacement))
                requestOptions |= RequestNameOptions.AllowReplacement;

            RequestNameReply reply;
            try
            {
                reply = await connection.DBus.RequestName(serviceName, requestOptions);
            }
            catch (Exception)
            {
                if (acquireDisposer != null)
                    acquireDisposer.Dispose();
                if (lostDisposer != null)
                    lostDisposer.Dispose();
                throw;
            }
            switch (reply)
            {
                case RequestNameReply.AlreadyOwner:
                case RequestNameReply.PrimaryOwner:
                    return;
                case RequestNameReply.Exists:
                case RequestNameReply.InQueue:
                    if (cancellationToken.CanBeCanceled)
                        cancellationToken.Register(() => tcs.TrySetCanceled());
                    if (await tcs.Task)
                        return;
                    throw new InvalidOperationException("Name could not be acquired");
            }
        }


        public static Task ConnectAsync(this IConnection connection, CancellationToken cancellationToken)
        {
            return connection.ConnectAsync(null, cancellationToken);
        }

        public static T CreateProxy<T>(this IConnection connection, string service, ObjectPath path)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));
            return connection.BaseDBusConnection.ProxyProvider.GetInstance<T>(path, service);
        }

        static DBusInterfaceAttribute FindInterfaceAttribute(Type type)
        {
            var interfaceAttribute = type.GetCustomAttribute<DBusInterfaceAttribute>(true);
            if (interfaceAttribute == null)
            {
                // Try find on implementing interfaces
                var interfaces = type.GetInterfaces();
                foreach (var ifce in interfaces)
                {
                    interfaceAttribute = ifce.GetCustomAttribute<DBusInterfaceAttribute>(true);
                    if (interfaceAttribute != null)
                        break;
                }
            }
            return interfaceAttribute;
        }

        public static void RegisterObject(this IConnection connection, object instance, ObjectPath? path, MemberExposure exposure = MemberExposure.OnlyDBusInterfaces)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));
            var t = instance.GetType();
            var interfaceAttribute = FindInterfaceAttribute(t);
            if (interfaceAttribute == null)
                throw new ArgumentException($"{nameof(DBusInterfaceAttribute)} missing for object instance. Either add it or register object specifying interface name");
            connection.RegisterObject(path, interfaceAttribute.Name, instance, exposure);
        }

        public static void RegisterObject<T>(this IConnection connection, T instance, MemberExposure exposure = MemberExposure.OnlyDBusInterfaces) where T : IDBusObject
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));
            var t = instance.GetType();
            var interfaceAttribute = FindInterfaceAttribute(t);
            if (interfaceAttribute == null)
                throw new ArgumentException($"{nameof(DBusInterfaceAttribute)} missing for object instance. Either add it or register object specifying interface name");
            connection.RegisterObject(instance.ObjectPath, interfaceAttribute.Name, instance, exposure);
        }

        public static void UnregisterObject(this IConnection connection, ObjectPath? path, object instance)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));
            var t = instance.GetType();
            var interfaceAttribute = FindInterfaceAttribute(t);
            if (interfaceAttribute == null)
                throw new ArgumentException($"{nameof(DBusInterfaceAttribute)} missing for object instance.");
            connection.UnregisterObject(path, interfaceAttribute.Name);
        }
    }
}
