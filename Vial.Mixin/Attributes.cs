using System;
using System.Collections.Generic;
using System.Text;

namespace Vial.Mixins
{
    /// <summary>
    /// Declare that this module contains mixins for the given assembly.
    /// </summary>
    [AttributeUsage(AttributeTargets.Module)]
    public class PatchAttribute : Attribute
    {
        public string Assembly { get; }

        /// <param name="assembly">The assembly that contains the types targeted by this module's mixins.</param>
        public PatchAttribute(string assembly) => Assembly = assembly;
    }

    /// <summary>
    /// Declare that this module contains dependencies on types inside the given assembly.
    /// </summary>
    [AttributeUsage(AttributeTargets.Module)]
    public class RequiredAttribute : Attribute
    {
        public string Assembly { get; }

        public RequiredAttribute(string assembly) => Assembly = assembly;
    }

    /// <summary>
    /// Inject this member into the patched assembly, ensuring that it does not collide with any existing members.
    /// Non-exported members may see their signature change.
    /// Members this member causes the compiler to generate will inherit this attribute's behaviour.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class
                  | AttributeTargets.Struct
                  | AttributeTargets.Interface
                  | AttributeTargets.Enum
                  | AttributeTargets.Delegate)]
    public class InjectAttribute : Attribute { }

    /// <summary>
    /// Replace the definition in the patched assembly with this definition.
    /// The declaring type must also have Mixin or Dependency defined on it.
    /// If declared on a type, this type's implemented interfaces will be added to the target type.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class
                  | AttributeTargets.Interface
                  | AttributeTargets.Enum
                  | AttributeTargets.Property
                  | AttributeTargets.Event
                  | AttributeTargets.Constructor
                  | AttributeTargets.Method)]
    public class MixinAttribute : Attribute { }

    /// <summary>
    /// Use this name when patching instead of the name of this member.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class
                  | AttributeTargets.Struct
                  | AttributeTargets.Interface
                  | AttributeTargets.Enum
                  | AttributeTargets.Delegate
                  | AttributeTargets.Field
                  | AttributeTargets.Property
                  | AttributeTargets.Event
                  | AttributeTargets.Constructor
                  | AttributeTargets.Method)]
    public class NameAttribute : Attribute
    {
        public string Target { get; }

        public NameAttribute(Type target) : this(target.FullName.Replace('+', '/')) { }
        public NameAttribute(string target) => Target = target;
        //public NameAttribute(params string[] target) => Target = string.Join(".", target);
    }

    /// <summary>
    /// Replace the call to the base constructor with the base constructor used by this method.
    /// This attribute may only be used alongside <code>MixinAttribute</code>.
    /// The declaring type's base type must match the target's base type.
    /// </summary>
    [AttributeUsage(AttributeTargets.Constructor)]
    public class RewriteBaseAttribute : Attribute { }

    /// <summary>
    /// Don't replace this definition when injecting mixins.
    /// The definition's access modifier may be modified to match requirements this definition imposes.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class
                  | AttributeTargets.Struct
                  | AttributeTargets.Interface
                  | AttributeTargets.Enum
                  | AttributeTargets.Delegate
                  | AttributeTargets.Field
                  | AttributeTargets.Property
                  | AttributeTargets.Event
                  | AttributeTargets.Constructor
                  | AttributeTargets.Method)]
    public class DependencyAttribute : Attribute { }

    /// <summary>
    /// Substitute uses of this definition with the mixed class' base definition.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method
                  | AttributeTargets.Constructor)]
    public class BaseDependencyAttribute : Attribute { }
}
