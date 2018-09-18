﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vial.Installer
{
    sealed class TypeSignature
    {
        private static readonly Dictionary<string, WeakReference<TypeSignature>> types = new Dictionary<string, WeakReference<TypeSignature>>();
        private readonly Dictionary<string, WeakReference<FieldSignature>> fields = new Dictionary<string, WeakReference<FieldSignature>>();

        public static readonly TypeSignature Void = Get("System.Void", TypeKind.CorLib);
        public static readonly TypeSignature Boolean = Get("System.Boolean", TypeKind.CorLib);
        public static readonly TypeSignature Char = Get("System.Char", TypeKind.CorLib);
        public static readonly TypeSignature SByte = Get("System.SByte", TypeKind.CorLib);
        public static readonly TypeSignature Byte = Get("System.Byte", TypeKind.CorLib);
        public static readonly TypeSignature Int16 = Get("System.Int16", TypeKind.CorLib);
        public static readonly TypeSignature UInt16 = Get("System.UInt16", TypeKind.CorLib);
        public static readonly TypeSignature Int32 = Get("System.Int32", TypeKind.CorLib);
        public static readonly TypeSignature UInt32 = Get("System.UInt32", TypeKind.CorLib);
        public static readonly TypeSignature Int64 = Get("System.Int64", TypeKind.CorLib);
        public static readonly TypeSignature UInt64 = Get("System.UInt64", TypeKind.CorLib);
        public static readonly TypeSignature Single = Get("System.Single", TypeKind.CorLib);
        public static readonly TypeSignature Double = Get("System.Double", TypeKind.CorLib);
        public static readonly TypeSignature String = Get("System.String", TypeKind.CorLib);
        public static readonly TypeSignature TypedReference = Get("System.TypedReference", TypeKind.CorLib);
        public static readonly TypeSignature IntPtr = Get("System.IntPtr", TypeKind.CorLib);
        public static readonly TypeSignature UIntPtr = Get("System.UIntPtr", TypeKind.CorLib);
        public static readonly TypeSignature Object = Get("System.Object", TypeKind.CorLib);

        public string FullName { get; }
        public TypeKind Kind { get; }

        private TypeSignature(string fullName, TypeKind kind)
        {
            FullName = fullName;
            Kind = kind;
        }

        public override bool Equals(object obj) => base.Equals(obj);
        public override int GetHashCode() => FullName.GetHashCode();
        public override string ToString() => FullName;

        public static TypeSignature Get(string fullName, TypeKind kind)
        {
            if (!types.TryGetValue(fullName, out WeakReference<TypeSignature> weakRef)) types.Add(fullName, weakRef = new WeakReference<TypeSignature>(null));
            if (!weakRef.TryGetTarget(out TypeSignature value)) weakRef.SetTarget(value = new TypeSignature(fullName, kind));
            if (value.Kind != kind && value.Kind != TypeKind.CorLib) throw new ArgumentException(string.Format("kind {1} is incompatible with type {0}", value, kind));
            return value;
        }

        public FieldSignature Field(string name, TypeSignature fieldType)
        {
            if (!fields.TryGetValue(name, out WeakReference<FieldSignature> weakRef)) fields.Add(name, weakRef = new WeakReference<FieldSignature>(null));
            if (!weakRef.TryGetTarget(out FieldSignature value)) value = new InternalFieldSignature(this, name, fieldType);
            if (value.FieldType != fieldType) throw new ArgumentException(string.Format("field type {1} is incompatible with field {0}", value, fieldType));
            return value;
        }

        public MethodSignature Method(string name, CallConvention callConvention, params TypeSignature[] parameters) => Method(name, callConvention, (IEnumerable<TypeSignature>)parameters);
        public MethodSignature Method(string name, CallConvention callConvention, IEnumerable<TypeSignature> parameters) => new InternalMethodSignature(this, name, callConvention, parameters.ToArray());

        public static bool operator ==(TypeSignature a, TypeSignature b) => a?.FullName == b?.FullName;
        public static bool operator !=(TypeSignature a, TypeSignature b) => !(a == b);

        private sealed class InternalFieldSignature : FieldSignature
        {
            internal InternalFieldSignature(TypeSignature type, string name, TypeSignature fieldType) : base(type, name, fieldType) { }
        }

        private sealed class InternalMethodSignature : MethodSignature
        {
            internal InternalMethodSignature(TypeSignature type, string name, CallConvention callConvention, IEnumerable<TypeSignature> parameters) : base(type, name, callConvention, parameters) { }
        }
    }

    class FieldSignature
    {
        public TypeSignature DeclaringType { get; }
        public string Name { get; }
        public TypeSignature FieldType { get; }

        private protected FieldSignature(TypeSignature type, string name, TypeSignature fieldType)
        {
            DeclaringType = type;
            Name = name;
            FieldType = fieldType;
        }

        public override bool Equals(object obj) => base.Equals(obj);
        public override int GetHashCode() => (DeclaringType, Name).GetHashCode();
        public override string ToString() => string.Format("{0}::{1}", DeclaringType, Name);

        public static bool operator ==(FieldSignature a, FieldSignature b) => a?.DeclaringType == b?.DeclaringType && a?.Name == b?.Name;
        public static bool operator !=(FieldSignature a, FieldSignature b) => !(a == b);
    }

    class MethodSignature
    {
        public TypeSignature DeclaringType { get; }
        public string Name { get; }
        public CallConvention CallConvention { get; }
        public IReadOnlyList<ParameterSignature> Parameters { get; }

        private protected MethodSignature(TypeSignature type, string name, CallConvention callConvention, IEnumerable<TypeSignature> parameters)
        {
            DeclaringType = type;
            Name = name;
            CallConvention = callConvention;
            int index = 0;
            Parameters = parameters.Select(p => new InternalParameterSignature(this, index++, p)).ToList();
        }

        public override bool Equals(object obj) => base.Equals(obj);
        public override int GetHashCode() => (DeclaringType, Name, CallConvention).GetHashCode();
        public override string ToString() => string.Format("{0}::{1}({2})", DeclaringType, Name, string.Join(", ", Parameters));

        public static bool operator ==(MethodSignature a, MethodSignature b) => a?.DeclaringType == b?.DeclaringType && a?.Name == b?.Name && a?.CallConvention == b?.CallConvention && a.Parameters.SequenceEqual(b.Parameters, new ParameterComparer());
        public static bool operator !=(MethodSignature a, MethodSignature b) => !(a == b);

        private sealed class InternalParameterSignature : ParameterSignature
        {
            internal InternalParameterSignature(MethodSignature method, int index, TypeSignature type) : base(method, index, type) { }
        }

        private sealed class ParameterComparer : IEqualityComparer<ParameterSignature>
        {
            public bool Equals(ParameterSignature x, ParameterSignature y) => x == y;

            public int GetHashCode(ParameterSignature obj) => obj.GetHashCode();
        }
    }

    class ParameterSignature
    {
        public MethodSignature DeclaringMethod { get; }
        public int Index { get; }
        public TypeSignature ParameterType { get; }

        private protected ParameterSignature(MethodSignature method, int index, TypeSignature type)
        {
            DeclaringMethod = method;
            Index = index;
            ParameterType = type;
        }

        public override bool Equals(object obj) => base.Equals(obj);
        public override int GetHashCode() => (Index, ParameterType).GetHashCode();
        public override string ToString() => string.Format("[{0}]{1}", Index, ParameterType);

        public static bool operator ==(ParameterSignature a, ParameterSignature b) => a?.Index == b?.Index && a?.ParameterType == b?.ParameterType;
        public static bool operator !=(ParameterSignature a, ParameterSignature b) => !(a == b);
    }

    sealed class TypeDependency
    {
        public TypeSignature Signature { get; }
        public AccessLevel AccessLevel { get; }
        public IEnumerable<FieldDependency> Fields { get; private set; }
        public IEnumerable<MethodDependency> Methods { get; private set; }
        public IEnumerable<MethodSignature> Originals { get; private set; }

        private TypeDependency(Builder builder)
        {
            Signature = builder.Signature;
            AccessLevel = builder.AccessLevel;
            Originals = new List<MethodSignature>(builder.Originals);
        }

        public sealed class Builder
        {
            public TypeSignature Signature { get; }
            public AccessLevel AccessLevel { get; set; }
            public List<MethodSignature> Originals { get; } = new List<MethodSignature>();

            private readonly List<FieldDependency.Builder> fields = new List<FieldDependency.Builder>();
            private readonly List<MethodDependency.Builder> methods = new List<MethodDependency.Builder>();

            public Builder(TypeSignature signature) => Signature = signature;

            public FieldDependency.Builder Field(FieldSignature signature)
            {
                lock (fields)
                {
                    FieldDependency.Builder result = fields.FirstOrDefault(f => f.Signature == signature);
                    if (result == null) fields.Add(result = new FieldDependency.Builder(signature));
                    return result;
                }
            }

            public Builder Field(FieldSignature signature, Action<FieldDependency.Builder> action)
            {
                action(Field(signature));
                return this;
            }

            public MethodDependency.Builder Method(MethodSignature signature)
            {
                lock (methods)
                {
                    MethodDependency.Builder result = methods.FirstOrDefault(m => m.Signature == signature);
                    if (result == null) methods.Add(result = new MethodDependency.Builder(signature));
                    return result;
                }
            }

            public Builder Method(MethodSignature signature, Action<MethodDependency.Builder> action)
            {
                action(Method(signature));
                return this;
            }

            public Builder Original(params MethodSignature[] original) => Original((IEnumerable<MethodSignature>)original);
            public Builder Original(IEnumerable<MethodSignature> original)
            {
                Originals.AddRange(original);
                return this;
            }

            public TypeDependency Build() => new TypeDependency(this)
            {
                Fields = fields.Select(b => b.Build()).ToArray(),
                Methods = methods.Select(b => b.Build()).ToArray()
            };

            public static implicit operator TypeDependency(Builder self) => self.Build();
        }
    }

    sealed class FieldDependency
    {
        public FieldSignature Signature { get; }
        public AccessLevel AccessLevel { get; }
        public bool IsInitOnly { get; }

        private FieldDependency(Builder builder)
        {
            Signature = builder.Signature;
            AccessLevel = builder.AccessLevel;
            IsInitOnly = builder.IsInitOnly;
        }

        public sealed class Builder
        {
            public FieldSignature Signature { get; }
            public AccessLevel AccessLevel { get; set; }
            public bool IsInitOnly { get; set; }

            public Builder(FieldSignature signature) => Signature = signature;

            public FieldDependency Build() => new FieldDependency(this);

            public static implicit operator FieldDependency(Builder self) => self.Build();
        }
    }

    sealed class MethodDependency
    {
        public MethodSignature Signature { get; }
        public AccessLevel AccessLevel { get; }
        public TypeSignature ReturnType { get; }
        public IEnumerable<ParameterDependency> Parameters { get; }

        private MethodDependency(Builder builder, IEnumerable<ParameterDependency> parameters)
        {
            Signature = builder.Signature;
            AccessLevel = builder.AccessLevel;
            ReturnType = builder.ReturnType;
            Parameters = parameters;
        }

        public sealed class Builder
        {
            public MethodSignature Signature { get; }
            public AccessLevel AccessLevel { get; set; }
            public TypeSignature ReturnType { get; set; }

            private readonly List<ParameterDependency.Builder> parameters = new List<ParameterDependency.Builder>();

            public Builder(MethodSignature signature) => Signature = signature;

            public ParameterDependency.Builder Parameter(int index)
            {
                lock (parameters)
                {
                    ParameterDependency.Builder result = parameters.FirstOrDefault(p => p.Signature.Index == index);
                    if (result == null) parameters.Add(result = new ParameterDependency.Builder(Signature.Parameters[index]));
                    return result;
                }
            }

            public Builder Parameter(int index, Action<ParameterDependency.Builder> action)
            {
                action(Parameter(index));
                return this;
            }

            public MethodDependency Build() => new MethodDependency(this, parameters.Where(b => b != null).Select(b => b.Build()).ToArray());

            public static implicit operator MethodDependency(Builder self) => self.Build();
        }
    }

    sealed class ParameterDependency
    {
        public ParameterSignature Signature { get; }
        public AccessMode AccessMode { get; }

        private ParameterDependency(Builder builder)
        {
            Signature = builder.Signature;
            AccessMode = builder.AccessMode;
        }

        public sealed class Builder
        {
            public ParameterSignature Signature { get; }
            public AccessMode AccessMode { get; set; }

            public Builder(ParameterSignature signature) => Signature = signature;

            public ParameterDependency Build() => new ParameterDependency(this);

            public static implicit operator ParameterDependency(Builder self) => self.Build();
        }
    }

    enum AccessLevel { Private, FamilyAndAssembly, Family, Assembly, FamilyOrAssembly, Public }

    [Flags]
    enum AccessMode { None, Read, Write, ReadWrite }

    enum TypeKind
    {
        Class,
        Value,
        CorLib
    }

    [Flags]
    enum CallConvention
    {
        Standard = 1,
        VarArgs = 2,
        //Any = 3,
        Generic = 16,
        HasThis = 32,
        ExplicitThis = 64
    }
}
