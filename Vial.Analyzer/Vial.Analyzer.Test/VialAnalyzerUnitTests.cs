using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using TestHelper;
using Vial.Analyzer;

namespace Vial.Analyzer.Test
{
    [TestClass]
    public class UnitTest : CodeFixVerifier
    {
        //No diagnostics expected to show up
        [TestMethod]
        public void TestEmpty() => VerifyCSharpDiagnostic("");

        [TestMethod]
        public void Test0000() => VerifyCSharpDiagnostic(@"
            using Vial.Mixins;
            [module: Patch(""mscorlib"")]
            namespace IDoNotExist
            {
                [Dependency]
                class Type { }
            }
            namespace System
            {
                [Mixin]
                class String
                {
                    [Mixin]
                    internal String(char[] args) { }
                }
            }
            [Dependency, Name(typeof(System.Collections.Generic.Dictionary<object, object>.Enumerator))]
            class Type2 { }
        ", new DiagnosticResult
        {
            Id = "Vial0000",
            Message = string.Format("Dependency or mixin target '{0}' could not be found", "IDoNotExist.Type"),
            Severity = DiagnosticSeverity.Warning,
            Locations = new[] { new DiagnosticResultLocation("Test0.cs", 7, 23) }
        });

         [TestMethod]
        public void Test0001() => VerifyCSharpDiagnostic(@"
            using Vial.Mixins;
            [module: Patch(null)]
            [Mixin, Inject]
            class Type { }
        ", new DiagnosticResult
        {
            Id = "Vial0001",
            Message = string.Format("'{0}' has both '{1}' and '{2}'", "Type", "Vial.Mixins.InjectAttribute", "Vial.Mixins.MixinAttribute"),
            Severity = DiagnosticSeverity.Error,
            Locations = new[] { new DiagnosticResultLocation("Test0.cs", 5, 19) }
        });

        [TestMethod]
        public void Test0002() => VerifyCSharpDiagnostic(@"
            class Test
            {
                [Vial.Mixins.Mixin]
                int field;
            }
        ", new DiagnosticResult
        {
            Id = "Vial0002",
            Message = string.Format("Declaring type of '{0}' does not have a 'MixinAttribute' or a 'DependencyAttribute'", "field"),
            Severity = DiagnosticSeverity.Error,
            Locations = new[] { new DiagnosticResultLocation("Test0.cs", 5, 21) }
        });

        [TestMethod]
        public void Test0003() => VerifyCSharpDiagnostic(@"
            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo(null)]
            [module: Vial.Mixins.Patch(null)]
        ", new DiagnosticResult
        {
            Id = "Vial0003",
            Message = "Assembly contains a module with a 'PatchAttribute'",
            Severity = DiagnosticSeverity.Warning,
            Locations = new[] { new DiagnosticResultLocation("Test0.cs", 2, 24) }
        });

        [TestMethod]
        public void Test0004() => VerifyCSharpDiagnostic(@"
            using Vial.Mixins;
            [module: Patch(null)]
            [Dependency]
            class Type : IDisposable
            {
                public void Dispose() { }
            }
            [Dependency]
            enum EnumType { }
            [Dependency]
            delegate void DelegateType();
        ", new DiagnosticResult
        {
            Id = "Vial0004",
            Message = string.Format("Type '{0}' has a 'DependencyAttribute' or a 'MixinAttribute' but does not inherit from 'System.Object', 'System.Enum', 'System.Delegate', or 'System.MulticastDelegate'", "Type"),
            Severity = DiagnosticSeverity.Error,
            Locations = new[] { new DiagnosticResultLocation("Test0.cs", 5, 19) }
        });

        [TestMethod]
        public void Test0005() => VerifyCSharpDiagnostic(@"
            [module: Vial.Mixins.Patch(""Idonotexist"")]
        ", new DiagnosticResult
        {
            Id = "Vial0005",
            Message = string.Format("Assembly '{0}' could not be found", "Idonotexist"),
            Severity = DiagnosticSeverity.Warning,
            Locations = new[] { new DiagnosticResultLocation("Test0.cs", 2, 40) }
        });

        [TestMethod]
        public void Test0006() => VerifyCSharpDiagnostic(@"
            using Vial.Mixins;
            [module: Patch(null)]
            [Dependency]
            class Type
            {
                [BaseDependency]
                void BaseMethod() { }
                void Method() => BaseMethod();
            }
        ", new DiagnosticResult
        {
            Id = "Vial0006",
            Message = string.Format("Method '{0}' does not have a 'MixinAttribute' yet it attempts to call a method with a 'BaseDependencyAttribute'", "Method"),
            Severity = DiagnosticSeverity.Error,
            Locations = new[] { new DiagnosticResultLocation("Test0.cs", 9, 34) }
        });

        [TestMethod]
        public void Test0007() => VerifyCSharpDiagnostic(@"
            class Type
            {
                [Vial.Mixins.BaseDependency]
                public void BaseMethod() { }
            }
        ", new DiagnosticResult
        {
            Id = "Vial0007",
            Message = string.Format("Member '{0}' is not declared internal", "BaseMethod"),
            Severity =  DiagnosticSeverity.Warning,
            Locations = new[] { new DiagnosticResultLocation("Test0.cs", 5, 29) }
        });

        [TestMethod]
        public void Test0008() => VerifyCSharpDiagnostic(@"
            using Vial.Mixins;
            [module: Patch(null)]
            [Mixin]
            class Type : System.IDisposable
            {
                public void System.IDisposable::Dispose() { }
            }
        ", new DiagnosticResult
        {
            Id = "Vial0008",
            Message = string.Format("Interface implementation '{0}' does not have an 'InjectAttribute'", "System.IDisposable.Dispose"),
            Severity = DiagnosticSeverity.Error,
            Locations = new[] { new DiagnosticResultLocation("Test0.cs", 7, 49) }
        });

        [TestMethod]
        public void Test0009() => VerifyCSharpDiagnostic(@"
            using Vial.Mixins;
            [module: Patch(null)]
            [Inject]
            class Type
            {
                private int field;
                [Inject]
                int Method() => field;
            }
        ", new DiagnosticResult
        {
            Id = "Vial0009",
            Message = string.Format("Reference to unavailable member '{0}'", "field"),
            Severity = DiagnosticSeverity.Warning,
            Locations = new[] { new DiagnosticResultLocation("Test0.cs", 9, 33) }
        });

        [TestMethod]
        public void Test0010() => VerifyCSharpDiagnostic(@"
            using Vial.Mixins;
            [module: Patch(""mscorlib"")]
            [Inject]
            class Type
            {
                [Inject]
                System.Object Method() => null;
            }
        ", new DiagnosticResult
        {
            Id = "Vial0010",
            Message = string.Format("Direct reference to '{0}' from patched or dependency assembly '{1}'", "Object", "mscorlib"),
            Severity = DiagnosticSeverity.Warning,
            Locations = new[] { new DiagnosticResultLocation("Test0.cs", 8, 24) }
        });

        [TestMethod]
        public void Test0011() => VerifyCSharpDiagnostic(@"
            [Vial.Mixins.Inject]
            class Type { }
        ", new DiagnosticResult
        {
            Id = "Vial0011",
            Message = string.Format("Declaring module of type '{0}' is not a patch", "Type"),
            Severity = DiagnosticSeverity.Error,
            Locations = new[] { new DiagnosticResultLocation("Test0.cs", 3, 19) }
        });

        [TestMethod]
        public void Test0012() => VerifyCSharpDiagnostic(@"
            using Vial.Mixins;
            [module: Patch(null)]
            [Mixin]
            class Type
            {
                [BaseDependency]
                void BaseMethod() { }
                [Mixin]
                void Method(int arg) => BaseMethod();
                [BaseDependency]
                void BaseMethod2(int arg) { }
                [Mixin]
                void Method2(int arg) => BaseMethod2(arg);
            }
        ", new DiagnosticResult
        {
            Id = "Vial0012",
            Message = string.Format("Base dependency argument mismatch in '{0}'", "Method"),
            Severity = DiagnosticSeverity.Error,
            Locations = new[] { new DiagnosticResultLocation("Test0.cs", 10, 41) }
        });

        [TestMethod]
        public void Test0013() => VerifyCSharpDiagnostic(@"
            using Vial.Mixins;
            [module: Patch(null)]
            [Mixin]
            class Type : System.IDisposable
            {
                [Inject, Name(""Name"")]
                public void System.IDisposable::Dispose() { }
            }
        ", new DiagnosticResult
        {
            Id = "Vial0013",
            Message = string.Format("Interface implementation '{0}' cannot have a 'NameAttribute'", "System.IDisposable.Dispose"),
            Severity = DiagnosticSeverity.Error,
            Locations = new[] { new DiagnosticResultLocation("Test0.cs", 8, 49) }
        });

        [TestMethod]
        public void Test0014() => VerifyCSharpDiagnostic(@"
            using Vial.Mixins;
            [module: Patch(null)]
            [Inject]
            class Type : System.IDisposable
            {
                [Inject]
                public void Dispose() { }
            }
        ", new DiagnosticResult
        {
            Id = "Vial0014",
            Message = string.Format("Interface implementation '{0}' from interface '{1}' is not explicitly defined", "Dispose", "IDisposable"),
            Severity = DiagnosticSeverity.Warning,
            Locations = new[] { new DiagnosticResultLocation("Test0.cs", 8, 29) }
        });

        //  | Diagnostics to add (tests included)
        // Mixin/Dependency method modifiers must match the original
        // Referenced version of mscorlib is not correct: should be 2.0
        // Mixin/Inject member creates backing members

        protected override CodeFixProvider GetCSharpCodeFixProvider() => new MixinsCodeFixProvider();

        protected override DiagnosticAnalyzer GetCSharpDiagnosticAnalyzer() => new MixinsAnalyzer();
    }
}
