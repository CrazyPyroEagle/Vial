using dnlib.DotNet;
using System;
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

        public MethodSignature Method(string name, CallConvention callConvention, params Func<ModuleDef, TypeSig>[] parameters) => Method(name, callConvention, (IEnumerable<Func<ModuleDef, TypeSig>>)parameters);
        public MethodSignature Method(string name, CallConvention callConvention, IEnumerable<Func<ModuleDef, TypeSig>> parameters) => new InternalMethodSignature(this, name, callConvention, parameters.ToArray());

        public static bool operator ==(TypeSignature a, TypeSignature b) => a?.FullName == b?.FullName;
        public static bool operator !=(TypeSignature a, TypeSignature b) => !(a == b);

        private sealed class InternalFieldSignature : FieldSignature
        {
            internal InternalFieldSignature(TypeSignature type, string name, TypeSignature fieldType) : base(type, name, fieldType) { }
        }

        private sealed class InternalMethodSignature : MethodSignature
        {
            internal InternalMethodSignature(TypeSignature type, string name, CallConvention callConvention, IEnumerable<Func<ModuleDef, TypeSig>> parameters) : base(type, name, callConvention, parameters) { }
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

        private protected MethodSignature(TypeSignature type, string name, CallConvention callConvention, IEnumerable<Func<ModuleDef, TypeSig>> parameters)
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
            internal InternalParameterSignature(MethodSignature method, int index, Func<ModuleDef, TypeSig> type) : base(method, index, type) { }
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
        public Func<ModuleDef, TypeSig> ParameterType { get; }

        private protected ParameterSignature(MethodSignature method, int index, Func<ModuleDef, TypeSig> type)
        {
            DeclaringMethod = method;
            Index = index;
            ParameterType = type;
        }

        public override bool Equals(object obj) => base.Equals(obj);
        public override int GetHashCode() => (Index, ParameterType).GetHashCode();
        public override string ToString() => string.Format("[{0}]{1}", Index, ParameterType(new ModuleDefUser()));

        public static bool operator ==(ParameterSignature a, ParameterSignature b) => a?.Index == b?.Index && CompareTypes(a?.ParameterType, b?.ParameterType);
        public static bool operator !=(ParameterSignature a, ParameterSignature b) => !(a == b);

        private static bool CompareTypes(Func<ModuleDef, TypeSig> a, Func<ModuleDef, TypeSig> b)
        {
            if (a == b) return true;
            if (a == null || b == null) return false;
            ModuleDef module = new ModuleDefUser();
            return new SigComparer(SigComparerOptions.IgnoreModifiers | SigComparerOptions.DontCompareReturnType | SigComparerOptions.DontCompareTypeScope).Equals(a(module), b(module));
        }
    }

    sealed class TypeDescriptor
    {
        public TypeSignature Signature { get; }
        public AccessLevel AccessLevel { get; }

        private TypeDescriptor(Builder builder)
        {
            Signature = builder.Signature;
            AccessLevel = builder.AccessLevel;
        }

        public sealed class Builder
        {
            public TypeSignature Signature { get; }
            public AccessLevel AccessLevel { get; set; }

            public Builder(TypeSignature signature) => Signature = signature;

            public TypeDescriptor Build() => new TypeDescriptor(this);

            public static implicit operator TypeDescriptor(Builder self) => self.Build();
        }
    }

    sealed class FieldDescriptor
    {
        public FieldSignature Signature { get; }
        public AccessLevel AccessLevel { get; }
        public bool IsInitOnly { get; }

        private FieldDescriptor(Builder builder)
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

            public FieldDescriptor Build() => new FieldDescriptor(this);

            public static implicit operator FieldDescriptor(Builder self) => self.Build();
        }
    }
    
    sealed class MethodDescriptor
    {
        public MethodSignature Signature { get; }
        public AccessLevel AccessLevel { get; }
        public TypeSignature ReturnType { get; }
        public IEnumerable<ParameterDescriptor> Parameters { get; }

        private MethodDescriptor(Builder builder, IEnumerable<ParameterDescriptor> parameters)
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

            private readonly List<ParameterDescriptor.Builder> parameters = new List<ParameterDescriptor.Builder>();

            public Builder(MethodSignature signature) => Signature = signature;

            public ParameterDescriptor.Builder Parameter(int index)
            {
                lock (parameters)
                {
                    ParameterDescriptor.Builder result = parameters.FirstOrDefault(p => p.Signature.Index == index);
                    if (result == null) parameters.Add(result = new ParameterDescriptor.Builder(Signature.Parameters[index]));
                    return result;
                }
            }

            public Builder Parameter(int index, Action<ParameterDescriptor.Builder> action)
            {
                action(Parameter(index));
                return this;
            }

            public MethodDescriptor Build() => new MethodDescriptor(this, parameters.Where(b => b != null).Select(b => b.Build()).ToArray());

            public static implicit operator MethodDescriptor(Builder self) => self.Build();
        }
    }

    sealed class ParameterDescriptor
    {
        public ParameterSignature Signature { get; }
        public AccessMode AccessMode { get; }

        private ParameterDescriptor(Builder builder)
        {
            Signature = builder.Signature;
            AccessMode = builder.AccessMode;
        }

        public sealed class Builder
        {
            public ParameterSignature Signature { get; }
            public AccessMode AccessMode { get; set; }

            public Builder(ParameterSignature signature) => Signature = signature;

            public ParameterDescriptor Build() => new ParameterDescriptor(this);

            public static implicit operator ParameterDescriptor(Builder self) => self.Build();
        }
    }

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

    enum AccessLevel { Private, FamilyAndAssembly, Family, Assembly, FamilyOrAssembly, Public }

    [Flags]
    enum AccessMode { None, Read, Write, ReadWrite }
}
