using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vial.Analyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class MixinsAnalyzer : DiagnosticAnalyzer
    {
        private const string AttributeNamespace = "Vial.Mixins.";
        private const string PatchAttribute = AttributeNamespace + "PatchAttribute";
        private const string RequiredAttribute = AttributeNamespace + "RequiredAttribute";
        private const string NameAttribute = AttributeNamespace + "NameAttribute";
        private const string InjectAttribute = AttributeNamespace + "InjectAttribute";
        private const string MixinAttribute = AttributeNamespace + "MixinAttribute";
        private const string DependencyAttribute = AttributeNamespace + "DependencyAttribute";
        private const string BaseDependencyAttribute = AttributeNamespace + "BaseDependencyAttribute";

        // You can change these strings in the Resources.resx file. If you do not want your analyzer to be localize-able, you can use regular strings for Title and MessageFormat.
        // See https://github.com/dotnet/roslyn/blob/master/docs/analyzers/Localizing%20Analyzers.md for more on localization
        private static readonly DiagnosticDescriptor RuleTargetNotFound = Descriptor("0000", DiagnosticSeverity.Warning, true);
        private static readonly DiagnosticDescriptor RuleIncompatibleAttributes = Descriptor("0001", DiagnosticSeverity.Error, true);
        private static readonly DiagnosticDescriptor RuleNotInMixinType = Descriptor("0002", DiagnosticSeverity.Error, true);       // TODO Fix 0002 not always triggered?
        private static readonly DiagnosticDescriptor RuleSharedInternals = Descriptor("0003", DiagnosticSeverity.Warning, true);
        private static readonly DiagnosticDescriptor RuleDependencyInheritance = Descriptor("0004", DiagnosticSeverity.Error, true);
        private static readonly DiagnosticDescriptor RulePatchedMissing = Descriptor("0005", DiagnosticSeverity.Warning, true);
        private static readonly DiagnosticDescriptor RuleNonMixinCallingBase = Descriptor("0006", DiagnosticSeverity.Error, true);
        private static readonly DiagnosticDescriptor RuleNotInternal = Descriptor("0007", DiagnosticSeverity.Warning, true);
        private static readonly DiagnosticDescriptor RuleInterfaceMember = Descriptor("0008", DiagnosticSeverity.Error, true);
        private static readonly DiagnosticDescriptor RuleUnavailableMember = Descriptor("0009", DiagnosticSeverity.Warning, true);
        private static readonly DiagnosticDescriptor RuleDirectReference = Descriptor("0010", DiagnosticSeverity.Warning, true);
        private static readonly DiagnosticDescriptor RuleDeclaringPatch = Descriptor("0011", DiagnosticSeverity.Error, true);
        private static readonly DiagnosticDescriptor RuleOriginalArguments = Descriptor("0012", DiagnosticSeverity.Error, true);
        private static readonly DiagnosticDescriptor RuleNamedImplementation = Descriptor("0013", DiagnosticSeverity.Error, true);
        private static readonly DiagnosticDescriptor RuleImplicitImplementation = Descriptor("0014", DiagnosticSeverity.Warning, true);

        // TODO Add diagnostics for the following things
        // Mismatching dependency enum constants
        // Mismatching dependency parameter default values

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(RuleTargetNotFound, RuleIncompatibleAttributes, RuleNotInMixinType, RuleSharedInternals, RuleDependencyInheritance, RulePatchedMissing, RuleNonMixinCallingBase, RuleNotInternal, RuleInterfaceMember, RuleUnavailableMember, RuleDirectReference, RuleDeclaringPatch, RuleOriginalArguments, RuleNamedImplementation, RuleImplicitImplementation);

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.RegisterCompilationAction(AnalyzeCompilation);
            context.RegisterSymbolAction(AnalyzeNamedTypeSymbol, SymbolKind.NamedType);
            context.RegisterSymbolAction(AnalyzeMemberSymbol, SymbolKind.Field, SymbolKind.Property, SymbolKind.Event, SymbolKind.Method);
            context.RegisterSymbolAction(AnalyzeMethodSymbol, SymbolKind.Method);
        }

        private static void AnalyzeCompilation(CompilationAnalysisContext context)
        {
            bool warnSharedInternals = false;
            foreach (IModuleSymbol module in context.Compilation.Assembly.Modules)
            {
                AttributeData patchAttribute = GetAttribute(context.Compilation, module, PatchAttribute);
                IEnumerable<string> required = GetAttributes(context.Compilation, module, RequiredAttribute).Select(GetTargetName);
                if (patchAttribute != null)
                {
                    warnSharedInternals = true;
                    string name = GetTargetName(patchAttribute);
                    if (name != null) required = required.Concat(new[] { name });
                }
                foreach (string name in required) if (GetPatchTarget(context.Compilation, name) == null) context.ReportDiagnostic(Diagnostic.Create(RulePatchedMissing, ((AttributeSyntax)patchAttribute.ApplicationSyntaxReference.GetSyntax()).ArgumentList.Arguments[0].GetLocation(), name));
            }
            if (warnSharedInternals)
            {
                foreach (AttributeData attribute in context.Compilation.Assembly.GetAttributes(context.Compilation.GetTypeByMetadataName(typeof(System.Runtime.CompilerServices.InternalsVisibleToAttribute).FullName)))
                {
                    context.ReportDiagnostic(Diagnostic.Create(RuleSharedInternals, attribute.ApplicationSyntaxReference.GetSyntax().GetLocation()));
                }
            }
        }
        
        private static void AnalyzeNamedTypeSymbol(SymbolAnalysisContext context)
        {
            INamedTypeSymbol symbol = (INamedTypeSymbol)context.Symbol;
            CheckDuplicates(context, InjectAttribute, MixinAttribute, DependencyAttribute, BaseDependencyAttribute);
            if (IsDependency(context.Compilation, symbol))
            {
                AnalyzeDependencySymbol(context);
                if (!symbol.BaseType.Equals(context.Compilation.GetSpecialType(SpecialType.System_Object)) && !symbol.BaseType.Equals(context.Compilation.GetSpecialType(SpecialType.System_Enum)) && !symbol.BaseType.Equals(context.Compilation.GetSpecialType(SpecialType.System_Delegate)) && !symbol.BaseType.Equals(context.Compilation.GetSpecialType(SpecialType.System_MulticastDelegate)) || !IsMixin(context.Compilation, symbol) && symbol.Interfaces.Any()) context.ReportDiagnostic(Diagnostic.Create(RuleDependencyInheritance, symbol.Locations[0], symbol.Name));
            }
            if ((IsDependency(context.Compilation, symbol) || IsInject(context.Compilation, symbol)) && GetAttribute(context.Compilation, symbol.ContainingModule, PatchAttribute) == null) context.ReportDiagnostic(Diagnostic.Create(RuleDeclaringPatch, symbol.Locations[0], symbol.Name));
        }

        private static void AnalyzeMemberSymbol(SymbolAnalysisContext context)
        {
            CheckDuplicates(context, InjectAttribute, MixinAttribute, DependencyAttribute, BaseDependencyAttribute);
            ISymbol symbol = context.Symbol;
            if (IsDependency(context.Compilation, symbol))
            {
                AnalyzeDependencySymbol(context);
                if (!IsDependency(context.Compilation, symbol.ContainingType)) context.ReportDiagnostic(Diagnostic.Create(RuleNotInMixinType, symbol.Locations[0], symbol.Name));
            }
            if (IsMixin(context.Compilation, symbol) && !IsMixin(context.Compilation, symbol)) context.ReportDiagnostic(Diagnostic.Create(RuleNotInMixinType, symbol.Locations[0], symbol.Name));
            if (IsBaseDependency(context.Compilation, symbol)) CheckAccessibility(context);
            if (IsAvailable(context.Compilation, symbol.ContainingType) && symbol.IsInterfaceImplementation())
            {
                if (!IsInject(context.Compilation, symbol)) context.ReportDiagnostic(Diagnostic.Create(RuleInterfaceMember, symbol.Locations[0], symbol.Name));
                if (IsDefined(context.Compilation, symbol, NameAttribute)) context.ReportDiagnostic(Diagnostic.Create(RuleNamedImplementation, symbol.Locations[0], symbol.Name));
                List<ISymbol> implicitly = new List<ISymbol>(symbol.GetImplementedInterfaceMembers());
                switch (symbol)
                {
                    case IMethodSymbol method:
                        foreach (ISymbol ifsymbol in method.ExplicitInterfaceImplementations) implicitly.RemoveAll(s => ifsymbol.Equals(s));
                        break;
                    case IPropertySymbol property:
                        foreach (ISymbol ifsymbol in property.ExplicitInterfaceImplementations) implicitly.RemoveAll(s => ifsymbol.Equals(s));
                        break;
                    case IEventSymbol @event:
                        foreach (ISymbol ifsymbol in @event.ExplicitInterfaceImplementations) implicitly.RemoveAll(s => ifsymbol.Equals(s));
                        break;
                }
                foreach (ISymbol ifsymbol in implicitly) context.ReportDiagnostic(Diagnostic.Create(RuleImplicitImplementation, symbol.Locations[0], symbol.Name, ifsymbol.ContainingType.Name));
            }
        }

        private static void AnalyzeMethodSymbol(SymbolAnalysisContext context)
        {
            IMethodSymbol symbol = (IMethodSymbol)context.Symbol;
            bool reportCallingBase = !IsMixin(context.Compilation, symbol);
            bool reportUnavailableMember = IsMixin(context.Compilation, symbol) || IsInject(context.Compilation, symbol);
            foreach (IdentifierNameSyntax expression in symbol.DeclaringSyntaxReferences.SelectMany(r => r.GetSyntax().DescendantNodes().OfType<IdentifierNameSyntax>()))
            {
                ISymbol target = context.Compilation.GetSemanticModel(expression.SyntaxTree).GetSymbolInfo(expression).Symbol;
                if (target?.ContainingModule == null || expression.Parent.IsKind(SyntaxKind.Attribute)) continue;
                CheckReference(context, target, expression.GetLocation(), reportCallingBase, reportUnavailableMember);
            }
        }

        private static void AnalyzeDependencySymbol(SymbolAnalysisContext context)
        {
            ISymbol symbol = context.Symbol;
            CheckAccessibility(context);
            Compilation withAllSymbols = context.Compilation.WithOptions(context.Compilation.Options.WithMetadataImportOptions(MetadataImportOptions.All));
            IAssemblySymbol assembly = GetPatchTarget(withAllSymbols, context.Compilation, symbol);
            ISymbol target = ResolveTarget(withAllSymbols, context.Compilation, assembly, symbol);
            if (assembly == null || target != null) return;
            if (IsDependencyExplicit(context.Compilation, symbol))
            {
                foreach (IAssemblySymbol assembly2 in GetRequiredTargets(context.Compilation, symbol.ContainingModule))
                {
                    ISymbol target2 = ResolveTarget(withAllSymbols, context.Compilation, assembly2, symbol);
                    if (assembly2 == null || target2 != null) return;
                }
            }
            context.ReportDiagnostic(Diagnostic.Create(RuleTargetNotFound, symbol.Locations[0], GetName(context.Compilation, symbol)));
        }

        private static void CheckReference(SymbolAnalysisContext context, ISymbol symbol, Location location, bool reportCallingBase, bool reportUnavailableMember)
        {
            if (reportCallingBase && IsBaseDependency(context.Compilation, symbol)) context.ReportDiagnostic(Diagnostic.Create(RuleNonMixinCallingBase, location, context.Symbol.Name));
            if (reportUnavailableMember && symbol.Kind != SymbolKind.Parameter && symbol.Kind != SymbolKind.Local && symbol.Kind != SymbolKind.Label && !IsAvailable(context.Compilation, symbol)) context.ReportDiagnostic(Diagnostic.Create(RuleUnavailableMember, location, symbol.Name));
            if (symbol.ContainingAssembly.Name == GetPatchName(context.Compilation, context.Symbol.ContainingModule)) context.ReportDiagnostic(Diagnostic.Create(RuleDirectReference, location, symbol.Name, symbol.ContainingAssembly.Name));
            if (context.Symbol is IMethodSymbol method && symbol is IMethodSymbol baseMethod && IsBaseDependency(context.Compilation, baseMethod) && (!method.ReturnType.Equals(baseMethod.ReturnType) || method.Parameters.Length != baseMethod.Parameters.Length || method.Parameters.Zip(baseMethod.Parameters, (a, b) => a.Type.Equals(b.Type)).Any(b => !b))) context.ReportDiagnostic(Diagnostic.Create(RuleOriginalArguments, location, context.Symbol.Name));
        }

        private static void CheckAccessibility(SymbolAnalysisContext context)
        {
            ISymbol symbol = context.Symbol;
            switch (symbol.DeclaredAccessibility)
            {
                case Accessibility.NotApplicable:
                case Accessibility.Private:
                case Accessibility.ProtectedAndInternal:
                case Accessibility.Internal:
                    break;
                default:
                    context.ReportDiagnostic(Diagnostic.Create(RuleNotInternal, symbol.Locations[0], symbol.Name));
                    break;
            }
        }

        private static void CheckDuplicates(SymbolAnalysisContext context, params string[] attributes)
        {
            string[] present = attributes.Where(attr => IsDefined(context.Compilation, context.Symbol, attr)).ToArray();
            if (present.Length >= 2) context.ReportDiagnostic(Diagnostic.Create(RuleIncompatibleAttributes, context.Symbol.Locations[0], context.Symbol.Name, present[0], present[1]));
        }

        private static ISymbol ResolveTarget(Compilation withAllSymbols, Compilation symbolSource, IAssemblySymbol assembly, ISymbol symbol)
        {
            switch (symbol)
            {
                case INamedTypeSymbol type:
                    /*switch (type.TypeKind)
                    {
                        case TypeKind.Class:
                        case TypeKind.Enum:
                        case TypeKind.Interface:
                        case TypeKind.Struct:
                            return assembly?.GetTypeByMetadataName(GetName(compilation, symbol));
                        case TypeKind.Array:
                            // TODO
                        case TypeKind.Error:
                        case TypeKind.Unknown:
                            return null;
                    }*/
                    return assembly?.GetTypeByMetadataName(GetName(symbolSource, symbol));
                case IMethodSymbol method:
                    //IEnumerable<ITypeSymbol> parameters = method.Parameters.Select(p => ResolveTarget(compilation, compilation.Assembly, p.Type) as ITypeSymbol ?? p.Type);
                    //return ((ITypeSymbol)ResolveTarget(compilation, assembly, symbol.ContainingType))?.GetMembers(GetName(compilation, symbol)).FirstOrDefault(s => s is IMethodSymbol m && m.Parameters.Zip(parameters, (a, b) => a.Type.Equals(b)).All(b => b));
                    return ((ITypeSymbol)ResolveTarget(withAllSymbols, symbolSource, assembly, symbol.ContainingType))?.GetBaseTypesAndThis().SelectMany(t => t.GetMembers(GetName(symbolSource, symbol))).FirstOrDefault(s => s is IMethodSymbol m && CompareParameters(withAllSymbols, symbolSource, m.Parameters, method.Parameters));
                default:
                    return ((ITypeSymbol)ResolveTarget(withAllSymbols, symbolSource, assembly, symbol.ContainingType))?.GetBaseTypesAndThis().SelectMany(t => t.GetMembers(GetName(symbolSource, symbol))).FirstOrDefault();
            }
        }

        private static bool CompareParameters(Compilation ca, Compilation cb, IReadOnlyList<IParameterSymbol> a, IReadOnlyList<IParameterSymbol> b)
        {
            if (a.Count != b.Count) return false;
            for (int index = 0; index < a.Count; index++) if (!CompareTypes(ca, cb, a[index].Type, b[index].Type)) return false;
            return true;
        }

        private static bool CompareTypes(Compilation ca, Compilation cb, ITypeSymbol a, ITypeSymbol b)
        {
            if (a.TypeKind != b.TypeKind) return false;
            switch (a.TypeKind)
            {
                case TypeKind.Array:
                    IArrayTypeSymbol arrayA = (IArrayTypeSymbol)a, arrayB = (IArrayTypeSymbol)b;
                    return arrayA.Rank == arrayB.Rank && arrayA.Sizes.SequenceEqual(arrayB.Sizes) && CompareTypes(ca, cb, arrayA.ElementType, arrayB.ElementType);
                case TypeKind.Pointer:
                    IPointerTypeSymbol ptrA = (IPointerTypeSymbol)a, ptrB = (IPointerTypeSymbol)b;
                    return CompareTypes(ca, cb, ptrA.PointedAtType, ptrB.PointedAtType);
                case TypeKind.Class:
                case TypeKind.Delegate:
                case TypeKind.Dynamic:
                case TypeKind.Enum:
                case TypeKind.Error:
                case TypeKind.Interface:
                case TypeKind.Module:
                case TypeKind.Struct:
                case TypeKind.TypeParameter:
                case TypeKind.Unknown:
                    return GetName(ca, a) == GetName(cb, b);
            }
            throw new ArgumentException("unknown type kind: " + a.TypeKind);
        }

        private static string GetPatchName(Compilation compilation, IModuleSymbol symbol) => GetTargetName(GetAttribute(compilation, symbol, PatchAttribute));
        private static IEnumerable<string> GetRequiredNames(Compilation compilation, IModuleSymbol symbol) => GetAttributes(compilation, symbol, RequiredAttribute).Select(GetTargetName);
        private static string GetName(Compilation compilation, ISymbol symbol) => GetTargetName(GetAttribute(compilation, symbol, NameAttribute)) ?? symbol.GetQualifiedName();

        private static IAssemblySymbol GetPatchTarget(Compilation withAllSymbols, Compilation symbolSource, ISymbol patch) => GetPatchTarget(withAllSymbols, GetPatchName(symbolSource, patch.ContainingModule));
        private static IAssemblySymbol GetPatchTarget(Compilation compilation, string name) => name != null ? compilation.References.Select(compilation.GetAssemblyOrModuleSymbol).Select(s => s is IAssemblySymbol a ? a : s?.ContainingAssembly).FirstOrDefault(a => a?.Name == name) : null;
        private static IEnumerable<IAssemblySymbol> GetRequiredTargets(Compilation compilation, IModuleSymbol patch) => GetRequiredTargets(compilation, GetRequiredNames(compilation, patch));
        private static IEnumerable<IAssemblySymbol> GetRequiredTargets(Compilation compilation, IEnumerable<string> names) => names.SelectMany(n => compilation.References.Select(compilation.GetAssemblyOrModuleSymbol).Select(s => s is IAssemblySymbol a ? a : s?.ContainingAssembly).Where(a => a?.Name == n));

        private static string GetTargetName(AttributeData attribute)
        {
            if (attribute == null || attribute.ConstructorArguments.Length == 0) return null;
            TypedConstant argument = attribute.ConstructorArguments[0];
            if (argument.Value == null) return null;
            switch (argument.Value)
            {
                case string stringValue:
                    return stringValue.Replace('/', '+');
                case INamedTypeSymbol typeValue:
                    return typeValue.GetQualifiedName();
            }
            throw new NotImplementedException("cannot parse argument " + argument.Value);
        }

        private static AttributeData GetAttribute(Compilation compilation, ISymbol symbol, string attribute) => symbol.GetAttribute(compilation.GetTypeByMetadataName(attribute));
        private static IEnumerable<AttributeData> GetAttributes(Compilation compilation, ISymbol symbol, string attribute) => symbol.GetAttributes(compilation.GetTypeByMetadataName(attribute));
        private static bool IsDefined(Compilation compilation, ISymbol symbol, string attribute) => symbol.HasAttribute(compilation.GetTypeByMetadataName(attribute));
        private static bool IsMixin(Compilation compilation, ISymbol symbol) => IsDefined(compilation, symbol, MixinAttribute);
        private static bool IsInject(Compilation compilation, ISymbol symbol) => IsDefined(compilation, symbol, InjectAttribute);
        private static bool IsDependency(Compilation compilation, ISymbol symbol) => IsDependencyExplicit(compilation, symbol) || IsMixin(compilation, symbol);
        private static bool IsDependencyExplicit(Compilation compilation, ISymbol symbol) => IsDefined(compilation, symbol, DependencyAttribute);
        private static bool IsBaseDependency(Compilation compilation, ISymbol symbol) => IsDefined(compilation, symbol, BaseDependencyAttribute);
        private static bool IsAvailable(Compilation compilation, ISymbol symbol) => IsInject(compilation, symbol) || IsDependency(compilation, symbol) || IsBaseDependency(compilation, symbol) || symbol.ContainingAssembly.Equals(compilation.GetSpecialType(SpecialType.System_Void).ContainingAssembly);
        
        private static LocalizableResourceString Of(string key) => new LocalizableResourceString(key, Resources.ResourceManager, typeof(Resources));

        private static DiagnosticDescriptor Descriptor(string id, DiagnosticSeverity severity, bool isEnabledByDefault) => new DiagnosticDescriptor("Vial" + id, Of("Title" + id), Of("MessageFormat" + id), "Mixins", severity, isEnabledByDefault, Of("Description" + id));
    }
}
