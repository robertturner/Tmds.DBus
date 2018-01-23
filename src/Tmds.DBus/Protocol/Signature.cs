// Copyright 2006 Alp Toker <alp@atoker.com>
// Copyright 2016 Tom Deseyn <tom.deseyn@gmail.com>
// This software is made available under the MIT License
// See COPYING for details

using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Tmds.DBus.Objects;

namespace Tmds.DBus.Protocol
{
    public struct Signature
    {
        public static readonly Signature Empty = new Signature (String.Empty);
        public static readonly Signature ArraySig = Allocate (DType.Array);
        public static readonly Signature ByteSig = Allocate (DType.Byte);
        public static readonly Signature DictEntryBegin = Allocate (DType.DictEntryBegin);
        public static readonly Signature DictEntryEnd = Allocate (DType.DictEntryEnd);
        public static readonly Signature Int32Sig = Allocate (DType.Int32);
        public static readonly Signature UInt16Sig = Allocate (DType.UInt16);
        public static readonly Signature UInt32Sig = Allocate (DType.UInt32);
        public static readonly Signature StringSig = Allocate (DType.String);
        public static readonly Signature StructBegin = Allocate (DType.StructBegin);
        public static readonly Signature StructEnd = Allocate (DType.StructEnd);
        public static readonly Signature ObjectPathSig = Allocate (DType.ObjectPath);
        public static readonly Signature SignatureSig = Allocate (DType.Signature);
        public static readonly Signature VariantSig = Allocate (DType.Variant);
        public static readonly Signature BoolSig = Allocate(DType.Boolean);
        public static readonly Signature DoubleSig = Allocate(DType.Double);
        public static readonly Signature Int16Sig = Allocate(DType.Int16);
        public static readonly Signature Int64Sig = Allocate(DType.Int64);
        public static readonly Signature SingleSig = Allocate(DType.Single);
        public static readonly Signature UInt64Sig = Allocate(DType.UInt64);
        public static readonly Signature StructBeginSig = Allocate(DType.StructBegin);
        public static readonly Signature StructEndSig = Allocate(DType.StructEnd);
        public static readonly Signature DictEntryBeginSig = Allocate(DType.DictEntryBegin);
        public static readonly Signature DictEntryEndSig = Allocate(DType.DictEntryEnd);

        private byte[] _data;

        public static bool operator == (Signature a, Signature b)
        {
            if (a._data == b._data)
                return true;

            if (a._data == null)
                return false;

            if (b._data == null)
                return false;

            if (a._data.Length != b._data.Length)
                return false;

            for (int i = 0 ; i != a._data.Length ; i++)
                if (a._data[i] != b._data[i])
                    return false;

            return true;
        }

        public static bool operator != (Signature a, Signature b)
        {
            return !(a == b);
        }

        public override bool Equals (object o)
        {
            if (o == null)
                return false;

            if (!(o is Signature))
                return false;

            return this == (Signature)o;
        }

        public override int GetHashCode ()
        {
            if (_data == null)
            {
                return 0;
            }
            int hash = 17;
            for(int i = 0; i < _data.Length; i++)
            {
                hash = hash * 31 + _data[i].GetHashCode();
            }
            return hash;
        }

        public static Signature operator + (Signature s1, Signature s2)
        {
            return Concat (s1, s2);
        }

        public static Signature Concat (Signature s1, Signature s2)
        {
            if (s1._data == null && s2._data == null)
                return Signature.Empty;

            if (s1._data == null)
                return s2;

            if (s2._data == null)
                return s1;

            if (s1.Length + s2.Length == 0)
                return Signature.Empty;

            byte[] data = new byte[s1._data.Length + s2._data.Length];
            s1._data.CopyTo (data, 0);
            s2._data.CopyTo (data, s1._data.Length);
            return Signature.Take (data);
        }

        public Signature (string value)
        {
            if (value == null)
                throw new ArgumentNullException ("value");
            if (!IsValid (value))
                throw new ArgumentException (string.Format ("'{0}' is not a valid signature", value), "value");

            foreach (var c in value)
                if (!Enum.IsDefined (typeof (DType), (byte) c))
                    throw new ArgumentException (string.Format ("{0} is not a valid dbus type", c));

            if (value.Length == 0) {
                _data = Array.Empty<byte>();
            } else if (value.Length == 1) {
                _data = DataForDType ((DType)value[0]);
            } else {
                _data = Encoding.ASCII.GetBytes (value);
            }
        }


        public static implicit operator Signature(string value)
        {
            return new Signature(value);
        }

        // Basic validity is to check that every "opening" DType has a corresponding closing DType
        static bool IsValid (string strSig)
        {
            int structCount = 0;
            int dictCount = 0;

            foreach (char c in strSig) {
                switch ((DType)c) {
                case DType.StructBegin:
                    structCount++;
                    break;
                case DType.StructEnd:
                    structCount--;
                    break;
                case DType.DictEntryBegin:
                    dictCount++;
                    break;
                case DType.DictEntryEnd:
                    dictCount--;
                    break;
                }
            }

            return structCount == 0 && dictCount == 0;
        }

        internal static Signature Take (byte[] value)
        {
            Signature sig;

            if (value.Length == 0) {
                sig._data = Empty._data;
                return sig;
            }

            if (value.Length == 1) {
                sig._data = DataForDType ((DType)value[0]);
                return sig;
            }

            sig._data = value;
            return sig;
        }

        static byte[] DataForDType (DType value)
        {
            switch (value) {
                case DType.Byte: return ByteSig._data;
                case DType.Boolean: return BoolSig._data;
                case DType.Int16: return Int16Sig._data;
                case DType.UInt16: return UInt16Sig._data;
                case DType.Int32: return Int32Sig._data;
                case DType.UInt32: return UInt32Sig._data;
                case DType.Int64: return Int64Sig._data;
                case DType.UInt64: return UInt64Sig._data;
                case DType.Single: return SingleSig._data;
                case DType.Double: return DoubleSig._data;
                case DType.String: return StringSig._data;
                case DType.ObjectPath: return ObjectPathSig._data;
                case DType.Signature: return SignatureSig._data;
                case DType.Array: return ArraySig._data;
                case DType.Variant: return VariantSig._data;
                case DType.StructBegin: return StructBeginSig._data;
                case DType.StructEnd: return StructEndSig._data;
                case DType.DictEntryBegin: return DictEntryBeginSig._data;
                case DType.DictEntryEnd: return DictEntryEndSig._data;
                default:
                    return new byte[] {(byte)value};
            }
        }

        private static Signature Allocate (DType value)
        {
            Signature sig;
            sig._data = new byte[] {(byte)value};
            return sig;
        }

        internal Signature (DType value)
        {
            this._data = DataForDType (value);
        }

        public byte[] GetBuffer ()
        {
            return _data;
        }

        internal DType this[int index]
        {
            get {
                return (DType)_data[index];
            }
        }

        public int Length
        {
            get {
                return _data != null ? _data.Length : 0;
            }
        }

        public string Value
        {
            get {
                if (_data == null)
                    return String.Empty;

                return Encoding.ASCII.GetString (_data);
            }
        }

        public override string ToString ()
        {
            return Value;
        }

        public static Signature MakeArray (Signature signature)
        {
            if (!signature.IsSingleCompleteType)
                throw new ArgumentException ("The type of an array must be a single complete type", "signature");
            return Signature.ArraySig + signature;
        }

        public static Signature MakeStruct (Signature signature)
        {
            if (signature == Signature.Empty)
                throw new ArgumentException ("Cannot create a struct with no fields", "signature");

            return Signature.StructBegin + signature + Signature.StructEnd;
        }

        public static Signature MakeDictEntry (Signature keyType, Signature valueType)
        {
            if (!keyType.IsSingleCompleteType)
                throw new ArgumentException ("Signature must be a single complete type", "keyType");
            if (!valueType.IsSingleCompleteType)
                throw new ArgumentException ("Signature must be a single complete type", "valueType");

            return Signature.DictEntryBegin +
                    keyType +
                    valueType +
                    Signature.DictEntryEnd;
        }

        public static Signature MakeDict (Signature keyType, Signature valueType)
        {
            return MakeArray (MakeDictEntry (keyType, valueType));
        }

        public int Alignment
        {
            get {
                if (_data.Length == 0)
                    return 0;

                return ProtocolInformation.GetAlignment (this[0]);
            }
        }

        static int GetSize (DType dtype)
        {
            switch (dtype) {
                case DType.Byte:
                    return 1;
                case DType.Boolean:
                    return 4;
                case DType.Int16:
                case DType.UInt16:
                    return 2;
                case DType.Int32:
                case DType.UInt32:
                    return 4;
                case DType.Int64:
                case DType.UInt64:
                    return 8;
                case DType.Single:
                    return 4;
                case DType.Double:
                    return 8;
                case DType.String:
                case DType.ObjectPath:
                case DType.Signature:
                case DType.Array:
                case DType.StructBegin:
                case DType.Variant:
                case DType.DictEntryBegin:
                    return -1;
                case DType.Invalid:
                default:
                    throw new ProtocolException("Cannot determine size of unknown D-Bus type: " + dtype);
            }
        }

        public bool GetFixedSize (ref int size)
        {
            if (size < 0)
                return false;

            if (_data.Length == 0)
                return true;

            // Sensible?
            size = ProtocolInformation.Padded (size, Alignment);

            if (_data.Length == 1) {
                int valueSize = GetSize (this[0]);

                if (valueSize == -1)
                    return false;

                size += valueSize;
                return true;
            }

            if (IsStructlike) {
                foreach (Signature sig in GetParts ())
                        if (!sig.GetFixedSize (ref size))
                            return false;
                return true;
            }

            if (IsArray || IsDict)
                return false;

            if (IsStruct) {
                foreach (Signature sig in GetFieldSignatures ())
                        if (!sig.GetFixedSize (ref size))
                            return false;
                return true;
            }

            // Any other cases?
            throw new Exception ();
        }

        public bool IsSingleCompleteType
        {
            get {
                if (_data.Length == 0)
                    return true;
                var checker = new SignatureChecker (_data);
                return checker.CheckSignature ();
            }
        }

        public bool IsStruct
        {
            get {
                if (Length < 2)
                    return false;

                if (this[0] != DType.StructBegin)
                    return false;

                // FIXME: Incorrect! What if this is in fact a Structlike starting and finishing with structs?
                if (this[Length - 1] != DType.StructEnd)
                    return false;

                return true;
            }
        }

        public bool IsStructlike
        {
            get {
                if (Length < 2)
                    return false;

                if (IsArray)
                    return false;

                if (IsDict)
                    return false;

                if (IsStruct)
                    return false;

                return true;
            }
        }

        public bool IsDict
        {
            get {
                if (Length < 3)
                    return false;

                if (!IsArray)
                    return false;

                // 0 is 'a'
                if (this[1] != DType.DictEntryBegin)
                    return false;

                return true;
            }
        }

        public bool IsArray
        {
            get {
                if (Length < 2)
                    return false;

                if (this[0] != DType.Array)
                    return false;

                return true;
            }
        }

        public Type ToType ()
        {
            int pos = 0;
            Type ret = ToType (ref pos);
            if (pos != _data.Length)
                throw new ProtocolException("Signature '" + Value + "' is not a single complete type");
            return ret;
        }

        public IEnumerable<Signature> GetFieldSignatures ()
        {
            if (this == Signature.Empty || this[0] != DType.StructBegin)
                throw new ProtocolException("Not a struct");

            for (int pos = 1 ; pos < _data.Length - 1 ;)
                yield return GetNextSignature (ref pos);
        }

        public void GetDictEntrySignatures (out Signature sigKey, out Signature sigValue)
        {
            if (this == Signature.Empty || this[0] != DType.DictEntryBegin)
                throw new ProtocolException("Not a DictEntry");

            int pos = 1;
            sigKey = GetNextSignature (ref pos);
            sigValue = GetNextSignature (ref pos);
        }

        public IEnumerable<Signature> GetParts ()
        {
            if (_data == null)
                yield break;
            for (int pos = 0 ; pos < _data.Length ;) {
                yield return GetNextSignature (ref pos);
            }
        }

        public Signature GetNextSignature (ref int pos)
        {
            if (_data == null)
                return Signature.Empty;

            DType dtype = (DType)_data[pos++];

            switch (dtype) {
                //case DType.Invalid:
                //    return typeof (void);
                case DType.Array:
                    //peek to see if this is in fact a dictionary
                    if ((DType)_data[pos] == DType.DictEntryBegin) {
                        //skip over the {
                        pos++;
                        Signature keyType = GetNextSignature (ref pos);
                        Signature valueType = GetNextSignature (ref pos);
                        //skip over the }
                        pos++;
                        return Signature.MakeDict (keyType, valueType);
                    } else {
                        Signature elementType = GetNextSignature (ref pos);
                        return MakeArray (elementType);
                    }
                //case DType.DictEntryBegin: // FIXME: DictEntries should be handled separately.
                case DType.StructBegin:
                    //List<Signature> fieldTypes = new List<Signature> ();
                    Signature fieldsSig = Signature.Empty;
                    while ((DType)_data[pos] != DType.StructEnd)
                        fieldsSig += GetNextSignature (ref pos);
                    //skip over the )
                    pos++;
                    return Signature.MakeStruct (fieldsSig);
                    //return fieldsSig;
                case DType.DictEntryBegin:
                    Signature sigKey = GetNextSignature (ref pos);
                    Signature sigValue = GetNextSignature (ref pos);
                    //skip over the }
                    pos++;
                    return Signature.MakeDictEntry (sigKey, sigValue);
                default:
                    return new Signature (dtype);
            }
        }

        public Type ToType (ref int pos)
        {
            if (_data == null)
                return typeof (void);

            DType dtype = (DType)_data[pos++];

            switch (dtype) {
            case DType.Invalid:
                return typeof (void);
            case DType.Byte:
                return typeof (byte);
            case DType.Boolean:
                return typeof (bool);
            case DType.Int16:
                return typeof (short);
            case DType.UInt16:
                return typeof (ushort);
            case DType.Int32:
                return typeof (int);
            case DType.UInt32:
                return typeof (uint);
            case DType.Int64:
                return typeof (long);
            case DType.UInt64:
                return typeof (ulong);
            case DType.Single: ////not supported by libdbus at time of writing
                return typeof (float);
            case DType.Double:
                return typeof (double);
            case DType.String:
                return typeof (string);
            case DType.ObjectPath:
                return typeof (ObjectPath);
            case DType.Signature:
                return typeof (Signature);
            case DType.Array:
                //peek to see if this is in fact a dictionary
                if ((DType)_data[pos] == DType.DictEntryBegin) {
                    //skip over the {
                    pos++;
                    Type keyType = ToType (ref pos);
                    Type valueType = ToType (ref pos);
                    //skip over the }
                    pos++;
                    return typeof(IDictionary<,>).MakeGenericType (new [] { keyType, valueType});
                } else {
                    return ToType (ref pos).MakeArrayType ();
                }
            case DType.StructBegin:
                List<Type> innerTypes = new List<Type> ();
                while (((DType)_data[pos]) != DType.StructEnd)
                    innerTypes.Add (ToType (ref pos));
                // go over the struct end
                pos++;
                return TypeOfValueTupleOf(innerTypes.ToArray ());
            case DType.DictEntryBegin:
                return typeof (System.Collections.Generic.KeyValuePair<,>);
            case DType.Variant:
                return typeof (object);
            default:
                throw new NotSupportedException ("Parsing or converting this signature is not yet supported (signature was '" + Value + "'), at DType." + dtype);
            }
        }

        public static Signature GetSig(object[] objs)
        {
            return GetSig(objs.Select(o => o.GetType()).ToArray());
        }

        public static Signature GetSig(Type[] types)
        {
            if (types == null)
                throw new ArgumentNullException (nameof(types));

            Signature sig = Signature.Empty;
            foreach (Type type in types)
                    sig += GetSig (type);
            return sig;
        }

        public static Signature GetSig(Type type)
        {
            if (type == null)
                throw new ArgumentNullException (nameof(type));

            if (type.GetTypeInfo().IsEnum)
                type = Enum.GetUnderlyingType(type);

            if (type == typeof(bool))
                return BoolSig;
            else if (type == typeof(byte))
                return ByteSig;
            else if (type == typeof(double))
                return DoubleSig;
            else if (type == typeof(short))
                return Int16Sig;
            else if (type == typeof(int))
                return Int32Sig;
            else if (type == typeof(long))
                return Int64Sig;
            else if (type == typeof(ObjectPath))
                return ObjectPathSig;
            else if (type == typeof(Signature))
                return SignatureSig;
            else if (type == typeof(string))
                return StringSig;
            else if (type == typeof(float))
                return SingleSig;
            else if (type == typeof(ushort))
                return UInt16Sig;
            else if (type == typeof(uint))
                return UInt32Sig;
            else if (type == typeof(ulong))
                return UInt64Sig;
            else if (type == typeof(object))
                return VariantSig;
            else if (type == typeof(IDBusObject))
                return ObjectPathSig;
            else if (type == typeof(void))
                return Empty;

            if (ArgTypeInspector.IsDBusObjectType(type))
                return ObjectPathSig;

            Type elementType;
            var enumerableType = ArgTypeInspector.InspectEnumerableType(type, out elementType);
            if (enumerableType != ArgTypeInspector.EnumerableType.NotEnumerable)
            {
                if ((enumerableType == ArgTypeInspector.EnumerableType.EnumerableKeyValuePair) ||
                    (enumerableType == ArgTypeInspector.EnumerableType.GenericDictionary) ||
                    (enumerableType == ArgTypeInspector.EnumerableType.AttributeDictionary))
                {
                    Type keyType = elementType.GenericTypeArguments[0];
                    Type valueType = elementType.GenericTypeArguments[1];
                    return Signature.MakeDict(GetSig(keyType), GetSig(valueType));
                }
                else // Enumerable
                    return MakeArray(GetSig(elementType));
            }

            if (ArgTypeInspector.IsStructType(type, out bool isValueTuple))
            {
                Signature sig = Signature.Empty;
                var fields = ArgTypeInspector.GetStructFields(type, isValueTuple);
                foreach (FieldInfo fi in fields)
                    sig += GetSig(fi.FieldType);

                return Signature.MakeStruct(sig);
            }

            throw new ArgumentException($"Cannot (de)serialize Type '{type.FullName}'");
        }


        public static Type TypeOfValueTupleOf(Type[] innerTypes)
        {
            // We only support up to 7 inner types
            if (innerTypes == null || innerTypes.Length == 0 || innerTypes.Length > 7)
                throw new NotSupportedException($"ValueTuple of length {innerTypes.Length} is not supported");

            Type structType = null;
            switch (innerTypes.Length) {
            case 1:
                structType = typeof(ValueTuple<>);
                break;
            case 2:
                structType = typeof(ValueTuple<,>);
                break;
            case 3:
                structType = typeof(ValueTuple<,,>);
                break;
            case 4:
                structType = typeof(ValueTuple<,,,>);
                break;
            case 5:
                structType = typeof(ValueTuple<,,,,>);
                break;
            case 6:
                structType = typeof(ValueTuple<,,,,,>);
                break;
            case 7:
                structType = typeof(ValueTuple<,,,,,,>);
                break;
            }
            return structType.MakeGenericType(innerTypes);
        }

        class SignatureChecker
        {
            byte[] data;
            int pos;

            internal SignatureChecker (byte[] data)
            {
                this.data = data;
            }

            internal bool CheckSignature ()
            {
                return SingleType () ? pos == data.Length : false;
            }

            bool SingleType ()
            {
                if (pos >= data.Length)
                    return false;

                //Console.WriteLine ((DType)data[pos]);

                switch ((DType)data[pos]) {
                // Simple Type
                case DType.Byte:
                case DType.Boolean:
                case DType.Int16:
                case DType.UInt16:
                case DType.Int32:
                case DType.UInt32:
                case DType.Int64:
                case DType.UInt64:
                case DType.Single:
                case DType.Double:
                case DType.String:
                case DType.ObjectPath:
                case DType.Signature:
                case DType.Variant:
                    pos += 1;
                    return true;
                case DType.Array:
                    pos += 1;
                    return ArrayType ();
                case DType.StructBegin:
                    pos += 1;
                    return StructType ();
                case DType.DictEntryBegin:
                    pos += 1;
                    return DictType ();
                }

                return false;
            }

            bool ArrayType ()
            {
                return SingleType ();
            }

            bool DictType ()
            {
                bool result = SingleType () && SingleType () && ((DType)data[pos]) == DType.DictEntryEnd;
                if (result)
                    pos += 1;
                return result;
            }

            bool StructType ()
            {
                if (pos >= data.Length)
                    return false;
                while (((DType)data[pos]) != DType.StructEnd) {
                    if (!SingleType ())
                        return false;
                    if (pos >= data.Length)
                        return false;
                }
                pos += 1;

                return true;
            }
        }
    }
}
