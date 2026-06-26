using DotnetCoupling.Analysis;
using DotnetCoupling.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotnetCoupling.Tests;

/// <summary>テスト用: ソース文字列から in-memory コンパイルとグラフを作る。</summary>
internal static class TestCompiler
{
    private static readonly MetadataReference[] Core =
    {
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
    };

    public static CSharpCompilation Compile(string assemblyName, string source, params MetadataReference[] extra)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: assemblyName + ".cs");
        return CSharpCompilation.Create(
            assemblyName,
            new[] { tree },
            Core.Concat(extra),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    public static CouplingGraph BuildGraph(params (string Name, CSharpCompilation Comp)[] projects) =>
        new SemanticGraphBuilder().Build(
            projects.Select(p => new ProjectCompilation(p.Name, p.Comp)).ToList());
}
