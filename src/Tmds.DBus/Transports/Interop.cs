// Copyright 2016 Tom Deseyn <tom.deseyn@gmail.com>
// This software is made available under the MIT License
// See COPYING for details

using System;
using System.Runtime.InteropServices;

namespace Tmds.DBus.Transports
{
    using SizeT = System.UIntPtr;
    using SSizeT = System.IntPtr;
    internal static class Interop
    {        
        [DllImport("libc.so.6")]
        internal static extern uint geteuid();
        [DllImport("libc.so.6")]
        internal static extern uint getuid();
#if false
        public struct Passwd
        {
            public IntPtr Name;
            public IntPtr Password;
            public uint UserID;
            public uint GroupID;
            public IntPtr UserInfo;
            public IntPtr HomeDir;
            public IntPtr Shell;
        }
        [DllImport("libc.so.6")]
        internal static extern unsafe int getpwuid_r(uint uid, out Passwd pwd, byte* buf, int bufLen, out IntPtr result);
#endif
        [DllImport ("libc.so.6", SetLastError=true)]
        public static extern SSizeT sendmsg(int sockfd, IntPtr msg, int flags);
        [DllImport ("libc.so.6", SetLastError=true)]
        public static extern SSizeT recvmsg(int sockfd, IntPtr msg, int flags);
        
        [DllImport("libX11")]
        internal static extern IntPtr XOpenDisplay (string name);
        [DllImport("libX11")]
        internal static extern int XCloseDisplay (IntPtr display);
        [DllImport("libX11")]
        internal static extern IntPtr XInternAtom (IntPtr display, string atom_name, bool only_if_exists);
        [DllImport("libX11")]
        internal static extern int XGetWindowProperty(IntPtr display, IntPtr w, IntPtr property, 
            int long_offset, int long_length, bool delete, IntPtr req_type, 
            out IntPtr actual_type_return, out IntPtr actual_format_return, 
            out IntPtr nitems_return, out IntPtr bytes_after_return, out IntPtr prop_return);
        [DllImport("libX11")]
        internal static extern int XFree(IntPtr data);
        [DllImport("libX11")]
        internal static extern IntPtr XGetSelectionOwner(IntPtr display, IntPtr Atom);
    }
}
