using dnlib.DotNet;
using System;
using System.Collections.Generic;

namespace Vial.Installer
{
    sealed class TypeInject
    {
        public TypeDescriptor Descriptor { get; set; }
        public IEnumerable<FieldInject> FieldInjects { get; private set; }
        public IEnumerable<MethodInject> MethodInjects { get; private set; }

        private TypeInject(Builder builder) => Descriptor = builder.Descriptor;

        public sealed class Builder
        {
            private readonly List<FieldInject> fieldInjects = new List<FieldInject>();
            private readonly List<MethodInject> methodInjects = new List<MethodInject>();

            public TypeDescriptor Descriptor { get; }

            public Builder(TypeDescriptor descriptor) => Descriptor = descriptor;

            public Builder Inject(FieldInject field)
            {
                fieldInjects.Add(field);
                return this;
            }

            public Builder Inject(MethodInject method)
            {
                methodInjects.Add(method);
                return this;
            }

            public TypeInject Build() => new TypeInject(this)
            {
                FieldInjects = fieldInjects,
                MethodInjects = methodInjects
            };

            public static implicit operator TypeInject(Builder self) => self.Build();
        }
    }

    sealed class MethodInject
    {
        public MethodDescriptor Descriptor { get; }
        public Action<MethodDef> Inject { get; }

        public MethodInject(MethodDescriptor descriptor, Action<MethodDef> inject)
        {
            Descriptor = descriptor;
            Inject = inject;
        }
    }

    sealed class FieldInject
    {
        public FieldDescriptor Descriptor { get; }
        public Action<FieldDef> Inject { get; }

        public FieldInject(FieldDescriptor descriptor, Action<FieldDef> inject)
        {
            Descriptor = descriptor;
            Inject = inject;
        }
    }
}
