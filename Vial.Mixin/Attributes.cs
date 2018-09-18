using System;
using System.Collections.Generic;
using System.Text;

namespace Vial.Mixins
{
    /// <summary>
    /// Replace the method definitions in the patched assemblies with the methods defined in the marked type.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface), Transparent]
    public class MixinAttribute : Attribute { }

    /// <summary>
    /// Use this name when patching instead of the name of the marked object.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method | AttributeTargets.Constructor), Transparent]
    public class NameAttribute : Attribute
    {
        public string Target { get; }

        public NameAttribute(Type target) : this(target.FullName.Replace('+', '/')) { }
        public NameAttribute(string target) => Target = target;
    }

    /// <summary>
    /// Replace the call to the base constructor with the base constructor used by this method.
    /// </summary>
    [AttributeUsage(AttributeTargets.Constructor), Transparent]
    public class RewriteBaseAttribute : Attribute { }

    /// <summary>
    /// Don't replace this definition when injecting mixins.
    /// The definition's access modifier may be modified to match requirements this definition imposes.
    /// Fields are implicit dependencies.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor), Transparent]
    public class DependencyAttribute : Attribute { }

    /// <summary>
    /// Substitute uses of this definition with the mixed class' base definition.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor), Transparent]
    public class BaseDependencyAttribute : Attribute { }

    /// <summary>
    /// Completely exclude this definition when injecting this assembly.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method | AttributeTargets.Class), Transparent]
    public class TransparentAttribute : Attribute { }
}
