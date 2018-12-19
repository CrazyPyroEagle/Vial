using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using MethodBody = System.Reflection.MethodBody;
using MethodAttributes = dnlib.DotNet.MethodAttributes;
using Vial.Mixins;

namespace Vial.Installer
{
    static class ReflectionExtensions
    {
        private const MethodAttributes CopyMask = MethodAttributes.RequireSecObject | MethodAttributes.HideBySig | MethodAttributes.Static;
        private const MethodAttributes CopyAttr = MethodAttributes.PrivateScope | MethodAttributes.SpecialName;

        public static Action<MethodDef> ApplyMixin(this MethodBase mixin, PatchConfiguration patch) => original =>
        {
            CilBody originalBody = original.Body;
            MethodBody mixinBody = mixin.GetMethodBody();
            List<Instruction> originalIL = new List<Instruction>(originalBody.Instructions);
            List<Local> originalLocals = new List<Local>(originalBody.Variables);
            originalBody.Variables.Clear();
            foreach (LocalVariableInfo local in mixinBody.LocalVariables.OrderBy(lvi => lvi.LocalIndex)) originalBody.Variables.Add(original.Module.ToDNLib(local));
            List<Instruction> mixinIL = new CilParser(mixin.Module, originalBody.Variables, mixinBody.GetILAsByteArray()).Parse();
            mixinIL.SimplifyMacros(originalBody.Variables, original.Parameters);
            IList<Instruction> newIL = originalBody.Instructions;
            newIL.Clear();
            int ilStart = 0;
            if (mixin.IsConstructor)
            {
                foreach (Instruction inst in mixin.IsDefined(typeof(RewriteBaseAttribute)) ? mixinIL : originalIL)
                {
                    ilStart++;
                    newIL.Add(inst);
                    if (inst.OpCode.FlowControl == FlowControl.Call) break;
                }
                RemoveCall(originalIL);
                RemoveCall(mixinIL);
            }
            MethodDef baseCopy = new MethodDefUser(original.DeclaringType.FindUnusedMethodName(original.Name + "<Base>$"), original.MethodSig, original.ImplAttributes, original.Attributes & CopyMask | CopyAttr);
            bool useBase = false;
            foreach (Instruction inst in mixinIL)
            {
                switch (inst.Operand)
                {
                    case FieldInfo field:
                        inst.Operand = patch.ResolveOrImport(original.Module, field);
                        break;
                    case MethodBase method:
                        if (method.IsDefined(typeof(BaseDependencyAttribute)))
                        {
                            useBase = true;
                            inst.Operand = baseCopy;
                            break;
                        }
                        inst.Operand = patch.ResolveOrImport(original.Module, method);
                        break;
                    case Type type:
                        inst.Operand = patch.ResolveOrImport(original.Module, type);
                        break;
                    case byte[] blob:
                        throw new NotImplementedException("how do you import this?");
                    case MemberInfo member:
                        throw new NotImplementedException("how do you import this?");
                }
                newIL.Add(inst);
            }
            if (useBase)
            {
                baseCopy.Body = new CilBody(originalBody.InitLocals, originalIL, new List<ExceptionHandler>(originalBody.ExceptionHandlers), originalBody.Variables);
                original.DeclaringType.Methods.Add(baseCopy);
            }
            originalBody.ExceptionHandlers.Clear();
            foreach (ExceptionHandlingClause ehc in mixinBody.ExceptionHandlingClauses) originalBody.ExceptionHandlers.Add(ehc.ToDNLib(original.Module, newIL));
            originalBody.OptimizeMacros();

            void RemoveCall(List<Instruction> il)
            {
                for (int index = 0; index < il.Count; index++)
                {
                    if (il[index].OpCode.FlowControl != FlowControl.Call) continue;
                    il.RemoveRange(0, index + 1);
                    break;
                }
            }
        };

        public static Action<MethodDef> ApplyInject(this MethodBase inject, PatchConfiguration patch) => stub =>
        {
            CilBody stubBody = stub.Body;
            MethodBody injectBody = inject.GetMethodBody();
            List<Instruction> originalIL = new List<Instruction>(stubBody.Instructions);
            List<Local> originalLocals = new List<Local>(stubBody.Variables);
            stubBody.Variables.Clear();
            foreach (LocalVariableInfo local in injectBody.LocalVariables.OrderBy(lvi => lvi.LocalIndex)) stubBody.Variables.Add(stub.Module.ToDNLib(local));
            List<Instruction> mixinIL = new CilParser(inject.Module, stubBody.Variables, injectBody.GetILAsByteArray()).Parse();
            mixinIL.SimplifyMacros(stubBody.Variables, stub.Parameters);
            IList<Instruction> newIL = stubBody.Instructions;
            newIL.Clear();
            foreach (Instruction inst in mixinIL)
            {
                switch (inst.Operand)
                {
                    case FieldInfo field:
                        inst.Operand = patch.ResolveOrImport(stub.Module, field);
                        break;
                    case MethodBase method:
                        if (method.IsDefined(typeof(BaseDependencyAttribute))) throw new InvalidOperationException("attempt to inject a body with a base dependency call");
                        inst.Operand = patch.ResolveOrImport(stub.Module, method);
                        break;
                    case Type type:
                        inst.Operand = patch.ResolveOrImport(stub.Module, type);
                        break;
                    case byte[] blob:
                        throw new NotImplementedException("how do you import this?");
                    case MemberInfo member:
                        throw new NotImplementedException("how do you import this?");
                }
                newIL.Add(inst);
            }
            stubBody.ExceptionHandlers.Clear();
            foreach (ExceptionHandlingClause ehc in injectBody.ExceptionHandlingClauses) stubBody.ExceptionHandlers.Add(ehc.ToDNLib(stub.Module, newIL));
            stubBody.OptimizeMacros();
        };

        public static ITypeDefOrRef ResolveOrImport(this PatchConfiguration patch, ModuleDef module, Type type)
        {
            if (patch.TryResolve(module, type.ToSignature(), out TypeDef def)) return def;
            type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(type.GetName())).First(t => t != null);
            return module.Import(type);
            /*string fullName = type.GetName();
            int nsEnd = fullName.LastIndexOf('.');
            string ns = null, name = fullName;
            if (nsEnd >= 0)
            {
                ns = fullName.Substring(0, nsEnd);
                name = fullName.Substring(nsEnd + 1);
            }
            return new TypeRefUser(module, ns, name, new AssemblyNameInfo(type.Assembly.GetName()).ToAssemblyRef());//new AssemblyRefUser(module, type.Module.Name.Substring(0, type.Module.Name.Length - 4)));*/
        }

        public static IField ResolveOrImport(this PatchConfiguration patch, ModuleDef module, FieldInfo field)
        {
            if (patch.TryResolve(module, field.ToSignature(), out FieldDef def)) return def;
            MemberRef fieldRef = module.Import(field);
            fieldRef.Name = field.GetName();
            return fieldRef;
        }

        public static IMethod ResolveOrImport(this PatchConfiguration patch, ModuleDef module, MethodBase method)
        {
            if (patch.TryResolve(module, method.ToSignature(), out MethodDef def)) return def;
            return new MemberRefUser(module, method.GetName(), module.ToSig(method.ToSignature()), patch.ResolveOrImport(module, method.DeclaringType));
        }

        public static UTF8String FindUnusedMethodName(this TypeDef type, UTF8String baseName)
        {
            for (ulong index = 0uL; index < ulong.MaxValue; index++)
            {
                UTF8String proposed = baseName + index;
                if (type.Methods.All(m => m.Name != proposed)) return proposed;
            }
            throw new ArgumentException("could not find unused method name for " + type + " with base name " + baseName);
        }

        public static AccessLevel GetAccessLevel(this TypeInfo type) => type.IsNestedPrivate ? AccessLevel.Private : type.IsNestedFamANDAssem ? AccessLevel.FamilyAndAssembly : type.IsNestedFamily ? AccessLevel.Family : type.IsNotPublic || type.IsNestedAssembly ? AccessLevel.Assembly : type.IsNestedFamORAssem ? AccessLevel.FamilyOrAssembly : AccessLevel.Public;
        public static AccessLevel GetAccessLevel(this FieldInfo field) => field.IsPrivate ? AccessLevel.Private : field.IsFamilyAndAssembly ? AccessLevel.FamilyAndAssembly : field.IsFamily ? AccessLevel.Family : field.IsAssembly ? AccessLevel.Assembly : field.IsFamilyOrAssembly ? AccessLevel.FamilyOrAssembly : AccessLevel.Public;
        public static AccessLevel GetAccessLevel(this MethodBase method) => method.IsPrivate ? AccessLevel.Private : method.IsFamilyAndAssembly ? AccessLevel.FamilyAndAssembly : method.IsFamily ? AccessLevel.Family : method.IsAssembly ? AccessLevel.Assembly : method.IsFamilyOrAssembly ? AccessLevel.FamilyOrAssembly : AccessLevel.Public;
        public static AccessMode GetAccessMode(this ParameterInfo parameter) => (parameter.IsIn ? AccessMode.None : AccessMode.Write) | (parameter.IsOut ? AccessMode.None : AccessMode.Read);

        public static CallConvention ToPatcher(this CallingConventions conventions) => (CallConvention)conventions;
        public static ExceptionHandlerType ToDNLib(this ExceptionHandlingClauseOptions options) => (ExceptionHandlerType)options;
        public static ExceptionHandler ToDNLib(this ExceptionHandlingClause clause, ModuleDef module, IList<Instruction> instructions)
        {
            ExceptionHandlerType type = clause.Flags.ToDNLib();
            ExceptionHandler result = new ExceptionHandler(type);
            if (clause.CatchType != null) result.CatchType = module.ToSig(clause.CatchType.ToSignature()).ToTypeDefOrRef();
            if (type.HasFlag(ExceptionHandlerType.Filter)) result.FilterStart = instructions.AtOffset(clause.FilterOffset);
            result.HandlerStart = instructions.AtOffset(clause.HandlerOffset);
            result.HandlerEnd = instructions.AtOffset(clause.HandlerOffset + clause.HandlerLength);
            result.TryStart = instructions.AtOffset(clause.TryOffset);
            result.TryEnd = instructions.AtOffset(clause.TryOffset + clause.TryLength);
            return result;
        }
        public static Local ToDNLib(this ModuleDef module, LocalVariableInfo local) => new Local(module.ToSig(local.LocalType.ToSignature()));

        public static TypeSignature ToMixinSignature(this Type type) => TypeSignature.Get(type.FullName.Replace('+', '/'), type.IsValueType ? TypeKind.Value : TypeKind.Class);
        public static TypeSignature ToSignature(this Type type) => TypeSignature.Get(type.GetName(), type.IsValueType ? TypeKind.Value : TypeKind.Class);

        public static FieldSignature ToSignature(this FieldInfo field) => field.DeclaringType.ToSignature().Field(field.GetName(), field.FieldType.ToSignature());
        public static MethodSignature ToSignature(this MethodBase method) => method.DeclaringType.ToSignature().Method(method.GetName(), method.CallingConvention.ToPatcher(), method.GetParameters().Select(pi => pi.ParameterType.ToSig()));
        public static ParameterSignature ToSignature(this ParameterInfo parameter) => ((MethodInfo)parameter.Member).ToSignature().Parameters[parameter.Position];

        public static Func<ModuleDef, TypeSig> ToSig(this Type type) => module =>
        {
            if (type == typeof(void)) return module.CorLibTypes.Void;
            if (type == typeof(bool)) return module.CorLibTypes.Boolean;
            if (type == typeof(char)) return module.CorLibTypes.Char;
            if (type == typeof(sbyte)) return module.CorLibTypes.SByte;
            if (type == typeof(byte)) return module.CorLibTypes.Byte;
            if (type == typeof(short)) return module.CorLibTypes.Int16;
            if (type == typeof(ushort)) return module.CorLibTypes.UInt16;
            if (type == typeof(int)) return module.CorLibTypes.Int32;
            if (type == typeof(uint)) return module.CorLibTypes.UInt32;
            if (type == typeof(long)) return module.CorLibTypes.Int64;
            if (type == typeof(ulong)) return module.CorLibTypes.UInt64;
            if (type == typeof(float)) return module.CorLibTypes.Single;
            if (type == typeof(double)) return module.CorLibTypes.Double;
            if (type == typeof(string)) return module.CorLibTypes.String;
            if (type == typeof(TypedReference)) return module.CorLibTypes.TypedReference;
            if (type == typeof(IntPtr)) return module.CorLibTypes.IntPtr;
            if (type == typeof(UIntPtr)) return module.CorLibTypes.UIntPtr;
            if (type == typeof(object)) return module.CorLibTypes.Object;

            Type element = type.GetElementType();
            if (element == null) return type.IsValueType ? (TypeSig)new ValueTypeSig(Ref(type)) : new ClassSig(Ref(type));
            TypeSig next = element.ToSig()(module);
            if (type.IsArray) return type.GetArrayRank() == 1 ? (TypeSig)new SZArraySig(next) : new ArraySig(next);
            if (type.IsPointer) return new PtrSig(next);
            throw new ArgumentException("unrecognised type");

            TypeRefUser Ref(Type getType)
            {
                string fullName = getType.FullName.Replace('+', '/');
                int index = fullName.LastIndexOf('.');
                string ns = fullName.Substring(0, index < 0 ? index = 0 : index++);
                return GetRef(ns, fullName.Substring(index));
            }

            TypeRefUser GetRef(string ns, string name)
            {
                int newIndex = name.LastIndexOf('/');
                return newIndex < 0 ? new TypeRefUser(module, ns, name, module) : new TypeRefUser(module, "", name.Substring(newIndex + 1), GetRef(ns, name.Substring(0, newIndex)));
            }
        };

        public static string GetName(this Type type) => type.GetCustomAttribute<NameAttribute>()?.Target ?? type.FullName.Replace('+', '/');
        public static string GetName(this MemberInfo member) => member.GetCustomAttribute<NameAttribute>()?.Target ?? member.Name;
        public static string SimpleName(this string fullName)
        {
            int index = fullName.LastIndexOf('.');
            return index == -1 ? fullName : fullName.Substring(index + 1);
        }

        public static void LoadReferencedAssemblies(this Assembly assembly)
        {
            foreach (AssemblyName name in assembly.GetReferencedAssemblies()) Assembly.Load(name);
        }

        public static DataReader ToDataStream(this byte[] bytes) => new DataReader(DataStreamFactory.Create(bytes), 0u, (uint)bytes.LongLength);

        public static Instruction AtOffset(this IList<Instruction> instructions, long offset)
        {
            foreach (Instruction inst in instructions)
            {
                if (offset == inst.Offset) return inst;
                else if (offset < inst.Offset) throw new ArgumentException("offset in the middle of instruction");
            }
            return null;
        }
    }
}
