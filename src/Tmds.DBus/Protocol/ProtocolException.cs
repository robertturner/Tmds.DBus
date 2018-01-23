// Copyright 2016 Tom Deseyn <tom.deseyn@gmail.com>
// This software is made available under the MIT License
// See COPYING for details

using System;

namespace Tmds.DBus
{
    public class ProtocolException : Exception
    {
        internal ProtocolException()
        {}

        internal ProtocolException(string message) : base(message)
        {}

        internal ProtocolException(string message, Exception innerException) : base(message, innerException)
        {}
    }
}
