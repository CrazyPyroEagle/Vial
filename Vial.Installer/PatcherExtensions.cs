using System;
using System.Collections.Generic;
using System.Linq;
using dnlib.DotNet;

namespace Vial.Installer
{
    static class PatcherExtensions
    {
        private const SigComparerOptions ComparerOptions = SigComparerOptions.IgnoreModifiers | SigComparerOptions.DontCompareReturnType | SigComparerOptions.DontCompareTypeScope;

        public static TType Substitute<TType>(this IReadOnlyDictionary<TType, TType> substitutions, TType value)
        {
            while (substitutions.TryGetValue(value, out TType newValue)) value = newValue;
            return value;
        }

        public static TypeSig ApplyToLeaf(this TypeSig type, Func<TypeSig, TypeSig> newLeaf)
        {
            if (type == null) return null;
            if (type.Next == null) return newLeaf(type);
            TypeSig unwrapped = type.Next.ApplyToLeaf(newLeaf);
            if (unwrapped == type.Next) return type;
            switch (type)
            {
                case ValueArraySig valueArray:
                    return new ValueArraySig(unwrapped, valueArray.Size);
                case SZArraySig szArray:
                    return new SZArraySig(unwrapped);
                case ArraySig array:
                    return new ArraySig(unwrapped, array.Rank, array.Sizes, array.LowerBounds);
                case ByRefSig byRef:
                    return new ByRefSig(unwrapped);
                case PtrSig ptr:
                    return new PtrSig(unwrapped);
                case ModuleSig module:
                    return new ModuleSig(module.Index, unwrapped);
                case CModOptSig cModOpt:
                    return new CModOptSig(cModOpt.Modifier, unwrapped);
                case CModReqdSig cModReqd:
                    return new CModReqdSig(cModReqd.Modifier, unwrapped);
                case PinnedSig pinned:
                    return new PinnedSig(unwrapped);
                /*case ClassSig @class:
                    return new ClassSig(unwrapped.ToTypeDefOrRef());
                case CorLibTypeSig corLibType:
                    return new CorLibTypeSig(unwrapped.ToTypeDefOrRef(), corLibType.ElementType);
                case GenericInstSig genericInst:
                    return new GenericInstSig(unwrapped.ToClassOrValueTypeSig());
                case SentinelSig sentinel:
                    return new SentinelSig();
                case ValueTypeSig valueType:
                    return new ValueTypeSig(unwrapped.ToTypeDefOrRef());*/
            }
            throw new NotImplementedException("type not supported");
        }

        public static TypeDef GetDefinition(this ModuleDef module, TypeSignature type) => module.Find(type.FullName, false);
        public static FieldDef GetDefinition(this TypeDef type, FieldSignature field) => type.FindFieldCheckBaseType(field.Name, type.Module.ToSig(field), ComparerOptions);
        public static MethodDef GetDefinition(this TypeDef type, MethodSignature method) => type.FindMethodCheckBaseType(method.Name, type.Module.ToSig(method), ComparerOptions, type.Module);
        public static Parameter GetDefinition(this MethodDef method, ParameterSignature parameter) => method.Parameters[parameter.Index];
        public static bool TryGetDefinition(this ModuleDef module, TypeSignature type, out TypeDef definition) => (definition = module.GetDefinition(type)) != null;
        public static bool TryGetDefinition(this TypeDef type, FieldSignature field, out FieldDef definition) => (definition = type.GetDefinition(field)) != null;
        public static bool TryGetDefinition(this TypeDef type, MethodSignature method, out MethodDef definition) => (definition = type.GetDefinition(method)) != null;
        public static bool TryGetDefinition(this MethodDef method, ParameterSignature parameter, out Parameter definition) => (definition = method.GetDefinition(parameter)) != null;

        /*public static bool CompareSignatures(this TypeSignature signature, TypeSig reference) => signature.FullName == reference.FullName;
        public static bool CompareSignatures(this FieldSignature signature, IField reference) => signature.Name == reference.Name.String;
        public static bool CompareSignatures(this MethodSignature signature, MethodDef reference) => signature.Name == reference.Name && signature.Parameters.Zip(reference.Parameters, CompareSignatures).All(b => b);
        public static bool CompareSignatures(this ParameterSignature signature, Parameter reference) => signature.Index == reference.Index && new SigComparer(ComparerOptions).Equals(signature.ParameterType, reference.Type);*/

        public static TypeSig ToSig(this ModuleDef module, TypeSignature signature)
        {
            if (signature == TypeSignature.Void) return module.CorLibTypes.Void;
            if (signature == TypeSignature.Boolean) return module.CorLibTypes.Boolean;
            if (signature == TypeSignature.Char) return module.CorLibTypes.Char;
            if (signature == TypeSignature.SByte) return module.CorLibTypes.SByte;
            if (signature == TypeSignature.Byte) return module.CorLibTypes.Byte;
            if (signature == TypeSignature.Int16) return module.CorLibTypes.Int16;
            if (signature == TypeSignature.UInt16) return module.CorLibTypes.UInt16;
            if (signature == TypeSignature.Int32) return module.CorLibTypes.Int32;
            if (signature == TypeSignature.UInt32) return module.CorLibTypes.UInt32;
            if (signature == TypeSignature.Int64) return module.CorLibTypes.Int64;
            if (signature == TypeSignature.UInt64) return module.CorLibTypes.UInt64;
            if (signature == TypeSignature.Single) return module.CorLibTypes.Single;
            if (signature == TypeSignature.Double) return module.CorLibTypes.Double;
            if (signature == TypeSignature.String) return module.CorLibTypes.String;
            if (signature == TypeSignature.TypedReference) return module.CorLibTypes.TypedReference;
            if (signature == TypeSignature.IntPtr) return module.CorLibTypes.IntPtr;
            if (signature == TypeSignature.UIntPtr) return module.CorLibTypes.UIntPtr;
            if (signature == TypeSignature.Object) return module.CorLibTypes.Object;
            string fullName = signature.FullName;
            int index = fullName.LastIndexOf('.');
            string ns = fullName.Substring(0, index < 0 ? index = 0 : index++);
            TypeRefUser typeRef = Get(fullName.Substring(index));
            switch (signature.Kind)
            {
                case TypeKind.Class:
                    return new ClassSig(typeRef);
                case TypeKind.Value:
                    return new ValueTypeSig(typeRef);
            }
            throw new NotImplementedException("cannot create TypeSig of kind " + signature.Kind);

            TypeRefUser Get(string name)
            {
                int newIndex = name.LastIndexOf('/');
                return newIndex < 0 ? new TypeRefUser(module, ns, name) : new TypeRefUser(module, ns, name.Substring(newIndex + 1), Get(name.Substring(0, newIndex)));
            }
        }

        public static FieldSig ToSig(this ModuleDef module, FieldSignature signature) => new FieldSig(module.ToSig(signature.FieldType));
        public static MethodSig ToSig(this ModuleDef module, MethodSignature signature) => new MethodSig(signature.CallConvention.ToDNLib(), 0, module.CorLibTypes.Void, signature.Parameters.Select(p => p.ParameterType(module)).ToArray());

        public static ITypeDefOrRef ToRef(this ModuleDef module, TypeSignature signature) => module.ToSig(signature).ToTypeDefOrRef();
        public static MemberRef ToRef(this ModuleDef module, FieldSignature signature) => new MemberRefUser(module, signature.Name, module.ToSig(signature), module.ToRef(signature.DeclaringType));
        public static MemberRef ToRef(this ModuleDef module, MethodSignature signature) => new MemberRefUser(module, signature.Name, module.ToSig(signature), module.ToRef(signature.DeclaringType));

        public static TypeSignature ToSignature(this TypeSig type) => TypeSignature.Get(type.FullName, type.IsValueType ? TypeKind.Value : TypeKind.Class);
        public static FieldSignature ToFieldSignature(this MemberRef field) => field.DeclaringType.ToTypeSig().ToSignature().Field(field.Name, field.FieldSig.Type.ToSignature());
        //public static MethodSignature ToMethodSignature(this MemberRef method) => method.DeclaringType.ToTypeSig().ToSignature().Method(method.Name, method.MethodSig.CallingConvention.ToPatcher(), method.MethodSig.Params.ToArray());

        public static bool IsAssignable(this TypeSig src, TypeSig dst, AccessMode mode) => new TypeEqualityComparer(ComparerOptions).Equals(src, dst);         // TODO: Improve this check (base types, etc)

        public static CallingConvention ToDNLib(this CallConvention convention)
        {
            CallingConvention result = CallingConvention.Default;
            switch (convention & (CallConvention)CallingConvention.Mask)
            {
                case CallConvention.VarArgs:
                    result |= CallingConvention.VarArg;
                    break;
            }
            result |= (CallingConvention)convention & ~CallingConvention.Mask;
            return result;
        }

        public static CallConvention ToPatcher(this CallingConvention convention)
        {
            CallConvention result = CallConvention.Standard;
            switch (convention & CallingConvention.Mask)
            {
                case CallingConvention.VarArg:
                    result |= CallConvention.VarArgs;
                    break;
            }
            result |= (CallConvention)(convention & ~CallingConvention.Mask);
            return result;
        }

        public static void MakeAccessible(this TypeDef type, AccessLevel level)
        {
            if (type.IsAccessible(level)) return;
            TypeAttributes attributes = type.Attributes & ~TypeAttributes.VisibilityMask;
            if (type.IsNested)
            {
                switch (level)
                {
                    default:
                        return;
                    case AccessLevel.Private:
                        attributes |= TypeAttributes.NestedPrivate;
                        break;
                    case AccessLevel.FamilyAndAssembly:
                        attributes |= TypeAttributes.NestedFamANDAssem;
                        break;
                    case AccessLevel.Family:
                        attributes |= type.IsNestedAssembly ? TypeAttributes.NestedFamORAssem : TypeAttributes.NestedFamily;
                        break;
                    case AccessLevel.Assembly:
                        attributes |= type.IsNestedFamily ? TypeAttributes.NestedFamORAssem : TypeAttributes.NestedAssembly;
                        break;
                    case AccessLevel.FamilyOrAssembly:
                        attributes |= TypeAttributes.NestedFamORAssem;
                        break;
                    case AccessLevel.Public:
                        attributes |= TypeAttributes.NestedPublic;
                        break;
                }
            }
            else
            {
                switch (level)
                {
                    default:
                        return;
                    case AccessLevel.Private:
                    case AccessLevel.FamilyAndAssembly:
                    case AccessLevel.Family:
                    case AccessLevel.Assembly:
                        attributes |= TypeAttributes.NotPublic;
                        break;
                    case AccessLevel.FamilyOrAssembly:
                    case AccessLevel.Public:
                        attributes |= TypeAttributes.Public;
                        break;
                }
            }
            type.Attributes = attributes;
        }

        public static void MakeAccessible(this FieldDef field, AccessLevel level)
        {
            if (field.IsAccessible(level)) return;
            FieldAttributes attributes = field.Attributes & ~FieldAttributes.FieldAccessMask;
            switch (level)
            {
                default:
                    return;
                case AccessLevel.Private:
                    attributes |= FieldAttributes.Private;
                    break;
                case AccessLevel.FamilyAndAssembly:
                    attributes |= FieldAttributes.FamANDAssem;
                    break;
                case AccessLevel.Family:
                    attributes |= field.IsAssembly ? FieldAttributes.FamORAssem : FieldAttributes.Family;
                    break;
                case AccessLevel.Assembly:
                    attributes |= field.IsFamily ? FieldAttributes.FamORAssem : FieldAttributes.Assembly;
                    break;
                case AccessLevel.FamilyOrAssembly:
                    attributes |= FieldAttributes.FamORAssem;
                    break;
                case AccessLevel.Public:
                    attributes |= FieldAttributes.Public;
                    break;
            }
            field.Attributes = attributes;
        }

        public static void MakeAccessible(this MethodDef method, AccessLevel level)
        {
            if (method.IsAccessible(level)) return;
            MethodAttributes attributes = method.Attributes & ~MethodAttributes.MemberAccessMask;
            switch (level)
            {
                default:
                    return;
                case AccessLevel.Private:
                    attributes |= MethodAttributes.Private;
                    break;
                case AccessLevel.FamilyAndAssembly:
                    attributes |= MethodAttributes.FamANDAssem;
                    break;
                case AccessLevel.Family:
                    attributes |= method.IsAssembly ? MethodAttributes.FamORAssem : MethodAttributes.Family;
                    break;
                case AccessLevel.Assembly:
                    attributes |= method.IsFamily ? MethodAttributes.FamORAssem : MethodAttributes.Assembly;
                    break;
                case AccessLevel.FamilyOrAssembly:
                    attributes |= MethodAttributes.FamORAssem;
                    break;
                case AccessLevel.Public:
                    attributes |= MethodAttributes.Public;
                    break;
            }
            method.Attributes = attributes;
        }

        public static void MakeAccessible(this ParamDef parameter, AccessMode mode)
        {
            if (parameter.IsAccessible(mode)) return;
            ParamAttributes attributes = parameter.Attributes;
            if (mode.HasFlag(AccessMode.Read)) attributes &= ~ParamAttributes.Out;
            if (mode.HasFlag(AccessMode.Write)) attributes &= ~ParamAttributes.In;
            parameter.Attributes = attributes;
        }

        public static bool IsAccessible(this TypeDef type, AccessLevel level)
        {
            bool accessible = false;
            switch (level)
            {
                case AccessLevel.Private:
                    accessible = accessible || type.IsNestedPrivate;
                    goto case AccessLevel.FamilyAndAssembly;
                case AccessLevel.FamilyAndAssembly:
                    accessible = accessible || type.IsNestedFamilyAndAssembly;
                    goto case AccessLevel.FamilyOrAssembly;
                case AccessLevel.Family:
                    accessible = accessible || type.IsNestedFamily;
                    goto case AccessLevel.FamilyOrAssembly;
                case AccessLevel.Assembly:
                    accessible = accessible || type.IsNotPublic || type.IsNestedAssembly;
                    goto case AccessLevel.FamilyOrAssembly;
                case AccessLevel.FamilyOrAssembly:
                    accessible = accessible || type.IsNestedFamily || type.IsNotPublic || type.IsNestedAssembly || type.IsNestedFamilyOrAssembly;
                    goto case AccessLevel.Public;
                case AccessLevel.Public:
                    return accessible || type.IsPublic;
            }
            return accessible;
        }

        public static bool IsAccessible(this FieldDef field, AccessLevel level)
        {
            bool accessible = false;
            switch (level)
            {
                case AccessLevel.Private:
                    accessible = accessible || field.IsPrivate;
                    goto case AccessLevel.FamilyAndAssembly;
                case AccessLevel.FamilyAndAssembly:
                    accessible = accessible || field.IsFamilyAndAssembly;
                    goto case AccessLevel.FamilyOrAssembly;
                case AccessLevel.Family:
                    accessible = accessible || field.IsFamily;
                    goto case AccessLevel.FamilyOrAssembly;
                case AccessLevel.Assembly:
                    accessible = accessible || field.IsAssembly;
                    goto case AccessLevel.FamilyOrAssembly;
                case AccessLevel.FamilyOrAssembly:
                    accessible = accessible || field.IsFamily || field.IsAssembly || field.IsFamilyOrAssembly;
                    goto case AccessLevel.Public;
                case AccessLevel.Public:
                    return accessible || field.IsPublic;
            }
            return accessible;
        }

        public static bool IsAccessible(this MethodDef method, AccessLevel level)
        {
            bool accessible = false;
            switch (level)
            {
                case AccessLevel.Private:
                    accessible = accessible || method.IsPrivate;
                    goto case AccessLevel.FamilyAndAssembly;
                case AccessLevel.FamilyAndAssembly:
                    accessible = accessible || method.IsFamilyAndAssembly;
                    goto case AccessLevel.FamilyOrAssembly;
                case AccessLevel.Family:
                    accessible = accessible || method.IsFamily;
                    goto case AccessLevel.FamilyOrAssembly;
                case AccessLevel.Assembly:
                    accessible = accessible || method.IsAssembly;
                    goto case AccessLevel.FamilyOrAssembly;
                case AccessLevel.FamilyOrAssembly:
                    accessible = accessible || method.IsFamily || method.IsAssembly || method.IsFamilyOrAssembly;
                    goto case AccessLevel.Public;
                case AccessLevel.Public:
                    return accessible || method.IsPublic;
            }
            return accessible;
        }

        public static bool IsAccessible(this ParamDef parameter, AccessMode mode) => !(mode.HasFlag(AccessMode.Read) && parameter.IsOut || mode.HasFlag(AccessMode.Write) && parameter.IsIn);

        public static void MergeAdd<TKey, TValue>(this IDictionary<TKey, List<TValue>> dictionary, TKey key, TValue value)
        {
            if (!dictionary.TryGetValue(key, out List<TValue> list)) dictionary.Add(key, list = new List<TValue>());
            list.Add(value);
        }

        // Old Extension Methods

        /*public static IEnumerable<Instruction> ToInline(this IList<Instruction> instructions, object brOperand)
        {
            Dictionary<int, Instruction> offsetMap = new Dictionary<int, Instruction>();
            Instruction[] copy = instructions.Select(inst => inst.Copy(offsetMap)).ToArray();
            bool reqBr = false;
            Instruction previous = null;
            foreach (Instruction instruction in copy)
            {
                if (reqBr)
                {
                    previous.OpCode = OpCodes.Br;
                    previous.Operand = brOperand;
                    reqBr = false;
                }
                previous = instruction;
                if (instruction.Operand is Instruction inst) instruction.Operand = offsetMap[inst.Offset];
                else if (instruction.Operand is Instruction[] instArray) instruction.Operand = instArray.Select(inst2 => offsetMap[inst2.Offset]).ToArray();
                if (instruction.OpCode == OpCodes.Ret) reqBr = true;
            }
            if (reqBr)
            {
                previous.OpCode = OpCodes.Nop;
                previous.Operand = null;
            }
            return copy;
        }

        public static TypeAttributes ToCecil(this System.Reflection.TypeAttributes attributes) => (TypeAttributes)attributes;
        public static FieldAttributes ToCecil(this System.Reflection.FieldAttributes attributes) => (FieldAttributes)attributes;
        public static MethodAttributes ToCecil(this System.Reflection.MethodAttributes attributes) => (MethodAttributes)attributes;
        public static ParameterAttributes ToCecil(this System.Reflection.ParameterAttributes attributes) => (ParameterAttributes)attributes;

        public static OpCode ToCecil(this System.Reflection.Emit.OpCode opCode)
        {
            //return opCode.Value >> 8 == 0xFE ? ((OpCode[])typeof(OpCodes).GetField("TwoByteOpCode").GetValue(null))[opCode.Value >> 8] : ((OpCode[])typeof(OpCodes).GetField("OneByteOpCode").GetValue(null))[opCode.Value & 0xFF];
            if (opCode.Value > 0xFF) throw new NotImplementedException("cannot convert two-byte op codes");
            return ((OpCode[])typeof(OpCodes).GetField("OneByteOpCode", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly).GetValue(null))[opCode.Value & 0xFF];
        }

        public static Instruction NewInstruction(this OpCode opCode, object operand = null)
        {
            return (Instruction)Activator.CreateInstance(typeof(Instruction), BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { opCode, operand }, null);
            //return (Instruction)typeof(Instruction).GetConstructor(BindingFlags.NonPublic | BindingFlags.Static, null, new Type[] { typeof(OpCode), typeof(object) }, null).Invoke(new object[] { opCode, null });
        }

        public static Instruction GetInstruction(this List<Instruction> instructions, int offset) => (Instruction)Type.GetType("Mono.Reflection.MethodBodyReader", true).GetMethod("GetInstruction", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { instructions, offset });

        public static void MakeCompatible(this FieldDefinition definition, FieldInfo info)
        {
            if (IsCompatible(definition, info)) return;
            if (info.IsPrivate) definition.IsPrivate = true;
            if (info.IsFamilyAndAssembly) definition.IsFamilyAndAssembly = true;
            if (info.IsFamily) definition.IsFamily = true;
            if (info.IsAssembly) definition.IsAssembly = true;
            if (info.IsFamilyOrAssembly) definition.IsFamilyOrAssembly = true;
            if (info.IsPublic) definition.IsPublic = true;
        }
        public static void MakeCompatible(this MethodDefinition definition, MethodBase info)
        {
            if (IsCompatible(definition, info)) return;
            if (info.IsPrivate) definition.IsPrivate = true;
            if (info.IsFamilyAndAssembly) definition.IsFamilyAndAssembly = true;
            if (info.IsFamily) definition.IsFamily = true;
            if (info.IsAssembly) definition.IsAssembly = true;
            if (info.IsFamilyOrAssembly) definition.IsFamilyOrAssembly = true;
            if (info.IsPublic) definition.IsPublic = true;
        }

        public static bool IsCompatible(this FieldDefinition d, FieldInfo r)
        {
            bool c = r.IsPrivate;
            if (d.IsPrivate) return c;
            c = c || r.IsFamilyAndAssembly;
            return d.IsPublic || d.IsFamilyOrAssembly && (c || r.IsFamily || r.IsAssembly) || d.IsAssembly && (c || r.IsAssembly) || d.IsFamily && (c || r.IsFamily) || d.IsFamilyAndAssembly && c;
        }
        public static bool IsCompatible(this MethodDefinition d, MethodBase r)
        {
            bool c = r.IsPrivate;
            if (d.IsPrivate) return c;
            c = c || r.IsFamilyAndAssembly;
            return d.IsPublic || d.IsFamilyOrAssembly && (c || r.IsFamily || r.IsAssembly) || d.IsAssembly && (c || r.IsAssembly) || d.IsFamily && (c || r.IsFamily) || d.IsFamilyAndAssembly && c;
        }

        public static bool TryGetTypeDefinition(this ModuleDefinition module, Type type, out TypeDefinition definition) => (definition = module.GetTypes().Where(type.FullName.CompareTypes).FirstOrDefault()) != null;
        public static bool TryGetFieldDefinition(this TypeDefinition type, string name, FieldInfo field, out FieldDefinition definition) => (definition = type.Fields.Where(f => f.Name == name).FirstOrDefault()) != null || type.BaseType != null && type.BaseType.Resolve().TryGetFieldDefinition(name, field, out definition);
        public static bool TryGetMethodDefinition(this TypeDefinition type, string name, MethodBase method, out MethodDefinition definition) => (definition = type.Methods.Concat(type.Properties.SelectMany(p => p.OtherMethods.Append(p.GetMethod).Append(p.SetMethod).Where(m => m != null))).Where(m => name.CompareSignatures(method, m)).FirstOrDefault()) != null || type.BaseType != null && type.BaseType.Resolve().TryGetMethodDefinition(name, method, out definition);
        
        public static bool CompareSignatures(this string name, MethodBase info, MethodReference reference) => reference.Name == name && CompareParameters(reference.Parameters, info.GetParameters());

        public static bool CompareParameters(Collection<ParameterDefinition> a, ParameterInfo[] b)
        {
            foreach ((ParameterDefinition pa, ParameterInfo pb) in a.Zip(b, (pa, pb) => (pa, pb)))
            {
                if (!pb.ParameterType.FullName.CompareTypes(pa.ParameterType) || pa.IsOut != pb.IsOut || pa.IsIn != pb.IsIn || pa.HasDefault != pb.HasDefaultValue || pa.IsOptional != pb.IsOptional) return false;
            }
            return true;
        }

        public static bool CompareTypes(this string name, TypeReference reference) => reference.FullName == name.Replace('+', '/');

        private static Instruction Copy(this Instruction instruction, Dictionary<int, Instruction> offsetMap)
        {
            Instruction copy = instruction.OpCode.NewInstruction(instruction.Operand);
            offsetMap.Add(instruction.Offset, copy);
            return copy;
        }*/
    }
}
