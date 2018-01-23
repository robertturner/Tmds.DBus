﻿// Copyright 2016 Tom Deseyn <tom.deseyn@gmail.com>
// This software is made available under the MIT License
// See COPYING for details

using System.Text;
using AccessTypes = Tmds.DBus.Objects.InterfaceObjDef.PropertyDef.AccessTypes;

namespace Tmds.DBus.Protocol
{
    internal class IntrospectionWriter
    {
        private StringBuilder _sb = new StringBuilder();
        public void WriteDocType()
        {
            _sb.Append("<!DOCTYPE node PUBLIC \"-//freedesktop//DTD D-BUS Object Introspection 1.0//EN\"\n");
            _sb.Append("\"http://www.freedesktop.org/standards/dbus/1.0/introspect.dtd\">\n");
        }

        public void WriteInterfaceStart(string name)
        {
            _sb.AppendFormat("  <interface name=\"{0}\">\n", name);
        }

        public void WriteInterfaceEnd()
        {
            _sb.Append("  </interface>\n");
        }

        public void WriteMethodStart(string name)
        {
            _sb.AppendFormat("    <method name=\"{0}\">\n", name);
        }

        public void WriteMethodEnd()
        {
            _sb.Append("    </method>\n");
        }

        public void WriteInArg(string name, Signature signature)
        {
            _sb.AppendFormat("      <arg direction=\"in\" name=\"{0}\" type=\"{1}\"/>\n", name, signature);
        }

        public void WriteOutArg(string name, Signature signature)
        {
            _sb.AppendFormat("      <arg direction=\"out\" name=\"{0}\" type=\"{1}\"/>\n", name, signature);
        }

        public void WriteSignalStart(string name)
        {
            _sb.AppendFormat("    <signal name=\"{0}\">\n", name);
        }

        public void WriteSignalEnd()
        {
            _sb.Append("    </signal>\n");
        }

        public void WriteProperty(string name, Signature signature, AccessTypes access)
        {
            string propAccess;
            switch (access)
            {
                case AccessTypes.Read:
                    propAccess = "read";
                    break;
                case AccessTypes.Write:
                    propAccess = "write";
                    break;
                case AccessTypes.ReadWrite:
                    propAccess = "readwrite";
                    break;
                default:
                    _sb.AppendFormat("    <property name=\"{0}\" type=\"{1}\"/>\n", name, signature);
                    return;
            }
            _sb.AppendFormat("    <property name=\"{0}\" type=\"{1}\" access=\"{2}\"/>\n", name, signature, propAccess);
        }

        public void WriteArg(string name, Signature signature)
        {
            _sb.AppendFormat("      <arg name=\"{0}\" type=\"{1}\"/>\n", name, signature);
        }

        public void WriteNodeStart(string name)
        {
            _sb.AppendFormat("<node name=\"{0}\">\n", name);
        }

        public void WriteNodeEnd()
        {
            _sb.Append("</node>\n");
        }

        public void WriteLiteral(string value)
        {
            _sb.Append(value);
        }

        public void WriteChildNode(string name)
        {
            _sb.AppendFormat("  <node name=\"{0}\"/>\n", name);
        }

        public override string ToString()
        {
            return _sb.ToString();
        }
    }
}
