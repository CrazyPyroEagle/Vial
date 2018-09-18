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

        public static Action<MethodDef> ApplyMixin(this MethodBase mixin, AssemblyPatcher patcher) => (MethodDef original) =>
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
                        inst.Operand = patcher.ResolveOrImport(original.Module, field);
                        break;
                    case MethodBase method:
                        if (method.IsDefined(typeof(BaseDependencyAttribute)))
                        {
                            useBase = true;
                            inst.Operand = baseCopy;
                            break;
                        }
                        inst.Operand = patcher.ResolveOrImport(original.Module, method);
                        break;
                    case Type type:
                        inst.Operand = patcher.ResolveOrImport(original.Module, type);
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

        public static IType ResolveOrImport(this AssemblyPatcher patcher, ModuleDef module, Type type)
        {
            if (patcher.TryResolve(module, type.ToSignature(), out TypeDef def)) return def;
            ITypeDefOrRef typeRef = module.Import(type);
            typeRef.Name = type.GetName().SimpleName();
            return typeRef;
        }

        public static IField ResolveOrImport(this AssemblyPatcher patcher, ModuleDef module, FieldInfo field)
        {
            if (patcher.TryResolve(module, field.ToSignature(), out FieldDef def)) return def;
            MemberRef fieldRef = module.Import(field);
            fieldRef.Name = field.GetName();
            return fieldRef;
        }

        public static IMethod ResolveOrImport(this AssemblyPatcher patcher, ModuleDef module, MethodBase method)
        {
            if (patcher.TryResolve(module, method.ToSignature(), out MethodDef def)) return def;
            IMethod methodRef = module.Import(method);
            methodRef.Name = method.GetName();
            return methodRef;
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
        public static MethodSignature ToSignature(this MethodBase method) => method.DeclaringType.ToSignature().Method(method.GetName(), method.CallingConvention.ToPatcher(), method.GetParameters().Select(pi => pi.ParameterType.ToSignature()));
        public static ParameterSignature ToSignature(this ParameterInfo parameter) => ((MethodInfo)parameter.Member).ToSignature().Parameters[parameter.Position];

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
