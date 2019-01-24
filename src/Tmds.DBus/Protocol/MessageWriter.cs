// Copyright 2006 Alp Toker <alp@atoker.com>
// Copyright 2016 Tom Deseyn <tom.deseyn@gmail.com>
// This software is made available under the MIT License
// See COPYING for details

#pragma warning disable 0618 // 'Marshal.SizeOf(Type)' is obsolete

using System;
using System.Text;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Linq;
using Tmds.DBus.Objects;
using BaseLibs.Types;

namespace Tmds.DBus.Protocol
{
    public sealed class MessageWriter
    {
        readonly EndianFlag endianness;
        readonly MemoryStream stream;
        bool _skipNextStructPadding;

        static readonly Encoding stringEncoding = Encoding.UTF8;

        //a default constructor is a bad idea for now as we want to make sure the header and content-type match
        public MessageWriter() : this(Environment.NativeEndianness) {}

        public MessageWriter(EndianFlag endianness)
        {
            this.endianness = endianness;
            stream = new MemoryStream ();
        }

        public void SetSkipNextStructPadding()
        {
            _skipNextStructPadding = true;
        }

        public byte[] ToArray()
        {
            return stream.ToArray();
        }

        public void CloseWrite()
        {
            WritePad(8);
        }

        delegate void WriteHandler<T>(MessageWriter writer, T val);
        delegate void WriteHandler(MessageWriter writer, object val);

        static readonly MethodInfo s_messageWriterWriteDict = typeof(MessageWriter).GetMethod(nameof(MessageWriter.WriteFromDict));
        static readonly MethodInfo s_messageWriterWriteArray = typeof(MessageWriter).GetMethod(nameof(MessageWriter.WriteArray));
        static readonly MethodInfo s_messageWriterWriteDictionaryObject = typeof(MessageWriter).GetMethod(nameof(MessageWriter.WriteDictionaryObject));
        static readonly MethodInfo s_messageWriterWriteStruct = typeof(MessageWriter).GetMethod(nameof(MessageWriter.WriteStructure));

        static Type EnsureWriterForType(Type type)
        {
            if (type.GetTypeInfo().IsEnum)
                type = Enum.GetUnderlyingType(type);

            void AddTypeHandler(MethodInfo methodInfo)
            {
                var d = methodInfo.CreateCustomDelegate<WriteHandler>();
                objHandlers[type] = d;
                genHandlersCache[type] = methodInfo.CreateDelegate(typeof(WriteHandler<>).MakeGenericType(type));
            }

            if (objHandlers.TryGetValue(type, out WriteHandler handler))
                return type;

            if (ArgTypeInspector.IsDBusObjectType(type))
                return typeof(IDBusObject);

            var enumerableType = ArgTypeInspector.InspectEnumerableType(type, out Type elementType);
            if (enumerableType != ArgTypeInspector.EnumerableType.NotEnumerable)
            {
                if ((enumerableType == ArgTypeInspector.EnumerableType.EnumerableKeyValuePair) ||
                    (enumerableType == ArgTypeInspector.EnumerableType.GenericDictionary))
                    AddTypeHandler(s_messageWriterWriteDict.MakeGenericMethod(elementType.GenericTypeArguments));
                else if (enumerableType == ArgTypeInspector.EnumerableType.AttributeDictionary)
                    AddTypeHandler(s_messageWriterWriteDictionaryObject.MakeGenericMethod(type));
                else // Enumerable
                    AddTypeHandler(s_messageWriterWriteArray.MakeGenericMethod(new[] { elementType }));
                return type;
            }
            if (ArgTypeInspector.IsStructType(type))
            {
                AddTypeHandler(s_messageWriterWriteStruct.MakeGenericMethod(type));
                return type;
            }

            throw new ArgumentException($"Cannot (de)serialize Type '{type.FullName}'");
        }

        static WriteHandler WriterForType(Type type)
        {
            lock (handlersCacheLock)
                return objHandlers[EnsureWriterForType(type)];
        }
        static WriteHandler<T> WriterForType<T>()
        {
            lock (handlersCacheLock)
                return (WriteHandler<T>)genHandlersCache[EnsureWriterForType(typeof(T))];
        }

        static readonly object handlersCacheLock = new object();
        static readonly Dictionary<Type, WriteHandler> objHandlers = new Dictionary<Type, WriteHandler>
        {
            { typeof(bool), (w, v) => w.WriteBoolean((bool)v) },
            { typeof(byte), (w, v) => w.WriteByte((byte)v) },
            { typeof(Int16), (w, v) => w.WriteInt16((Int16)v) },
            { typeof(UInt16), (w, v) => w.WriteUInt16((UInt16)v) },
            { typeof(Int32), (w, v) => w.WriteInt32((Int32)v) },
            { typeof(UInt32), (w, v) => w.WriteUInt32((UInt32)v) },
            { typeof(Int64), (w, v) => w.WriteInt64((Int64)v) },
            { typeof(UInt64), (w, v) => w.WriteUInt64((UInt64)v) },
            { typeof(float), (w, v) => w.WriteSingle((float)v) },
            { typeof(double), (w, v) => w.WriteDouble((double)v) },
            { typeof(ObjectPath), (w, v) => w.WriteObjectPath((ObjectPath)v) },
            { typeof(Signature), (w, v) => w.WriteSignature((Signature)v) },
            { typeof(string), (w, v) => w.WriteString((string)v) },
            { typeof(object), (w, v) => w.WriteVariant(v) },
            { typeof(IDBusObject), (w, v) => w.WriteBusObject((IDBusObject)v) }
        };
        static readonly Dictionary<Type, object> genHandlersCache = new Dictionary<Type, object>
        {
            { typeof(bool), new WriteHandler<bool>((w, v) => w.WriteBoolean(v)) },
            { typeof(byte), new WriteHandler<byte>((w, v) => w.WriteByte(v)) },
            { typeof(Int16), new WriteHandler<Int16>((w, v) => w.WriteInt16(v)) },
            { typeof(UInt16), new WriteHandler<UInt16>((w, v) => w.WriteUInt16(v)) },
            { typeof(Int32), new WriteHandler<Int32>((w, v) => w.WriteInt32(v)) },
            { typeof(UInt32), new WriteHandler<UInt32>((w, v) => w.WriteUInt32(v)) },
            { typeof(Int64), new WriteHandler<Int64>((w, v) => w.WriteInt64(v)) },
            { typeof(UInt64), new WriteHandler<UInt64>((w, v) => w.WriteUInt64(v)) },
            { typeof(float), new WriteHandler<float>((w, v) => w.WriteSingle(v)) },
            { typeof(double), new WriteHandler<double>((w, v) => w.WriteDouble(v)) },
            { typeof(ObjectPath), new WriteHandler<ObjectPath>((w, v) => w.WriteObjectPath(v)) },
            { typeof(Signature), new WriteHandler<Signature>((w, v) => w.WriteSignature(v)) },
            { typeof(string), new WriteHandler<string>((w, v) => w.WriteString(v)) },
            { typeof(object), new WriteHandler<object>((w, v) => w.WriteVariant(v)) },
            { typeof(IDBusObject), new WriteHandler<IDBusObject>((w, v) => w.WriteBusObject(v)) }
        };

        public void Write(Type type, object val)
        {
            var m = WriterForType(type);
            m(this, val);
        }

        #region Writers

        public void WriteByte(byte val)
        {
            stream.WriteByte(val);
        }

        public void WriteBoolean(bool val)
        {
            WriteUInt32((uint)(val ? 1 : 0));
        }

        public void WriteInt16(short val)
        {
            MarshalTo(val, BitConverter.GetBytes);
        }

        public void WriteUInt16 (ushort val)
        {
            MarshalTo(val, BitConverter.GetBytes);
        }

        public void WriteInt32 (int val)
        {
            MarshalTo(val, BitConverter.GetBytes);
        }

        public void WriteUInt32 (uint val)
        {
            MarshalTo(val, BitConverter.GetBytes);
        }

        public void WriteInt64 (long val)
        {
            MarshalTo(val, BitConverter.GetBytes);
        }

        public void WriteUInt64 (ulong val)
        {
            MarshalTo(val, BitConverter.GetBytes);
        }

        public void WriteSingle (float val)
        {
            MarshalTo(val, BitConverter.GetBytes);
        }

        public void WriteDouble (double val)
        {
            MarshalTo(val, BitConverter.GetBytes);
        }

        public void WriteString (string val)
        {
            byte[] utf8_data = stringEncoding.GetBytes (val);
            WriteUInt32 ((uint)utf8_data.Length);
            stream.Write (utf8_data, 0, utf8_data.Length);
            WriteNull ();
        }

        public void WriteObjectPath (ObjectPath val)
        {
            WriteString (val.Value);
        }

        public void WriteSignature(Signature val)
        {
            byte[] ascii_data = val.GetBuffer ();

            if (ascii_data.Length > ProtocolInformation.MaxSignatureLength)
                throw new ProtocolException("Signature length " + ascii_data.Length + " exceeds maximum allowed " + ProtocolInformation.MaxSignatureLength + " bytes");

            WriteByte ((byte)ascii_data.Length);
            stream.Write (ascii_data, 0, ascii_data.Length);
            WriteNull ();
        }

        void MarshalTo<T>(T val, Func<T, byte[]> converter)
         where T : struct
        {
            int count = Marshal.SizeOf<T>();
            WritePad(count);
            var bytes = converter(val);
            if (endianness != Environment.NativeEndianness)
                Array.Reverse(bytes);
            stream.Write(bytes, 0, count);
        }

        public void WriteObject(Type type, object val)
        {
            throw new NotImplementedException();
#if false
            ObjectPath path;

            DBusObjectProxy bobj = val as DBusObjectProxy;

            if (bobj == null)
                throw new ArgumentException("No object reference to write", nameof(val));

            path = bobj.ObjectPath;

            WriteObjectPath (path);
#endif
        }

        public void WriteBusObject(IDBusObject busObject)
        {
            WriteObjectPath(busObject.ObjectPath);
        }

        public void WriteVariant(object val)
        {
            if (val == null)
            {
                //throw new NotSupportedException("Cannot send null variant");
                val = new object[0];
            }

            var type = val.GetType();
            if (type == typeof(object))
                throw new ArgumentException($"Cannot (de)serialize Type '{type.FullName}'");

            var sig = Signature.GetSig(type);
            WriteSignature(sig);
            Write(type, val);
        }

        public void WriteArray<T>(IEnumerable<T> val)
        {
            var elemType = typeof(T);

            var byteArray = val as byte[];
            if (byteArray != null)
            {
                int valLength = val.Count();
                if (byteArray.Length > ProtocolInformation.MaxArrayLength)
                    ThrowArrayLengthException((uint)byteArray.Length);

                WriteUInt32((uint)byteArray.Length);
                stream.Write(byteArray, 0, byteArray.Length);
                return;
            }

            if (elemType.GetTypeInfo().IsEnum)
                elemType = Enum.GetUnderlyingType(elemType);

            Signature sigElem = Signature.GetSig(elemType);
            int fixedSize = 0;

            if (endianness == Environment.NativeEndianness && elemType.GetTypeInfo().IsValueType && !sigElem.IsStruct && elemType != typeof(bool) &&
                sigElem.GetFixedSize(ref fixedSize) && val is Array)
            {
                var array = val as Array;
                int byteLength = fixedSize * array.Length;
                if (byteLength > ProtocolInformation.MaxArrayLength)
                    ThrowArrayLengthException((uint)byteLength);

                WriteUInt32((uint)byteLength);
                WritePad(sigElem.Alignment);

                byte[] data = new byte[byteLength];
                Buffer.BlockCopy(array, 0, data, 0, data.Length);
                stream.Write(data, 0, data.Length);

                return;
            }

            long origPos = stream.Position;
            WriteUInt32((uint)0);

            WritePad(sigElem.Alignment);

            long startPos = stream.Position;

            var tWriter = WriterForType<T>();

            foreach (T elem in val)
                tWriter(this, elem);

            long endPos = stream.Position;
            uint ln = (uint)(endPos - startPos);
            stream.Position = origPos;

            if (ln > ProtocolInformation.MaxArrayLength)
                ThrowArrayLengthException(ln);

            WriteUInt32(ln);
            stream.Position = endPos;
        }

        public void WriteStructure<T>(T value)
        {
            FieldInfo[] fis = ArgTypeInspector.GetStructFields(typeof(T));

            if (fis.Length == 0)
                return;

            if (!_skipNextStructPadding)
            {
                WritePad(8);
            }
            _skipNextStructPadding = false;

            object boxed = value;

            if (MessageReader.IsEligibleStruct(typeof(T), fis))
            {
                byte[] buffer = new byte[Marshal.SizeOf(fis[0].FieldType) * fis.Length];

                //unsafe {
                GCHandle valueHandle = GCHandle.Alloc(boxed, GCHandleType.Pinned);
                Marshal.Copy(valueHandle.AddrOfPinnedObject(), buffer, 0, buffer.Length);
                valueHandle.Free();
                //}
                stream.Write(buffer, 0, buffer.Length);
                return;
            }

            foreach (var fi in fis)
                Write(fi.FieldType, fi.GetValue(boxed));
        }

        public void WriteFromDict<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>> val)
        {
            long origPos = stream.Position;
            // Pre-write array length field, we overwrite it at the end with the correct value
            WriteUInt32((uint)0);
            WritePad(8);
            long startPos = stream.Position;

            var keyWriter = WriterForType<TKey>();
            var valueWriter = WriterForType<TValue>();

            foreach (KeyValuePair<TKey, TValue> entry in val)
            {
                WritePad(8);
                keyWriter(this, entry.Key);
                valueWriter(this, entry.Value);
            }

            long endPos = stream.Position;
            uint ln = (uint)(endPos - startPos);
            stream.Position = origPos;

            if (ln > ProtocolInformation.MaxArrayLength)
                throw new ProtocolException("Dict length " + ln + " exceeds maximum allowed " + ProtocolInformation.MaxArrayLength + " bytes");

            WriteUInt32(ln);
            stream.Position = endPos;
        }

        public void WriteDictionaryObject<T>(T val)
        {
            var type = typeof(T);
            FieldInfo[] fis = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            long origPos = stream.Position;
            // Pre-write array length field, we overwrite it at the end with the correct value
            WriteUInt32((uint)0);
            WritePad(8);
            long startPos = stream.Position;

            foreach (var fi in fis)
            {
                object fieldVal = fi.GetValue(val);
                if (fieldVal == null)
                    continue;

                PropertyTypeInspector.InspectField(fi, out string fieldName, out Type fieldType);
                Signature sig = Signature.GetSig(fieldType);

                WritePad(8);
                WriteString(fieldName);
                WriteSignature(sig);
                Write(fieldType, fieldVal);
            }

            long endPos = stream.Position;
            uint ln = (uint)(endPos - startPos);
            stream.Position = origPos;

            if (ln > ProtocolInformation.MaxArrayLength)
                throw new ProtocolException("Dict length " + ln + " exceeds maximum allowed " + ProtocolInformation.MaxArrayLength + " bytes");

            WriteUInt32(ln);
            stream.Position = endPos;
        }

        public void WriteHeader(Header header)
        {
            WriteByte((byte)header.Endianness);
            WriteByte((byte)header.MessageType);
            WriteByte((byte)header.Flags);
            WriteByte(header.MajorVersion);
            WriteUInt32(header.Length);
            WriteUInt32(header.Serial);
            WriteHeaderFields(header.GetFields());
            CloseWrite();
        }

        internal void WriteHeaderFields(IEnumerable<KeyValuePair<FieldCode, object>> val)
        {
            long origPos = stream.Position;
            WriteUInt32((uint)0);

            WritePad(8);

            long startPos = stream.Position;

            foreach (KeyValuePair<FieldCode, object> entry in val)
            {
                WritePad(8);
                WriteByte((byte)entry.Key);
                switch (entry.Key)
                {
                    case FieldCode.Destination:
                    case FieldCode.ErrorName:
                    case FieldCode.Interface:
                    case FieldCode.Member:
                    case FieldCode.Sender:
                        WriteSignature(Signature.StringSig);
                        WriteString((string)entry.Value);
                        break;
                    case FieldCode.Path:
                        WriteSignature(Signature.ObjectPathSig);
                        WriteObjectPath((ObjectPath)entry.Value);
                        break;
                    case FieldCode.ReplySerial:
                        WriteSignature(Signature.UInt32Sig);
                        WriteUInt32((uint)entry.Value);
                        break;
                    case FieldCode.Signature:
                        WriteSignature(Signature.SignatureSig);
                        Signature sig = (Signature)entry.Value;
                        WriteSignature((Signature)entry.Value);
                        break;
                    default:
                        WriteVariant(entry.Value);
                        break;
                }
            }

            long endPos = stream.Position;
            uint ln = (uint)(endPos - startPos);
            stream.Position = origPos;

            if (ln > ProtocolInformation.MaxArrayLength)
                throw new ProtocolException("Dict length " + ln + " exceeds maximum allowed " + ProtocolInformation.MaxArrayLength + " bytes");

            WriteUInt32(ln);
            stream.Position = endPos;
        }

        private void WriteNull()
        {
            stream.WriteByte(0);
        }

        // Source buffer for zero-padding
        static readonly byte[] nullBytes = new byte[8];
        private void WritePad(int alignment)
        {
            int needed = ProtocolInformation.PadNeeded((int)stream.Position, alignment);
            if (needed == 0)
                return;
            stream.Write(nullBytes, 0, needed);
        }
#endregion

        internal static void ThrowArrayLengthException(uint ln)
        {
            throw new ProtocolException("Array length " + ln.ToString () + " exceeds maximum allowed " + ProtocolInformation.MaxArrayLength + " bytes");
        }

        
    }
}
