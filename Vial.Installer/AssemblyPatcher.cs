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
        private readonly List<PatchConfiguration> configurations;

        public AssemblyPatcher() => configurations = new List<PatchConfiguration>();

        public void Add(PatchConfiguration configuration) => configurations.Add(configuration);
        
        public void Patch(Func<UTF8String, ModuleDef> loader)
        {
            foreach (PatchConfiguration patch in configurations)
            {
                patch.Patch(loader(patch.TargetModule));
                foreach (string required in patch.RequiredModules) patch.PatchDependency(loader(required));
                patch.Validate();
            }
        }
    }

    class PatchConfiguration
    {       // TODO Replace string with UTF8String wherever necessary
        protected const SigComparerOptions ComparerOptions = SigComparerOptions.IgnoreModifiers | SigComparerOptions.DontCompareTypeScope | SigComparerOptions.DontCompareReturnType;

        public string PatchName { get; }
        public string TargetModule { get; }
        public IEnumerable<string> RequiredModules { get; }

        protected readonly HashSet<string> dependencyModules;
        protected readonly Dictionary<TypeSignature, TypeSignature> typeSubstitutions;
        protected readonly Dictionary<FieldSignature, FieldSignature> fieldSubstitutions;
        protected readonly Dictionary<MethodSignature, MethodSignature> methodSubstitutions;
        protected readonly Dictionary<MethodSignature, List<Action<MethodDef>>> mixins;
        protected readonly List<TypeInject> typeInjects;
        protected readonly List<TypeDependency> typeDependencies;
        protected readonly List<TypeMixin> typeMixins;

        public event Action<ModuleDef> PatchModule;

        public PatchConfiguration(string patchName, string targetAssembly, IEnumerable<string> requiredAssemblies)
        {
            PatchName = patchName;
            TargetModule = targetAssembly;
            RequiredModules = requiredAssemblies;
            dependencyModules = new HashSet<string>();
            typeDependencies = new List<TypeDependency>();
            typeSubstitutions = new Dictionary<TypeSignature, TypeSignature>();
            fieldSubstitutions = new Dictionary<FieldSignature, FieldSignature>();
            methodSubstitutions = new Dictionary<MethodSignature, MethodSignature>();
            mixins = new Dictionary<MethodSignature, List<Action<MethodDef>>>();
            typeInjects = new List<TypeInject>();
            typeDependencies = new List<TypeDependency>();
            typeMixins = new List<TypeMixin>();
        }

        public void AddInjected(TypeInject inject) => typeInjects.Add(inject);
        public void AddDependency(TypeDependency dependency) => typeDependencies.Add(dependency);
        public void AddMixin(TypeMixin mixin)
        {
            typeMixins.Add(mixin);
            foreach (MethodMixin mm in mixin.Mixins)
            {
                if (!mixins.TryGetValue(mm.Signature, out List<Action<MethodDef>> actions)) mixins.Add(mm.Signature, actions = new List<Action<MethodDef>>());
                actions.Add(mm.Mixin);
            }
        }

        public void AddSubstitution(TypeSignature original, TypeSignature substitution) => typeSubstitutions.Add(original, substitution);
        public void AddSubstitution(FieldSignature original, FieldSignature substitution) => fieldSubstitutions.Add(original, substitution);
        public void AddSubstitution(MethodSignature original, MethodSignature substitution) => methodSubstitutions.Add(original, substitution);
        public void AddMixin(MethodSignature target, Action<MethodDef> mixin) => mixins.MergeAdd(target, mixin);

        public bool TryResolve(ModuleDef module, TypeSignature signature, out TypeDef def) => module.TryGetDefinition(typeSubstitutions.Substitute(signature), out def);

        public bool TryResolve(ModuleDef module, FieldSignature signature, out FieldDef def) => (def = null) == null && TryResolve(module, (signature = fieldSubstitutions.Substitute(signature)).DeclaringType, out TypeDef type) && type.TryGetDefinition(signature, out def);

        public bool TryResolve(ModuleDef module, MethodSignature signature, out MethodDef def) => (def = null) == null && TryResolve(module, (signature = SubstituteArguments(module, methodSubstitutions.Substitute(signature))).DeclaringType, out TypeDef type) && type.TryGetDefinition(signature, out def);

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

        internal void Patch(ModuleDef module)
        {
            List<TypeDependency> owned = PatchDependency(module, typeDependencies.Concat(typeMixins.Select(m => m.Dependency)));
            typeDependencies.RemoveAll(owned.Contains);
            typeMixins.RemoveAll(m => owned.Contains(m.Dependency));
            HashSet<MethodSig> originals = new HashSet<MethodSig>();
            foreach (MethodSig original in typeDependencies.SelectMany(t => t.Originals.Select(module.ToSig))) originals.Add(original);
            foreach (KeyValuePair<MethodSignature, List<Action<MethodDef>>> method in mixins)
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

        internal void PatchDependency(ModuleDef module)
        {
            List<TypeDependency> owned = PatchDependency(module, typeDependencies);
            typeDependencies.RemoveAll(owned.Contains);
        }

        internal void Validate()
        {
            if (typeDependencies.Count > 0) throw new PatchException("failed to locate all dependencies");
            if (typeMixins.Count > 0) throw new PatchException("failed to locate all mixed types");
        }

        protected List<TypeDependency> PatchDependency(ModuleDef module, IEnumerable<TypeDependency> dependencies)
        {
            List<TypeDependency> owned = new List<TypeDependency>();
            foreach (TypeDependency type in dependencies)
            {
                if (!TryResolve(module, type.Descriptor.Signature, out TypeDef typeDef)) continue;
                owned.Add(type);
                typeDef.MakeAccessible(type.Descriptor.AccessLevel);
                foreach (FieldDescriptor field in type.FieldDependencies)
                {
                    FieldDef fieldDef = Resolve(module, field.Signature);
                    fieldDef.MakeAccessible(field.AccessLevel);
                    fieldDef.IsInitOnly &= field.IsInitOnly;
                }
                foreach (MethodDescriptor method in type.MethodDependencies)
                {
                    MethodDef methodDef = Resolve(module, method.Signature);
                    methodDef.MakeAccessible(method.AccessLevel);
                    if (method.ReturnType != null && !module.ToSig(typeSubstitutions.Substitute(method.ReturnType)).IsAssignable(methodDef.ReturnType, AccessMode.Write)) throw new PatchException("dependency and original have incompatible types");
                    // TODO: Add other checks (e.g. virtual)
                    foreach (ParameterDescriptor parameter in method.Parameters)
                    {
                        ParamDef parameterDef = Resolve(module, parameter.Signature);
                        parameterDef.MakeAccessible(parameter.AccessMode);
                    }
                }
            }
            return owned;
        }

        protected TypeSig Substitute(TypeSig sig) => sig.ApplyToLeaf(s => sig.Module.ToSig(typeSubstitutions.Substitute(s.ToSignature())));

        protected MethodSignature SubstituteArguments(ModuleDef module, MethodSignature signature)
        {
            bool changed = false;
            List<Func<ModuleDef, TypeSig>> newParams = new List<Func<ModuleDef, TypeSig>>();
            SigComparer comparer = new SigComparer(ComparerOptions);
            foreach (ParameterSignature param in signature.Parameters)
            {
                TypeSig typeSub = Substitute(param.ParameterType(module));
                if (!comparer.Equals(typeSub, param.ParameterType(module))) changed = true;
                newParams.Add(Safe(typeSub));
            }
            return changed ? signature.DeclaringType.Method(signature.Name, signature.CallConvention, newParams) : signature;

            Func<ModuleDef, TypeSig> Safe(TypeSig sig) => m =>
            {
                if (module != m) throw new ArgumentException("incompatible module");
                return sig;
            };
        }

        // TODO Check for collisions (=> change member name) & ensure this runs after CreateDefinition(ModuleDef, TypeSignature)
        protected FieldDef CreateDefinition(ModuleDef module, FieldSignature signature) => new FieldDefUser(signature.Name, module.ToSig(signature))
        {
            DeclaringType = Resolve(module, signature.DeclaringType)
        };

        protected MethodDef CreateDefinition(ModuleDef module, MethodSignature signature) => new MethodDefUser(signature.Name, module.ToSig(signature))
        {
            DeclaringType = Resolve(module, signature.DeclaringType)
        };

        protected interface IInjecter
        {
            IAppliedInjecter Create(ModuleDef module);
        }

        protected interface IAppliedInjecter
        {
            void Inject();
        }

        protected class Injecter<TSig, TDef> : IInjecter where TSig : class where TDef : class
        {
            private readonly TSig signature;
            private readonly Action<TDef> injecter;
            private readonly Func<ModuleDef, TSig, TDef> factory;

            public Injecter(TSig signature, Action<TDef> injecter, Func<ModuleDef, TSig, TDef> factory)
            {
                this.signature = signature;
                this.injecter = injecter;
                this.factory = factory;
            }

            public IAppliedInjecter Create(ModuleDef module) => new AppliedInjecter<TDef>(injecter, factory(module, signature));
        }

        protected class AppliedInjecter<TDef> : IAppliedInjecter where TDef : class
        {
            private readonly Action<TDef> injecter;
            private readonly TDef definition;

            public AppliedInjecter(Action<TDef> injecter, TDef definition)
            {
                this.injecter = injecter;
                this.definition = definition;
            }

            public void Inject() => injecter(definition);
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
