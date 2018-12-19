using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Vial.Analyzer
{
    static class Extensions
    {
        private static readonly SymbolDisplayFormat FullyQualifiedFormat;// = new SymbolDisplayFormat(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

        // Move global::Microsoft.CodeAnalysis.SymbolDisplayCompiler.UseArityForGenericTypes to global::Microsoft.CodeAnalysis.SymbolDisplayGenericOptions or revolt!
        static Extensions() => FullyQualifiedFormat = (SymbolDisplayFormat)typeof(SymbolDisplayFormat).GetTypeInfo().GetDeclaredField("QualifiedNameArityFormat").GetValue(null);

        public static bool HasAttribute(this ISymbol symbol, INamedTypeSymbol attribute) => symbol.GetAttributes().Any(attr => attr.AttributeClass.Equals(attribute));

        public static IEnumerable<AttributeData> GetAttributes(this ISymbol symbol, INamedTypeSymbol attribute) => symbol.GetAttributes().Where(attr => attr.AttributeClass.Equals(attribute));

        public static AttributeData GetAttribute(this ISymbol symbol, INamedTypeSymbol attribute) => symbol.GetAttributes(attribute).FirstOrDefault();

        public static bool IsInterfaceImplementation(this ISymbol symbol) => symbol.ContainingType.AllInterfaces.SelectMany(iface => iface.GetMembers()).Any(m => symbol.ContainingType.FindImplementationForInterfaceMember(m)?.Equals(symbol) ?? false);
        public static IEnumerable<ISymbol> GetImplementedInterfaceMembers(this ISymbol symbol) => symbol.ContainingType.AllInterfaces.SelectMany(iface => iface.GetMembers()).Where(m => symbol.ContainingType.FindImplementationForInterfaceMember(m)?.Equals(symbol) ?? false);
        
        // From the Roslyn source code
        public static IEnumerable<ITypeSymbol> GetBaseTypesAndThis(this ITypeSymbol type)
        {
            while (type != null)
            {
                yield return type;
                type = type.BaseType;
            }
        }

        public static string GetQualifiedName(this ISymbol symbol)
        {
            switch (symbol)
            {
                case INamedTypeSymbol type:
                    return type.GetFullMetadataName();
                default:
                    return symbol.Name;
            }
        }

        // From a Stack Overflow answer I have since lost
        public static string GetFullMetadataName(this ISymbol s)
        {
            if (s == null || IsRootNamespace(s)) return string.Empty;
            StringBuilder sb = new StringBuilder(s.MetadataName);
            ISymbol last = s;
            s = s.ContainingSymbol;
            while (!IsRootNamespace(s))
            {
                if (s is ITypeSymbol && last is ITypeSymbol) sb.Insert(0, '+');
                else sb.Insert(0, '.');
                //sb.Insert(0, s.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
                sb.Insert(0, s.MetadataName);
                s = s.ContainingSymbol;
            }
            return sb.ToString();
        }

        // From the aforementioned Stack Overflow answer
        private static bool IsRootNamespace(ISymbol symbol)
        {
            INamespaceSymbol s = null;
            return ((s = symbol as INamespaceSymbol) != null) && s.IsGlobalNamespace;
        }
    }
}
