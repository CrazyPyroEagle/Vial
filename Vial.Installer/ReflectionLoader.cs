using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vial.Mixins;

namespace Vial.Installer
{
    class ReflectionLoader
    {
        private readonly AssemblyPatcher patcher;

        public ReflectionLoader(AssemblyPatcher patcher) => this.patcher = patcher;

        public void Add(Assembly assembly)
        {
            assembly.LoadReferencedAssemblies();
            foreach (TypeInfo type in assembly.DefinedTypes.Where(t => t.IsDefined(typeof(MixinAttribute))))
            {
                TypeSignature mixinTypeSig = type.ToMixinSignature();
                TypeSignature typeSig = type.ToSignature();
                patcher.AddSubstitution(mixinTypeSig, typeSig);
                TypeDependency.Builder typeDep = new TypeDependency.Builder(typeSig)
                {
                    AccessLevel = type.GetAccessLevel()
                };
                foreach (FieldInfo field in type.DeclaredFields.Where(f => !f.IsDefined(typeof(TransparentAttribute))))
                {
                    FieldSignature fieldSig = typeSig.Field(field.GetName(), field.FieldType.ToSignature());
                    patcher.AddSubstitution(mixinTypeSig.Field(field.GetName(), field.FieldType.ToMixinSignature()), fieldSig);
                    FieldDependency.Builder fieldDep = typeDep.Field(fieldSig);
                    fieldDep.AccessLevel = field.GetAccessLevel();
                    fieldDep.IsInitOnly = field.IsInitOnly;
                }
                HashSet<MethodBase> implicitDependencies = new HashSet<MethodBase>();
                foreach (PropertyInfo property in type.DeclaredProperties)
                {
                    if (property.IsDefined(typeof(DependencyAttribute)))
                    {
                        foreach (MethodBase accessor in property.GetAccessors(true)) implicitDependencies.Add(accessor);
                        continue;
                    }
                }
                foreach (MethodBase method in type.DeclaredMethods.Concat<MethodBase>(type.DeclaredConstructors).Where(m => !m.IsDefined(typeof(TransparentAttribute))))
                {
                    IEnumerable<TypeSignature> parameterTypes = method.GetParameters().Select(p => p.ParameterType.ToSignature()).ToArray();
                    MethodSignature mixinMethodSig = mixinTypeSig.Method(method.GetName(), method.CallingConvention.ToPatcher(), method.GetParameters().Select(p => p.ParameterType.ToMixinSignature()).ToArray());
                    if (method.IsDefined(typeof(BaseDependencyAttribute)))
                    {
                        typeDep.Original(mixinMethodSig);
                        continue;
                    }
                    MethodSignature methodSig = typeSig.Method(method.GetName(), method.CallingConvention.ToPatcher(), parameterTypes);
                    patcher.AddSubstitution(mixinMethodSig, methodSig);
                    MethodDependency.Builder methodDep = typeDep.Method(methodSig);
                    methodDep.AccessLevel = method.GetAccessLevel();
                    methodDep.ReturnType = method is MethodInfo methodInfo ? methodInfo.ReturnType.ToSignature() : TypeSignature.Void;
                    foreach (ParameterInfo parameter in method.GetParameters())
                    {
                        ParameterDependency.Builder parameterDep = methodDep.Parameter(parameter.Position);
                        parameterDep.AccessMode = parameter.GetAccessMode();
                    }
                    if (!method.IsDefined(typeof(DependencyAttribute)) && !implicitDependencies.Contains(method)) patcher.AddMixin(mixinMethodSig, method.ApplyMixin(patcher));
                }
                patcher.AddDependency(typeDep);
            }
        }

        private IEnumerable<TypeInfo> AllTypes(IEnumerable<TypeInfo> types)
        {
            List<TypeInfo> next;
            do
            {
                next = new List<TypeInfo>();
                foreach (TypeInfo type in types.Where(t => !t.IsDefined(typeof(TransparentAttribute))))
                {
                    next.AddRange(type.DeclaredNestedTypes);
                    yield return type;
                }
                types = next;
            }
            while (next.Count > 0);
        }
    }
}
