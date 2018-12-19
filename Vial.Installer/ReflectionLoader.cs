using dnlib.DotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vial.Mixins;

namespace Vial.Installer
{
    static class ReflectionLoader
    {
        public static PatchConfiguration ToPatch(this Assembly assembly) => assembly.ToPatch(assembly.FullName);

        public static PatchConfiguration ToPatch(this Assembly assembly, string patchName)
        {
            assembly.LoadReferencedAssemblies();
            PatchConfiguration patch = new PatchConfiguration(patchName, assembly.ManifestModule.GetCustomAttribute<PatchAttribute>().Assembly, assembly.ManifestModule.GetCustomAttributes<RequiredAttribute>().Select(a => a.Assembly));
            foreach (TypeInfo type in assembly.DefinedTypes)
            {
                if (type.IsInject()) patch.AddInjected(patch.LoadInject(type));
                if (type.IsDependencyExplicit()) patch.AddDependency(patch.LoadDependency(type));
                if (type.IsMixin()) patch.AddMixin(patch.LoadMixin(type));
            }
            return patch;
        }

        private static TypeInject.Builder LoadInject(this PatchConfiguration patch, TypeInfo type)
        {
            TypeInject.Builder builder = new TypeInject.Builder(type.ToDescriptor());
            foreach (FieldInfo field in type.DeclaredFields.Where(IsInject)) builder.Inject(new FieldInject(field.ToDescriptor(), a => { }));
            foreach (MethodInfo method in type.AllMethods().Where(IsInject)) builder.Inject(new MethodInject(method.ToDescriptor(), method.ApplyInject(patch)));
            return builder;
        }

        private static TypeDependency.Builder LoadDependency(this PatchConfiguration patch, TypeInfo type)
        {
            TypeSignature typeSig = type.ToSignature();
            TypeSignature mixinTypeSig = type.ToMixinSignature();
            patch.AddSubstitution(mixinTypeSig, typeSig);
            TypeDependency.Builder builder = new TypeDependency.Builder(type.ToDescriptor());
            foreach (FieldInfo field in type.DeclaredFields.Where(IsDependency))
            {
                patch.AddSubstitution(mixinTypeSig.Field(field.GetName(), field.FieldType.ToSignature()), typeSig.Field(field.GetName(), field.FieldType.ToSignature()));
                builder.FieldDependency(field.ToSignature(), field.Import());
            }
            foreach (MethodBase method in type.AllMethods().Where(IsDependency))
            {
                IEnumerable<Func<ModuleDef, TypeSig>> parameterTypes = method.GetParameters().Select(p => p.ParameterType.ToSig()).ToArray();
                patch.AddSubstitution(mixinTypeSig.Method(method.GetName(), method.CallingConvention.ToPatcher(), parameterTypes), typeSig.Method(method.GetName(), method.CallingConvention.ToPatcher(), parameterTypes));
                builder.MethodDependency(method.ToSignature(), method.Import());
            }
            return builder;
        }

        private static TypeMixin.Builder LoadMixin(this PatchConfiguration patch, TypeInfo type)
        {
            TypeMixin.Builder builder = new TypeMixin.Builder(patch.LoadDependency(type), patch.LoadInject(type));
            foreach (MethodBase method in type.AllMethods().Where(IsMixin)) builder.Mixin(new MethodMixin(method.ToSignature(), method.ApplyMixin(patch)));
            return builder;
        }

        private static TypeDescriptor ToDescriptor(this TypeInfo type)
        {
            TypeDescriptor.Builder builder = new TypeDescriptor.Builder(type.ToSignature());
            type.Import()(builder);
            return builder;
        }

        private static FieldDescriptor ToDescriptor(this FieldInfo field)
        {
            FieldDescriptor.Builder builder = new FieldDescriptor.Builder(field.ToSignature());
            field.Import()(builder);
            return builder;
        }

        private static MethodDescriptor ToDescriptor(this MethodBase method)
        {
            MethodDescriptor.Builder builder = new MethodDescriptor.Builder(method.ToSignature());
            method.Import()(builder);
            return builder;
        }

        private static Action<TypeDescriptor.Builder> Import(this TypeInfo type) => fd => fd.AccessLevel = type.GetAccessLevel();

        private static Action<FieldDescriptor.Builder> Import(this FieldInfo field) => fd =>
        {
            fd.AccessLevel = field.GetAccessLevel();
            fd.IsInitOnly = field.IsInitOnly;
        };

        private static Action<MethodDescriptor.Builder> Import(this MethodBase method) => fd =>
        {
            fd.AccessLevel = method.GetAccessLevel();
            fd.ReturnType = method is MethodInfo methodInfo ? methodInfo.ReturnType.ToSignature() : TypeSignature.Void;
        };

        private static bool IsInject(this ICustomAttributeProvider info) => info.IsDefined(typeof(InjectAttribute), false);
        private static bool IsDependency(this ICustomAttributeProvider info) => info.IsDependencyExplicit() || info.IsMixin();
        private static bool IsDependencyExplicit(this ICustomAttributeProvider info) => info.IsDefined(typeof(DependencyAttribute), false);
        private static bool IsMixin(this ICustomAttributeProvider info) => info.IsDefined(typeof(MixinAttribute), false);

        private static IEnumerable<TypeInfo> AllTypes(this Assembly assembly)
        {
            IEnumerable<TypeInfo> types = assembly.DefinedTypes;
            List<TypeInfo> next;
            do
            {
                next = new List<TypeInfo>();
                foreach (TypeInfo type in types)
                {
                    next.AddRange(type.DeclaredNestedTypes);
                    yield return type;
                }
                types = next;
            }
            while (next.Count > 0);
        }
        private static IEnumerable<MethodBase> AllMethods(this TypeInfo type) => type.DeclaredMethods.Concat<MethodBase>(type.DeclaredConstructors);
    }
}
