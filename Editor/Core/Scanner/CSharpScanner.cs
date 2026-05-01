using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.Core.Scanner {
    public static class CSharpScanner {
        public static IEnumerable<CSharpFinding> Scan(CSharpCompilation compilation) {
            foreach (var tree in compilation.SyntaxTrees) {
                var model = compilation.GetSemanticModel(tree);
                var root = tree.GetRoot();

                foreach (var u in root.DescendantNodes().OfType<UsingDirectiveSyntax>()) {
                    var name = u.Name?.ToString();
                    if (string.Equals(name, "Zenject", System.StringComparison.Ordinal)) {
                        yield return Make(tree, u, CSharpFindingKind.UsingDirective,
                            "Zenject", "using Zenject;", "High");
                    }
                }

                foreach (var attr in root.DescendantNodes().OfType<AttributeSyntax>()) {
                    var symbol = model.GetSymbolInfo(attr).Symbol?.ContainingType;
                    if (!SymbolMatchers.IsZenjectInjectAttribute(symbol)) continue;
                    yield return Make(tree, attr, CSharpFindingKind.InjectAttribute,
                        symbol.ToDisplayString(), attr.Parent?.ToString() ?? attr.ToString(),
                        symbol.Name == "InjectOptionalAttribute" ? "LowFlagged" : "High");
                }

                foreach (var cls in root.DescendantNodes().OfType<ClassDeclarationSyntax>()) {
                    var symbol = model.GetDeclaredSymbol(cls);
                    if (symbol == null) continue;
                    var basesAreZenject = false;
                    for (var b = symbol.BaseType; b != null; b = b.BaseType) {
                        if (!SymbolMatchers.IsZenjectSymbol(b)) continue;
                        if (b.Name == "MonoInstaller" || b.Name == "Installer" ||
                            b.Name == "ScriptableObjectInstaller") {
                            basesAreZenject = true;
                            break;
                        }
                    }
                    if (basesAreZenject) {
                        yield return Make(tree, cls, CSharpFindingKind.InstallerSubclass,
                            symbol.ToDisplayString(),
                            cls.Identifier.ToString(), "High");
                    }
                }

                foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
                    var symbol = model.GetSymbolInfo(inv).Symbol;
                    if (symbol == null) continue;
                    if (!SymbolMatchers.IsZenjectSymbol(symbol)) continue;
                    if (!IsBindOrFactoryRoot(symbol.Name)) continue;
                    yield return Make(tree, inv, CSharpFindingKind.BindCall,
                        symbol.ToDisplayString(),
                        inv.ToString(), "High");
                }
            }
        }

        private static bool IsBindOrFactoryRoot(string name) {
            switch (name) {
                case "Bind":
                case "BindInterfacesTo":
                case "BindInterfacesAndSelfTo":
                case "BindFactory":
                    return true;
                default:
                    return false;
            }
        }

        private static CSharpFinding Make(SyntaxTree tree, SyntaxNode node,
                                          CSharpFindingKind kind, string symbolName,
                                          string snippet, string confidence) {
            var pos = tree.GetLineSpan(node.Span);
            return new CSharpFinding {
                FilePath = tree.FilePath,
                Line = pos.StartLinePosition.Line + 1,
                Column = pos.StartLinePosition.Character + 1,
                Kind = kind,
                SymbolName = symbolName,
                Snippet = snippet.Length > 200 ? snippet.Substring(0, 200) : snippet,
                Confidence = confidence,
                Notes = ""
            };
        }
    }
}
