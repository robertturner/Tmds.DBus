// Copyright 2006 Alp Toker <alp@atoker.com>
// Copyright 2010 Alan McGovern <alan.mcgovern@gmail.com>
// Copyright 2016 Tom Deseyn <tom.deseyn@gmail.com>
// This software is made available under the MIT License
// See COPYING for details

using System;
using Tmds.DBus.Protocol;
using BaseLibs.Enums;

namespace Tmds.DBus
{
    public class DBusException : Exception
    {
        public DBusException(DBusErrors error, string errorMessage)
            : base($"{error.GetDescription(): errorMessage}")
        {
            WellknownError = error;
            ErrorName = error.GetDescription();
            ErrorMessage = errorMessage;
        }
        public DBusException(string errorName, string errorMessage) 
            : base($"{errorName: errorMessage}")
        {
            WellknownError = Enum_Extensions.TryParseFromDescription<DBusErrors>(errorName);
            ErrorName = errorName;
            ErrorMessage = errorMessage;
        }

        public DBusErrors? WellknownError { get; }

        public string ErrorName { get; }

        public string ErrorMessage { get; }
    }
}
