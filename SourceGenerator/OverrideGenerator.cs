﻿using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SourceGenerator;

[Generator]
public class OverrideGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Add our attribute source code (this will be available to the user's code)
        context.RegisterPostInitializationOutput(ctx =>
        {
            ctx.AddSource("GenerateOverrideAttribute.g.cs", SourceText.From(AttributeSource, Encoding.UTF8));
        });

        // Create a SyntaxProvider to find candidate classes with any attribute lists.
        // Later we filter those that have GenerateOverrideAttribute applied.
        var classDeclarations = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (s, _) => IsCandidate(s),
                transform: static (ctx, _) => GetSemanticTargetForGeneration(ctx))
            .Where(static m => m is not null);

        // Combine the selected class symbols with the compilation.
        var compilationAndClasses = context.CompilationProvider.Combine(classDeclarations.Collect());

        // Generate a partial class with override members for each selected class.
        context.RegisterSourceOutput(compilationAndClasses, (spc, source) =>
        {
            var (_, classes) = source;
            foreach (var classSymbol in classes.Distinct(SymbolEqualityComparer.Default).Cast<INamedTypeSymbol>())
            {
                // Process the class to generate its override implementations
                var result = ProcessClass(classSymbol);
                if (!string.IsNullOrWhiteSpace(result))
                {
                    spc.AddSource($"{classSymbol.Name}_Overrides.g.cs", SourceText.From(result, Encoding.UTF8));
                }
            }
        });
    }

    /// <summary>
    /// A simple predicate to filter only class declarations that have at least one attribute.
    /// </summary>
    private static bool IsCandidate(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax classDeclaration && classDeclaration.AttributeLists.Count > 0;
    }

    /// <summary>
    /// For each class candidate, check if it has the [GenerateOverride] attribute.
    /// </summary>
    private static INamedTypeSymbol? GetSemanticTargetForGeneration(GeneratorSyntaxContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        foreach (var attributeList in classDeclaration.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var symbolInfo = context.SemanticModel.GetSymbolInfo(attribute);
                if (symbolInfo.Symbol is IMethodSymbol attributeSymbol)
                {
                    var attributeContainingType = attributeSymbol.ContainingType;
                    if (attributeContainingType.Name == "GenerateOverrideAttribute" ||
                        attributeContainingType.ToDisplayString() == "GenerateOverrideAttribute")
                    {
                        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration);
                        return classSymbol as INamedTypeSymbol;
                    }
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Processes a target class symbol to generate a partial class file that overrides its virtual or abstract methods.
    /// </summary>
    private static string ProcessClass(INamedTypeSymbol classSymbol)
    {
        var typeToUse = classSymbol.BaseType;

        if (typeToUse?.BaseType is null) return string.Empty;

        if (typeToUse.BaseType.Name == typeToUse.Name)
        {
            typeToUse = typeToUse.BaseType;
        }
        
        var sb = new StringBuilder();

        // Define a display format that preserves nullability annotations.
        var format = SymbolDisplayFormat.MinimallyQualifiedFormat
            .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier | SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

        
        // Determine the namespace (if any)
        var namespaceName = classSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : classSymbol.ContainingNamespace.ToDisplayString();
        
        sb.AppendLine($"//Base Type is {typeToUse.Name}");

        if (!string.IsNullOrEmpty(namespaceName))
        {
            sb.AppendLine("""
            // <auto-generated/>
            #nullable enable
            using System.Linq.Expressions;
            using System.Runtime.CompilerServices;
            """);
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");
        }
       

        Dictionary<string, string> genericTypeMaps = new();

        for (var index = 0; index < typeToUse.TypeParameters.Length; index++)
        {
            if (classSymbol.TypeParameters.Length <= index)
            {
                break;
            }
            
            var genericParamIn = typeToUse.TypeParameters[index];
            genericTypeMaps.Add(genericParamIn.Name, classSymbol.TypeParameters[index].Name);
        }

        // Generate the partial class declaration (keeping type parameters out for simplicity)
        sb.AppendLine($"    partial class {classSymbol.Name}");
        sb.AppendLine("    {");

        // Get methods that can be overridden: virtual, abstract or override methods.
        var methods = typeToUse.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => !m.IsStatic && m.MethodKind == MethodKind.Ordinary 
                                    && m.Name != "BindAuthorizationCriteria" 
                                    && m is { IsVirtual: true, DeclaredAccessibility: Accessibility.Protected or Accessibility.Public });
        
        bool hasOverriddenAtLeastOneMethod = false;
        
        foreach (var method in methods)
        {
            // Build the parameters list and the argument list.
            bool hasCallerAttribute = false;
            var parameterStrings = method.Parameters.Select(p =>
            {
                var attributes = p.GetAttributes().Where(x => x.AttributeClass is { Name: "CallerFilePathAttribute" or "CallerLineNumberAttribute" } ).ToArray();

                string attributesText = "";
                if (attributes.Length > 0)
                {
                    hasCallerAttribute = true;
                    attributesText = string.Join(" ", attributes.Select(GetAttributeText));
                }
                
                return $"{attributesText}{p.Type.ToDisplayString(format)} {p.Name}{(p.HasExplicitDefaultValue ? $" = {p.ExplicitDefaultValue?.ToString().ToLower() ?? "null"}" : "")}";
            }).ToList();

            if (!hasCallerAttribute)
            {
               continue;
            }
            
            var parameters =string.Join(", ", parameterStrings);
            var arguments = string.Join(", ", method.Parameters.Select(p => genericTypeMaps.TryGetValue(p.Name, out var map) ? map: p.Name));

            // Get method accessibility.
            var accessibility = method.DeclaredAccessibility.ToString().ToLowerInvariant();

            // Return type and method signature.
            var returnType = method.ReturnType.ToDisplayString(format);

            // If the method itself is generic, get its generic parameters.
            string methodGenericParameters = "";
            if (method.TypeParameters.Length > 0)
            {
                methodGenericParameters = "<" + string.Join(", ", method.TypeParameters.Select(tp => tp.Name)) + ">";
            }

            // Write the override method.
            // sb.AppendLine("        [System.Diagnostics.DebuggerStepThrough]");
            // sb.AppendLine("        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]");
            sb.AppendLine($"        {accessibility} override {(method.IsAsync ? "async" : "")} {returnType} {method.Name}{methodGenericParameters}({parameters})");
            if (returnType.Contains("TProjectionType?"))
            {
                sb.AppendLine("            where TProjectionType: default");
            }
            sb.AppendLine("        {");
            if (returnType is "void" or "Task")
            {
                sb.AppendLine($"            {(method.IsAsync? "await " : "") }base.{method.Name}({arguments});");
            }
            else
            {
                sb.AppendLine($"            return {(method.IsAsync? "await " : "") }base.{method.Name}({arguments});");
            }
            sb.AppendLine("        }");
            sb.AppendLine();
            
            hasOverriddenAtLeastOneMethod = true;
        }
        
        if (!hasOverriddenAtLeastOneMethod)
        {
            return string.Empty;
        }

        sb.AppendLine("    }");

        if (!string.IsNullOrEmpty(namespaceName))
        {
            sb.AppendLine("}");
        }

        return sb.ToString();
    }
    
    
    // Helper method to convert an attribute to a string representation.
    // This implementation simply takes the attribute class name and strips the "Attribute" suffix.
    private static string GetAttributeText(AttributeData attributeData)
    {
        if (attributeData.AttributeClass is null)
            return "";
            
        var name = attributeData.AttributeClass.Name;
        if (name.EndsWith("Attribute"))
        {
            name = name.Substring(0, name.Length - "Attribute".Length);
        }
        return $"[{name}]";
    }

    // The source code for the attribute that triggers generation.
    private const string AttributeSource = @"// <auto-generated/>
using System;
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class GenerateOverrideAttribute : Attribute
{
}
";
}