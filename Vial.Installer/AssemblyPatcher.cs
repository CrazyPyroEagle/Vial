using dnlib.DotNet;
using dnlib.DotNet.Emit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Vial.Installer
{
    class AssemblyPatcher
    {
        private const SigComparerOptions ComparerOptions = SigComparerOptions.IgnoreModifiers | SigComparerOptions.DontCompareTypeScope | SigComparerOptions.DontCompareReturnType;

        private readonly List<TypeDependency> typeDependencies;
        private readonly Dictionary<TypeSignature, TypeSignature> typeSubstitutions;
        private readonly Dictionary<FieldSignature, FieldSignature> fieldSubstitutions;
        private readonly Dictionary<MethodSignature, MethodSignature> methodSubstitutions;
        private readonly Dictionary<MethodSignature, List<Action<MethodDef>>> mixins;

        public AssemblyPatcher()
        {
            typeDependencies = new List<TypeDependency>();
            typeSubstitutions = new Dictionary<TypeSignature, TypeSignature>(new GenericEqualityComparer<TypeSignature>((a, b) => a == b));
            fieldSubstitutions = new Dictionary<FieldSignature, FieldSignature>(new GenericEqualityComparer<FieldSignature>((a, b) => a == b));
            methodSubstitutions = new Dictionary<MethodSignature, MethodSignature>(new GenericEqualityComparer<MethodSignature>((a, b) => a == b));
            mixins = new Dictionary<MethodSignature, List<Action<MethodDef>>>();
        }

        public event Action<ModuleDef> PatchModule;

        public void AddDependency(TypeDependency dependency) => typeDependencies.Add(dependency);

        public void AddSubstitution(TypeSignature original, TypeSignature substitution) => typeSubstitutions.Add(original, substitution);
        public void AddSubstitution(FieldSignature original, FieldSignature substitution) => fieldSubstitutions.Add(original, substitution);
        public void AddSubstitution(MethodSignature original, MethodSignature substitution) => methodSubstitutions.Add(original, substitution);
        public void AddMixin(MethodSignature target, Action<MethodDef> mixin) => mixins.MergeAdd(target, mixin);

        public void Patch(ModuleDef module)
        {
            HashSet<MethodSig> originals = new HashSet<MethodSig>();
            foreach (TypeDependency type in typeDependencies)
            {
                TypeDef typeDef = Resolve(module, type.Signature);
                typeDef.MakeAccessible(type.AccessLevel);
                foreach (FieldDependency field in type.Fields)
                {
                    FieldDef fieldDef = Resolve(module, field.Signature);
                    fieldDef.MakeAccessible(field.AccessLevel);
                    fieldDef.IsInitOnly &= field.IsInitOnly;
                }
                foreach (MethodDependency method in type.Methods)
                {
                    MethodDef methodDef = Resolve(module, method.Signature);
                    methodDef.MakeAccessible(method.AccessLevel);
                    if (method.ReturnType != null) module.ToSig(method.ReturnType).IsAssignable(methodDef.ReturnType, AccessMode.Write);
                    // TODO: Add other checks (e.g. virtual)
                    foreach (ParameterDependency parameter in method.Parameters)
                    {
                        ParamDef parameterDef = Resolve(module, parameter.Signature);
                        parameterDef.MakeAccessible(parameter.AccessMode);
                    }
                }
                foreach (MethodSig original in type.Originals.Select(module.ToSig)) originals.Add(original);
            }
            foreach (KeyValuePair<MethodSignature, List<Action<MethodDef>>> method in this.mixins)
            {
                MethodDef methodDef = Resolve(module, method.Key);
                foreach (Action<MethodDef> action in method.Value) action(methodDef);
            }
            PatchModule?.Invoke(module);
            TypeEqualityComparer typeComparer = new TypeEqualityComparer(ComparerOptions);
            SignatureEqualityComparer comparer = new SignatureEqualityComparer(ComparerOptions);
            Dictionary<TypeSig, TypeSig> typeSubstitutions = this.typeSubstitutions.GroupBy(p => module.ToSig(p.Key), p => module.ToSig(p.Value), typeComparer).Where(g => !typeComparer.Equals(g.Key, g.First())).ToDictionary(g => g.Key, Enumerable.First, typeComparer);
            Dictionary<FieldSig, FieldSig> fieldSubstitutions = this.fieldSubstitutions.GroupBy(p => module.ToSig(p.Key), p => module.ToSig(p.Value), comparer).Where(g => !comparer.Equals(g.Key, g.First())).ToDictionary(g => g.Key, Enumerable.First, comparer);
            Dictionary<MethodSig, MethodSig> methodSubstitutions = this.methodSubstitutions.GroupBy(p => module.ToSig(p.Key), p => module.ToSig(p.Value), comparer).Where(g => !comparer.Equals(g.Key, g.First())).ToDictionary(g => g.Key, Enumerable.First, comparer);
            foreach (TypeDef type in module.GetTypes())
            {
                foreach (FieldDef field in type.Fields)
                {
                    while (fieldSubstitutions.TryGetValue(field.FieldSig, out FieldSig newFieldSig)) field.FieldSig = newFieldSig;
                    field.FieldType = field.FieldType.ApplyToLeaf(typeSubstitutions.Substitute);
                }
                foreach (MethodDef method in type.Methods)
                {
                    method.MethodSig = methodSubstitutions.Substitute(method.MethodSig);
                    foreach (Parameter parameter in method.Parameters) parameter.Type = parameter.Type.ApplyToLeaf(typeSubstitutions.Substitute);
                    method.ReturnType = method.ReturnType.ApplyToLeaf(typeSubstitutions.Substitute);
                }
            }
        }

        public bool TryResolve(ModuleDef module, TypeSignature signature, out TypeDef def) => module.TryGetDefinition(typeSubstitutions.Substitute(signature), out def);

        public bool TryResolve(ModuleDef module, FieldSignature signature, out FieldDef def) => (def = null) == null && TryResolve(module, (signature = fieldSubstitutions.Substitute(signature)).DeclaringType, out TypeDef type) && type.TryGetDefinition(signature, out def);

        public bool TryResolve(ModuleDef module, MethodSignature signature, out MethodDef def)
        {
            return (def = null) == null && TryResolve(module, (signature = methodSubstitutions.Substitute(signature)).DeclaringType, out TypeDef type) && type.TryGetDefinition(signature, out def);
        }

        public bool TryResolve(ModuleDef module, ParameterSignature signature, out ParamDef def)
        {
            def = null;
            if (!TryResolve(module, signature.DeclaringMethod, out MethodDef method) || !method.TryGetDefinition(signature, out Parameter definition)) return false;
            if (!definition.HasParamDef) definition.CreateParamDef();
            def = definition.ParamDef;
            return true;
        }

        public TypeDef Resolve(ModuleDef module, TypeSignature signature)
        {
            if (!TryResolve(module, signature, out TypeDef definition)) throw new PatchException("missing type " + signature);
            return definition;
        }

        public FieldDef Resolve(ModuleDef module, FieldSignature signature)
        {
            if (!TryResolve(module, signature, out FieldDef definition)) throw new PatchException("missing field " + signature);
            return definition;
        }

        public MethodDef Resolve(ModuleDef module, MethodSignature signature)
        {
            if (!TryResolve(module, signature, out MethodDef definition)) throw new PatchException("missing method " + signature);
            return definition;
        }

        public ParamDef Resolve(ModuleDef module, ParameterSignature signature)
        {
            if (!TryResolve(module, signature, out ParamDef definition)) throw new PatchException("missing parameter " + signature);
            return definition;
        }

        private MethodSignature SubstituteArguments(MethodSignature signature)
        {
            bool changed = false;
            List<TypeSignature> newParams = new List<TypeSignature>();
            foreach (ParameterSignature param in signature.Parameters)
            {
                if (typeSubstitutions.TryGetValue(param.ParameterType, out TypeSignature typeSub))
                {
                    changed = true;
                    newParams.Add(typeSubstitutions.Substitute(typeSub));
                    newParams.Add(typeSub);
                    break;
                }
                newParams.Add(param.ParameterType);
            }
            return changed ? signature.DeclaringType.Method(signature.Name, signature.CallConvention, newParams) : signature;
        }

        private class GenericEqualityComparer<T> : IEqualityComparer<T>
        {
            private readonly Func<T, T, bool> func;

            public GenericEqualityComparer(Func<T, T, bool> func) => this.func = func;

            public bool Equals(T x, T y) => func(x, y);

            public int GetHashCode(T obj) => obj.GetHashCode();
        }
    }

    class PatchException : Exception
    {
        public PatchException() { }

        public PatchException(string message) : base(message) { }
    }
}
