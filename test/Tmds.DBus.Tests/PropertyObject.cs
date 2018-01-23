using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Tmds.DBus.Tests
{
    public class PropertyObject : IPropertyObject
    {
        public static readonly ObjectPath Path = new ObjectPath("/tmds/dbus/tests/propertyobject");
        private IDictionary<string, object> _properties;
        public PropertyObject(IDictionary<string, object> properties)
        {
            _properties = properties;
        }
        public ObjectPath ObjectPath => Path;

        Action<(string name, object value)> propChangeCallback;

        public Task<IDictionary<string, object>> GetAll()
        {
            return Task.FromResult(_properties);
        }

        public Task<object> Get(string prop)
        {
            return Task.FromResult(_properties[prop]);
        }

        public Task Set(string prop, object val)
        {
            _properties[prop] = val;
            propChangeCallback?.Invoke((prop, val));
            return Task.CompletedTask;
        }

        public Task<IDisposable> WatchProperties(Action<(string name, object value)> handler)
        {
            propChangeCallback = handler;
            return null;
        }
    }
}