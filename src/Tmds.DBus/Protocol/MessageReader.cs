// Copyright 2006 Alp Toker <alp@atoker.com>
// Copyright 2016 Tom Deseyn <tom.deseyn@gmail.com>
// This software is made available under the MIT License
// See COPYING for details

#pragma warning disable 0618 // 'Marshal.SizeOf(Type)' is obsolete

using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using BaseLibs.Types;
using BaseLibs.Collections;

namespace Tmds.DBus.Protocol
{
    public sealed class MessageReader
    {
        private readonly EndianFlag _endianness;
        private readonly ArraySegment<byte> _data;

        private int _pos = 0;
        private bool _skipNextStructPadding = false;

        static readonly Dictionary<Type, bool> s_isPrimitiveStruct = new Dictionary<Type, bool> ();

        public MessageReader(EndianFlag endianness, ArraySegment<byte> data)
        {
            _endianness = endianness;
            _data = data;
        }
        public MessageReader(Message message) 
            : this(message.Header.Endianness, new ArraySegment<byte>(message.Body ?? Array.Empty<byte>()))
        { }

        public void SetSkipNextStructPadding()
        {
            _skipNextStructPadding = true;
        }

        public object Read(Type type)
        {
            var h = ReaderForType(type);
            return h(this);
        }

        public void Seek(int stride)
        {
            var check = _pos + stride;
            if (check < 0 || check > _data.Count)
                throw new ArgumentOutOfRangeException ("stride");
            _pos = check;
        }

        delegate T ReadHandler<T>(MessageReader reader);
        delegate object ReadHandler(MessageReader reader);

        static readonly Dictionary<Type, ReadHandler> objHandlers = new Dictionary<Type, ReadHandler>
        {
            { typeof(bool), r => r.ReadBoolean() },
            { typeof(byte), r => r.ReadByte() },
            { typeof(Int16), r => r.ReadInt16() },
            { typeof(UInt16), r => r.ReadUInt16() },
            { typeof(Int32), r => r.ReadInt32() },
            { typeof(UInt32), r => r.ReadUInt32() },
            { typeof(Int64), r => r.ReadInt64() },
            { typeof(UInt64), r => r.ReadUInt64() },
            { typeof(float), r => r.ReadSingle() },
            { typeof(double), r => r.ReadDouble() },
            { typeof(ObjectPath), r => r.ReadObjectPath() },
            { typeof(Signature), r => r.ReadSignature() },
            { typeof(string), r => r.ReadString() },
            { typeof(object), r => r.ReadVariant() },
            { typeof(IDBusObject), r => r.ReadBusObject() }
        };
        static readonly Dictionary<Type, object> genHandlersCache = new Dictionary<Type, object>
        {
            { typeof(bool), new ReadHandler<bool>(r => r.ReadBoolean()) },
            { typeof(byte), new ReadHandler<byte>(r => r.ReadByte()) },
            { typeof(Int16), new ReadHandler<Int16>(r => r.ReadInt16()) },
            { typeof(UInt16), new ReadHandler<UInt16>(r => r.ReadUInt16()) },
            { typeof(Int32), new ReadHandler<Int32>(r => r.ReadInt32()) },
            { typeof(UInt32), new ReadHandler<UInt32>(r => r.ReadUInt32()) },
            { typeof(Int64), new ReadHandler<Int64>(r => r.ReadInt64()) },
            { typeof(UInt64), new ReadHandler<UInt64>(r => r.ReadUInt64()) },
            { typeof(float), new ReadHandler<float>(r => r.ReadSingle()) },
            { typeof(double), new ReadHandler<double>(r => r.ReadDouble()) },
            { typeof(ObjectPath), new ReadHandler<ObjectPath>(r => r.ReadObjectPath()) },
            { typeof(Signature), new ReadHandler<Signature>(r => r.ReadSignature()) },
            { typeof(string), new ReadHandler<string>(r => r.ReadString()) },
            { typeof(object), new ReadHandler<object>(r => r.ReadVariant()) },
            { typeof(IDBusObject), new ReadHandler<IDBusObject>(r => r.ReadBusObject()) }
        };

        static readonly Dictionary<Type, (FieldInfo[] fis, Lazy<MemberSetter[]> getters)> structAccessorCache = new Dictionary<Type, (FieldInfo[] fields, Lazy<MemberSetter[]> getters)>();
        (FieldInfo[] fis, Lazy<MemberSetter[]> getters) GetStructFieldSetters(Type type)
        {
            return structAccessorCache.GetOrSet(type, () =>
            {
                FieldInfo[] fis = ArgTypeInspector.GetStructFields(type);
                return (fis, new Lazy<MemberSetter[]>(() => fis.Select(fi => fi.DelegateForSetField()).ToArray()));
            });
        }

        static void AddTypeHandler(Type type, MethodInfo methodInfo)
        {
            var d = methodInfo.CreateCustomDelegate<ReadHandler>();
            objHandlers[type] = d;
            genHandlersCache[type] = methodInfo.CreateDelegate(typeof(ReadHandler<>).MakeGenericType(type));
        }

        static ReadHandler<T> ReaderForType<T>()
        {
            return (ReadHandler<T>)genHandlersCache[EnsureReaderForType(typeof(T))];
        }
        static ReadHandler ReaderForType(Type type)
        {
            return objHandlers[EnsureReaderForType(type)];
        }

        static readonly MethodInfo s_messageReaderReadDictionary = typeof(MessageReader).GetMethod(nameof(MessageReader.ReadDictionary), Type.EmptyTypes);
        static readonly MethodInfo s_messageReaderReadDictionaryObject = typeof(MessageReader).GetMethod(nameof(MessageReader.ReadDictionaryObject), Type.EmptyTypes);
        static readonly MethodInfo s_messageReaderReadArray = typeof(MessageReader).GetMethod(nameof(MessageReader.ReadArray), Type.EmptyTypes);
        static readonly MethodInfo s_messageReaderReadStruct = typeof(MessageReader).GetMethod(nameof(MessageReader.ReadStruct), Type.EmptyTypes);

        static Type EnsureReaderForType(Type type)
        {
            if (type.GetTypeInfo().IsEnum)
                type = Enum.GetUnderlyingType(type);

            if (objHandlers.ContainsKey(type))
                return type;

            if (ArgTypeInspector.IsDBusObjectType(type))
                return typeof(IDBusObject);

            var enumerableType = ArgTypeInspector.InspectEnumerableType(type, out Type elementType);
            if (enumerableType != ArgTypeInspector.EnumerableType.NotEnumerable)
            {
                if (enumerableType == ArgTypeInspector.EnumerableType.GenericDictionary)
                {
                    AddTypeHandler(type, s_messageReaderReadDictionary.MakeGenericMethod(elementType.GenericTypeArguments));
                    return type;
                }
                else if (enumerableType == ArgTypeInspector.EnumerableType.AttributeDictionary)
                {
                    AddTypeHandler(type, s_messageReaderReadDictionaryObject.MakeGenericMethod(type));
                    return type;
                }
                else // Enumerable, EnumerableKeyValuePair
                {
                    AddTypeHandler(type, s_messageReaderReadArray.MakeGenericMethod(new[] { elementType }));
                    return type;
                }
            }

            if (ArgTypeInspector.IsStructType(type))
            {
                AddTypeHandler(type, s_messageReaderReadStruct.MakeGenericMethod(type));
                return type;
            }

            throw new ArgumentException($"Cannot (de)serialize Type '{type.FullName}'");
        }

        #region Readers
        static byte[] EndianSwap(byte[] src, int offset, int count)
        {
            var ret = new byte[count];
            Array.Copy(src, offset, ret, 0, count);
            Array.Reverse(ret);
            return ret;
        }

        T MarshalTo<T>(Func<byte[], int, T> converter)
            where T : struct
        {
            int count = Marshal.SizeOf<T>();
            ReadPad(count);
            if (_data.Count < _pos + count)
                throw new ProtocolException("Cannot read beyond end of data");
            T val = (_endianness == Environment.NativeEndianness) ? converter(_data.Array, _data.Offset + _pos) :
                converter(EndianSwap(_data.Array, _data.Offset + _pos, count), 0);
            _pos += count;
            return val;
        }

        public byte ReadByte()
        {
            return _data.Array[_data.Offset + _pos++];
        }

        public bool ReadBoolean()
        {
            uint intval = ReadUInt32();
            switch (intval)
            {
                case 0:
                    return false;
                case 1:
                    return true;
                default:
                    throw new ProtocolException("Read value " + intval + " at position " + _pos + " while expecting boolean (0/1)");
            }
        }

        public short ReadInt16() => MarshalTo(BitConverter.ToInt16);

        public ushort ReadUInt16() => MarshalTo(BitConverter.ToUInt16);

        public int ReadInt32() => MarshalTo(BitConverter.ToInt32);

        public uint ReadUInt32() => MarshalTo(BitConverter.ToUInt32);

        public long ReadInt64() => MarshalTo(BitConverter.ToInt64);

        public ulong ReadUInt64() => MarshalTo(BitConverter.ToUInt64);

        public float ReadSingle() => MarshalTo(BitConverter.ToSingle);

        public double ReadDouble() => MarshalTo(BitConverter.ToDouble);

        public string ReadString()
        {
            uint ln = ReadUInt32 ();
            string val = Encoding.UTF8.GetString(_data.Array, _data.Offset + _pos, (int)ln);
            _pos += (int)ln;
            ReadNull ();
            return val;
        }

        public void SkipString() => ReadString();

        public ObjectPath ReadObjectPath() => new ObjectPath(ReadString());

        public IDBusObject ReadBusObject() => new BusObject(ReadObjectPath());

        public Signature ReadSignature()
        {
            byte ln = ReadByte();

            // Avoid an array allocation for small signatures
            if (ln == 1)
            {
                DType dtype = (DType)ReadByte ();
                ReadNull ();
                return new Signature (dtype);
            }

            if (ln > ProtocolInformation.MaxSignatureLength)
                throw new ProtocolException("Signature length " + ln + " exceeds maximum allowed " + ProtocolInformation.MaxSignatureLength + " bytes");

            byte[] sigData = new byte[ln];
            Array.Copy (_data.Array, _data.Offset + _pos, sigData, 0, (int)ln);
            _pos += (int)ln;
            ReadNull ();

            return Signature.Take (sigData);
        }

        public object ReadVariant()
        {
            var sig = ReadSignature ();
            if (!sig.IsSingleCompleteType)
                throw new InvalidOperationException (string.Format ("ReadVariant need a single complete type signature, {0} was given", sig.ToString ()));
            return Read(sig.ToType());
        }

        public T ReadDictionaryObject<T>()
        {
            var type = typeof(T);
            FieldInfo[] fis = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            var val = Activator.CreateInstance(type);

            uint ln = ReadUInt32 ();
            if (ln > ProtocolInformation.MaxArrayLength)
                throw new ProtocolException("Dict length " + ln + " exceeds maximum allowed " + ProtocolInformation.MaxArrayLength + " bytes");

            ReadPad (8);

            int endPos = _pos + (int)ln;

            while (_pos < endPos)
            {
                ReadPad (8);

                var key = ReadString();
                var sig = ReadSignature();

                if (!sig.IsSingleCompleteType)
                    throw new InvalidOperationException (string.Format ("ReadVariant need a single complete type signature, {0} was given", sig.ToString ()));

                var field = fis.Where(f => f.Name.EndsWith(key, StringComparison.Ordinal) &&
                                            ((f.Name.Length == key.Length) ||
                                             (f.Name.Length == key.Length + 1 && f.Name[0] == '_'))).SingleOrDefault();

                if (field == null)
                {
                    var value = Read(sig.ToType());
                }
                else
                {
                    PropertyTypeInspector.InspectField(field, out string propertyName, out Type fieldType);
                    if (sig != Signature.GetSig(fieldType))
                        throw new ArgumentException($"Dictionary '{type.FullName}' field '{field.Name}' with type '{fieldType.FullName}' cannot be read from D-Bus type '{sig}'");
                    var readValue = Read(fieldType);
                    field.SetValue(val, readValue);
                }
            }

            if (_pos != endPos)
                throw new ProtocolException("Read pos " + _pos + " != ep " + endPos);
            return (T)val;
        }

        public Dictionary<TKey, TValue> ReadDictionary<TKey, TValue>()
        {
            uint ln = ReadUInt32 ();

            if (ln > ProtocolInformation.MaxArrayLength)
                throw new ProtocolException("Dict length " + ln + " exceeds maximum allowed " + ProtocolInformation.MaxArrayLength + " bytes");

            var val = new Dictionary<TKey, TValue> ((int)(ln / 8));
            ReadPad (8);

            int endPos = _pos + (int)ln;

            var keyReader = ReaderForType<TKey>();
            var valueReader = ReaderForType<TValue>();

            while (_pos < endPos)
            {
                ReadPad (8);
                TKey k = keyReader(this);
                TValue v = valueReader(this);
                val.Add(k, v);
            }

            if (_pos != endPos)
                throw new ProtocolException("Read pos " + _pos + " != ep " + endPos);

            return val;
        }

        public T[] ReadArray<T>()
        {
            uint ln = ReadUInt32 ();
            Type elemType = typeof(T);

            if (ln > ProtocolInformation.MaxArrayLength)
                throw new ProtocolException("Array length " + ln + " exceeds maximum allowed " + ProtocolInformation.MaxArrayLength + " bytes");

            //advance to the alignment of the element
            ReadPad (ProtocolInformation.GetAlignment (elemType));

            if (elemType.GetTypeInfo().IsPrimitive) {
                // Fast path for primitive types (except bool which isn't blittable and take another path)
                if (elemType != typeof (bool))
                    return MarshalArray<T> (ln);
                else
                    return (T[])(Array)MarshalBoolArray (ln);
            }

            var list = new List<T> ();
            int endPos = _pos + (int)ln;

            var elementReader = ReaderForType<T>();
            while (_pos < endPos)
                list.Add(elementReader(this));
            if (_pos != endPos)
                throw new ProtocolException("Read pos " + _pos + " != ep " + endPos);
            return list.ToArray ();
        }

        TArray[] MarshalArray<TArray> (uint length)
        {
            int sof = Marshal.SizeOf<TArray>();
            TArray[] array = new TArray[(int)(length / sof)];

            if (_endianness == Environment.NativeEndianness) {
                Buffer.BlockCopy (_data.Array, _data.Offset + _pos, array, 0, (int)length);
                _pos += (int)length;
            } else {
                GCHandle handle = GCHandle.Alloc (array, GCHandleType.Pinned);
                DirectCopy (sof, length, handle);
                handle.Free ();
            }
            return array;
        }

        void DirectCopy(int sof, uint length, GCHandle handle) => DirectCopy(sof, length, handle.AddrOfPinnedObject());

        void DirectCopy(int sof, uint length, IntPtr handle)
        {
            if (_endianness == Environment.NativeEndianness)
                Marshal.Copy(_data.Array, _data.Offset + _pos, handle, (int)length);
            else
                Marshal.Copy(EndianSwap(_data.Array, _data.Offset + _pos, (int)length), 0, handle, (int)length);
            _pos += (int)length * sof;
        }

        bool[] MarshalBoolArray (uint length)
        {
            bool[] array = new bool [length];
            for (int i = 0; i < length; i++)
                array[i] = ReadBoolean ();
            return array;
        }

        public object ReadStruct(Type type)
        {
            if (!_skipNextStructPadding)
                ReadPad (8);
            _skipNextStructPadding = false;

            var (fis, getters) = GetStructFieldSetters(type);
            // Empty struct? No need for processing
            if (fis.Length == 0)
                return Activator.CreateInstance(type);
            if (IsEligibleStruct(type, fis))
                return MarshalStruct(type, fis);

            object val = Activator.CreateInstance(type);
            for (int i = 0; i < fis.Length; ++i)
            {
                if (i == 7 && (type.Name == "ValueTuple`8"))
                    _skipNextStructPadding = true;
                var fieldVal = Read(fis[i].FieldType);
                val = getters.Value[i](val, fieldVal);
            }
            return val;
        }

        // NEEDED - do not remove!
        public T ReadStruct<T>() => (T)ReadStruct(typeof(T));

        object MarshalStruct(Type structType, FieldInfo[] fis)
        {
            object strct = Activator.CreateInstance(structType);
            int sof = Marshal.SizeOf(fis[0].FieldType);
            GCHandle handle = GCHandle.Alloc(strct, GCHandleType.Pinned);
            DirectCopy(sof, (uint)(fis.Length * sof), handle);
            handle.Free();
            return strct;
        }

        public void ReadNull()
        {
            if (_data.Array[_data.Offset + _pos] != 0)
                throw new ProtocolException("Read non-zero byte at position " + _pos + " while expecting null terminator");
            _pos++;
        }

        public void ReadPad(int alignment)
        {
            for (int endPos = ProtocolInformation.Padded (_pos, alignment) ; _pos != endPos ; _pos++)
                if (_data.Array[_data.Offset + _pos] != 0)
                    throw new ProtocolException("Read non-zero byte at position " + _pos + " while expecting padding. Value given: " + _data.Array[_data.Offset + _pos]);
        }

        // If a struct is only composed of primitive type fields (i.e. blittable types)
        // then this method return true. Result is cached in isPrimitiveStruct dictionary.
        internal static bool IsEligibleStruct(Type structType, FieldInfo[] fields)
        {
            lock (s_isPrimitiveStruct)
            {
                if (s_isPrimitiveStruct.TryGetValue(structType, out bool result))
                    return result;

                if (!(s_isPrimitiveStruct[structType] = fields.All((f) => f.FieldType.GetTypeInfo().IsPrimitive && f.FieldType != typeof(bool))))
                    return false;

                int alignement = ProtocolInformation.GetAlignment(fields[0].FieldType);

                return s_isPrimitiveStruct[structType] = !fields.Any((f) => ProtocolInformation.GetAlignment(f.FieldType) != alignement);
            }
        }
#endregion

        private class BusObject : IDBusObject
        {
            public BusObject(ObjectPath path)
            {
                ObjectPath = path;
            }

            public ObjectPath ObjectPath { get; }
        }
    }
}
