// Copyright 2016 Tom Deseyn <tom.deseyn@gmail.com>
// This software is made available under the MIT License
// See COPYING for details

using System;

namespace Tmds.DBus.Objects
{
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class DBusInterfaceAttribute : Attribute
    {
        public readonly string Name;

        public DBusInterfaceAttribute(string name)
        {
            Name = name;
        }
    }
}
