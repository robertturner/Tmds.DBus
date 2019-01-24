// Copyright 2006 Alp Toker <alp@atoker.com>
// Copyright 2010 Alan McGovern <alan.mcgovern@gmail.com>
// This software is made available under the MIT License
// See COPYING for details

using System;
using System.Collections.Generic;
using System.Linq;

namespace Tmds.DBus
{
    public struct ObjectPath : IComparable, IComparable<ObjectPath>, IEquatable<ObjectPath>
    {
        public static readonly ObjectPath Root = new ObjectPath("/");

        public readonly string Value;

        public ObjectPath(string value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            value = value.Trim();
            Validate(value);
            this.Value = value;
        }
        public ObjectPath(IEnumerable<string> parts, bool isAbsolute = true)
        {
            if (parts == null)
                throw new ArgumentNullException(nameof(parts));
            var partsList = new List<string>(parts);
            for (int i = 0; i < partsList.Count; ++i)
            {
                partsList[i] = partsList[i].Trim();
                var subPart = '/' + partsList[i];
                Validate(partsList[i]);
                if (new ObjectPath(subPart).Decomposed.Length > 1)
                    throw new ArgumentException("One of the parts has multiple subparts");
            }
            var value = string.Join("/", partsList);
            if (isAbsolute)
                value = '/' + value;
            Validate(value);
            Value = value;
        }

        private ObjectPath(string value, int _dummyForNoValidate) { Value = value.Trim(); }

        public static ObjectPath? Parse(string value)
        {
            return !Validate(value, throwIfInvalid: false) ? null : (ObjectPath?)new ObjectPath(value, 0);
        }

        public bool IsAbsolute => Value.StartsWith("/");

        public static bool Validate(string value, bool throwIfInvalid = true)
        {
            bool handler(string message)
            {
                if (throwIfInvalid)
                    throw new ArgumentException(message);
                return false;
            }
            /*if (!value.StartsWith("/", StringComparison.Ordinal))
                return handler("value");*/
            if (value.EndsWith("/", StringComparison.Ordinal) && value.Length > 1)
                return handler("ObjectPath cannot end in '/'");

            bool multipleSlash = false;

            foreach (char c in value)
            {
                bool valid = (c >= 'a' && c <= 'z')
                    || (c >= 'A' && c <= 'Z')
                    || (c >= '0' && c <= '9')
                    || c == '_'
                    || (!multipleSlash && c == '/');

                if (!valid)
                {
                    if (throwIfInvalid)
                        throw new ArgumentException($"'{c}' is not a valid character in an ObjectPath", "value");
                    else
                        return false;
                }
                multipleSlash = c == '/';
            }
            return true;
        }

        public int CompareTo(ObjectPath other)
        {
            return Value.CompareTo(other.Value);
        }

        public int CompareTo(object otherObject)
        {
            var other = otherObject as ObjectPath?;

            if (other == null)
                return 1;

            return Value.CompareTo(other.Value.Value);
        }

        public bool Equals(ObjectPath other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object o)
        {
            var b = o as ObjectPath?;

            if (b == null)
                return false;

            return Value.Equals(b.Value.Value);
        }

        public static bool operator ==(ObjectPath a, ObjectPath b)
        {
            return a.Value == b.Value;
        }

        public static bool operator !=(ObjectPath a, ObjectPath b)
        {
            return !(a == b);
        }

        public override int GetHashCode()
        {
            if (Value == null)
            {
                return 0;
            }
            return Value.GetHashCode();
        }

        public override string ToString()
        {
            return Value;
        }

        public static implicit operator ObjectPath(string value)
        {
            return new ObjectPath(value);
        }

        //this may or may not prove useful
        public string[] Decomposed
        {
            get
            {
                return Value.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            }
        }

        internal ObjectPath Parent
        {
            get
            {
                if (Value == Root.Value)
                    return null;

                string par = Value.Substring(0, Value.LastIndexOf('/'));
                if (par == String.Empty)
                    par = "/";

                return new ObjectPath(par);
            }
        }
    }
}
