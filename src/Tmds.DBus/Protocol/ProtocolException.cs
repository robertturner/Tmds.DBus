// Copyright 2016 Tom Deseyn <tom.deseyn@gmail.com>
// This software is made available under the MIT License
// See COPYING for details

using System;

namespace Tmds.DBus.Protocol
{
    public class ProtocolException : Exception
    {
        public ProtocolException()
        {}

        public ProtocolException(string message) : base(message)
        {}

        public ProtocolException(string message, Exception innerException) : base(message, innerException)
        {}
    }
}
