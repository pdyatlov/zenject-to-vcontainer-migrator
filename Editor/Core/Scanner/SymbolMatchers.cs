using Microsoft.CodeAnalysis;

namespace Zenject2VContainer.Core.Scanner {
    public static class SymbolMatchers {
        // Assembly names we treat as "Zenject". Includes Extenject (same shipped name)
        // and the older modesttree fork.
        private static readonly string[] ZenjectAssemblyNames = {
            "Zenject",
            "Zenject-usage",
            "Zenject.ReflectionBaking.Mono"
        };

        public static bool IsZenjectSymbol(ISymbol symbol) {
            if (symbol == null) return false;
            var asm = symbol.ContainingAssembly?.Name;
            if (string.IsNullOrEmpty(asm)) return false;
            foreach (var name in ZenjectAssemblyNames) {
                if (string.Equals(asm, name, System.StringComparison.Ordinal)) return true;
            }
            return false;
        }

        public static bool IsZenjectInjectAttribute(INamedTypeSymbol type) {
            if (type == null) return false;
            return IsZenjectSymbol(type) &&
                   (type.Name == "InjectAttribute" || type.Name == "InjectOptionalAttribute");
        }

        public static bool IsDiContainerType(ITypeSymbol type) {
            if (type == null) return false;
            return IsZenjectSymbol(type) && type.Name == "DiContainer";
        }
    }
}
