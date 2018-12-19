using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vial.Installer
{
    sealed class TypeDependency
    {
        public TypeDescriptor Descriptor { get; }
        public IEnumerable<FieldDescriptor> FieldDependencies { get; private set; }
        public IEnumerable<MethodDescriptor> MethodDependencies { get; private set; }
        public IEnumerable<MethodSignature> Originals { get; private set; }

        private TypeDependency(Builder builder)
        {
            Descriptor = builder.Descriptor;
            Originals = new List<MethodSignature>(builder.Originals);
        }

        public sealed class Builder
        {
            public TypeDescriptor Descriptor { get; }
            public List<MethodSignature> Originals { get; } = new List<MethodSignature>();

            private readonly Dictionary<FieldSignature, FieldDescriptor.Builder> fieldDependencies = new Dictionary<FieldSignature, FieldDescriptor.Builder>();
            private readonly Dictionary<MethodSignature, MethodDescriptor.Builder> methodDependencies = new Dictionary<MethodSignature, MethodDescriptor.Builder>();

            public Builder(TypeDescriptor descriptor) => Descriptor = descriptor;

            public FieldDescriptor.Builder FieldDependency(FieldSignature signature)
            {
                lock (fieldDependencies)
                {
                    if (!fieldDependencies.TryGetValue(signature, out FieldDescriptor.Builder result)) fieldDependencies.Add(signature, result = new FieldDescriptor.Builder(signature));
                    return result;
                }
            }

            public Builder FieldDependency(FieldSignature signature, Action<FieldDescriptor.Builder> action)
            {
                action(FieldDependency(signature));
                return this;
            }

            public MethodDescriptor.Builder MethodDependency(MethodSignature signature)
            {
                lock (methodDependencies)
                {
                    if (!methodDependencies.TryGetValue(signature, out MethodDescriptor.Builder result)) methodDependencies.Add(signature, result = new MethodDescriptor.Builder(signature));
                    return result;
                }
            }

            public Builder MethodDependency(MethodSignature signature, Action<MethodDescriptor.Builder> action)
            {
                action(MethodDependency(signature));
                return this;
            }

            public Builder Original(params MethodSignature[] original) => Original((IEnumerable<MethodSignature>)original);
            public Builder Original(IEnumerable<MethodSignature> original)
            {
                Originals.AddRange(original);
                return this;
            }

            public TypeDependency Build() => new TypeDependency(this)
            {
                FieldDependencies = fieldDependencies.Values.Select(b => b.Build()).ToArray(),
                MethodDependencies = methodDependencies.Values.Select(b => b.Build()).ToArray()
            };

            public static implicit operator TypeDependency(Builder self) => self.Build();
        }
    }
}
