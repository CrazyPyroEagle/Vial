using dnlib.DotNet;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Vial.Installer
{
    sealed class TypeMixin
    {
        public TypeDependency Dependency { get; }
        public TypeInject Inject { get; }
        public IEnumerable<MethodMixin> Mixins { get; }

        private TypeMixin(Builder builder)
        {
            Dependency = builder.Dependency;
            Inject = builder.Inject;
            Mixins = builder.Mixins.ToArray();
        }
        
        public sealed class Builder
        {
            public TypeDependency Dependency { get; }
            public TypeInject Inject { get; }
            public IEnumerable<MethodMixin> Mixins => mixins;

            private readonly List<MethodMixin> mixins = new List<MethodMixin>();

            public Builder(TypeDependency dependency, TypeInject inject)
            {
                Dependency = dependency;
                Inject = inject;
            }

            public Builder Mixin(MethodMixin mixin)
            {
                if (!Dependency.MethodDependencies.Any(md => md.Signature == mixin.Signature)) throw new ArgumentException("mixin is not a dependency");
                mixins.Add(mixin);
                return this;
            }

            public TypeMixin Build() => new TypeMixin(this);

            public static implicit operator TypeMixin(Builder self) => self.Build();
        }
    }

    sealed class MethodMixin
    {
        public MethodSignature Signature { get; }
        public Action<MethodDef> Mixin { get; }

        public MethodMixin(MethodSignature signature, Action<MethodDef> mixin)
        {
            Signature = signature;
            Mixin = mixin;
        }
    }
}
