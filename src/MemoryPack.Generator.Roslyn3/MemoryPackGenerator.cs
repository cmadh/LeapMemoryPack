﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MemoryPack.Generator;

[Generator(LanguageNames.CSharp)]
public partial class MemoryPackGenerator : ISourceGenerator
{
    public const string MemoryPackableAttributeFullName = "MemoryPack.MemoryPackableAttribute";
    public const string GenerateTypeScriptAttributeFullName = "MemoryPack.GenerateTypeScriptAttribute";

    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(SyntaxContextReceiver.Create);
    }

    public void Execute(GeneratorExecutionContext context)
    {
        if (context.SyntaxContextReceiver is not SyntaxContextReceiver receiver || receiver.ClassDeclarations.Count == 0)
        {
            return;
        }

        if (!context.AnalyzerConfigOptions.GlobalOptions.TryGetValue("build_property.MemoryPackGenerator_SerializationInfoOutputDirectory", out var logPath))
        {
            logPath = null;
        }

        var compiation = context.Compilation;
        var generateContext = new GeneratorContext(context);

        if (context.AnalyzerConfigOptions.GlobalOptions.TryGetValue("build_property.MemoryPackGenerator_DebugNonUnityMode", out var nonUnity))
        {
            generateContext.IsForUnity = !bool.Parse(nonUnity);
        }
        if (context.AnalyzerConfigOptions.GlobalOptions.TryGetValue("build_property.MemoryPackGenerator_Use7BitEncodedHeaders", out var use7BitEncodedHeaders))
        {
            generateContext.Use7BitEncodedHeaders = bool.Parse(use7BitEncodedHeaders);
        }
        if (context.AnalyzerConfigOptions.GlobalOptions.TryGetValue("build_property.MemoryPackGenerator_UseObjectHeaders", out var useObjectHeaders))
        {
            generateContext.UseObjectHeaders = bool.Parse(useObjectHeaders);
        }

        foreach (var syntax in receiver.ClassDeclarations)
        {
            Generate(syntax, compiation, logPath, generateContext);
        }
    }

    class SyntaxContextReceiver : ISyntaxContextReceiver
    {
        internal static ISyntaxContextReceiver Create()
        {
            return new SyntaxContextReceiver();
        }

        public HashSet<TypeDeclarationSyntax> ClassDeclarations { get; } = new();

        public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
        {
            var node = context.Node;
            if (node is ClassDeclarationSyntax
                     or StructDeclarationSyntax
                     or RecordDeclarationSyntax
                     or InterfaceDeclarationSyntax)
            {
                var typeSyntax = (TypeDeclarationSyntax)node;
                if (typeSyntax.AttributeLists.Count > 0)
                {
                    var attr = typeSyntax.AttributeLists.SelectMany(x => x.Attributes)
                        .FirstOrDefault(x =>
                        {
                            var packable = x.Name.ToString() is "MemoryPackable" or "MemoryPackableAttribute" or "MemoryPack.MemoryPackable" or "MemoryPack.MemoryPackableAttribute";
                            if (packable) return true;
                            var formatter = x.Name.ToString() is "MemoryPackUnionFormatter" or "MemoryPackUnionFormatterAttribute" or "MemoryPack.MemoryPackUnionFormatter" or "MemoryPack.MemoryPackUnionFormatterAttribute";
                            return formatter;
                        });
                    if (attr != null)
                    {
                        ClassDeclarations.Add(typeSyntax);
                    }
                }
            }
        }
    }

    class GeneratorContext : IGeneratorContext
    {
        GeneratorExecutionContext context;

        public GeneratorContext(GeneratorExecutionContext context)
        {
            this.context = context;
        }

        public CancellationToken CancellationToken => context.CancellationToken;

        public LanguageVersion LanguageVersion => LanguageVersion.CSharp9; // No IncrementalGenerator is C# 9.0

        public bool IsNet7OrGreater => false; // No IncrementalGenerator is always not NET7

        public bool IsForUnity { get; set; } = false;
        public bool Use7BitEncodedHeaders { get; set; } = false;
        public bool UseObjectHeaders { get; set; } = false;

        public void AddSource(string hintName, string source)
        {
            context.AddSource(hintName, source);
        }

        public void ReportDiagnostic(Diagnostic diagnostic)
        {
            context.ReportDiagnostic(diagnostic);
        }
    }
}
