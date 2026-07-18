using DotnetCoupling.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotnetCoupling.Analysis;

/// <summary>型から型への 1 つの依存出現（semantic 解決済み）。</summary>
public sealed record TypeDependency(
    INamedTypeSymbol Source,
    INamedTypeSymbol Target,
    DependencyKind Kind,
    string? FilePath,
    int? Line);

/// <summary>
/// 1 つの <see cref="Compilation"/> を走査し、型レベルの依存（DependencyKind 付き）を抽出する。
/// MSBuild には依存しないため、in-memory コンパイルでも決定論的にテストできる。
/// </summary>
public sealed class CouplingWalker
{
    public IReadOnlyList<TypeDependency> Walk(Compilation compilation, CancellationToken ct = default)
    {
        var deps = new List<TypeDependency>();

        // 宣言由来の依存（継承・実装・field/property 型・引数/戻り値型）。
        foreach (var type in GetSourceNamedTypes(compilation))
        {
            ct.ThrowIfCancellationRequested();
            CollectDeclarationDependencies(type, deps);
        }

        // 本体由来の依存（object creation・method call）。
        foreach (var tree in compilation.SyntaxTrees)
        {
            ct.ThrowIfCancellationRequested();
            var model = compilation.GetSemanticModel(tree);
            CollectBodyDependencies(tree, model, deps, ct);
        }

        return deps;
    }

    private static IEnumerable<INamedTypeSymbol> GetSourceNamedTypes(Compilation compilation)
    {
        var stack = new Stack<INamespaceOrTypeSymbol>();
        stack.Push(compilation.Assembly.GlobalNamespace);
        while (stack.Count > 0)
        {
            foreach (var member in stack.Pop().GetMembers())
            {
                switch (member)
                {
                    case INamespaceSymbol ns:
                        stack.Push(ns);
                        break;
                    case INamedTypeSymbol type:
                        if (IsAnalyzableSource(type))
                            yield return type;
                        stack.Push(type); // ネスト型を辿る
                        break;
                }
            }
        }
    }

    private static bool IsAnalyzableSource(INamedTypeSymbol type) =>
        !type.IsImplicitlyDeclared
        && type.TypeKind is TypeKind.Class or TypeKind.Struct or TypeKind.Interface
        && type.Locations.Any(l => l.IsInSource);

    private static void CollectDeclarationDependencies(INamedTypeSymbol type, List<TypeDependency> deps)
    {
        var typeLoc = PrimaryLocation(type);

        if (type.TypeKind == TypeKind.Class && type.BaseType is { SpecialType: SpecialType.None } baseType)
            Add(deps, type, baseType, DependencyKind.Inheritance, typeLoc);

        foreach (var iface in type.Interfaces)
            Add(deps, type, iface, DependencyKind.InterfaceImplementation, typeLoc);

        // 型レベルの属性適用。
        foreach (var attr in type.GetAttributes())
            AddAttribute(deps, type, attr, typeLoc);

        foreach (var member in type.GetMembers())
        {
            if (member.IsImplicitlyDeclared)
                continue;

            // メンバーレベル（メソッド / プロパティ / フィールド）の属性適用。
            foreach (var attr in member.GetAttributes())
                AddAttribute(deps, type, attr, PrimaryLocation(member));

            switch (member)
            {
                case IFieldSymbol { IsImplicitlyDeclared: false } field:
                    Add(deps, type, field.Type, DependencyKind.FieldType, PrimaryLocation(field));
                    break;

                case IPropertySymbol { IsImplicitlyDeclared: false } property:
                    Add(deps, type, property.Type, DependencyKind.PropertyType, PrimaryLocation(property));
                    break;

                case IMethodSymbol method when IsRealMethod(method):
                    var paramKind = method.MethodKind == MethodKind.Constructor
                        ? DependencyKind.ConstructorParameter
                        : DependencyKind.MethodParameter;
                    var methodLoc = PrimaryLocation(method);
                    foreach (var parameter in method.Parameters)
                        Add(deps, type, parameter.Type, paramKind, methodLoc);
                    if (method.MethodKind != MethodKind.Constructor && !method.ReturnsVoid)
                        Add(deps, type, method.ReturnType, DependencyKind.ReturnType, methodLoc);
                    break;
            }
        }
    }

    private static bool IsRealMethod(IMethodSymbol method) =>
        !method.IsImplicitlyDeclared
        && method.MethodKind is MethodKind.Ordinary or MethodKind.Constructor;

    private static void CollectBodyDependencies(
        SyntaxTree tree, SemanticModel model, List<TypeDependency> deps, CancellationToken ct)
    {
        foreach (var node in tree.GetRoot(ct).DescendantNodes())
        {
            switch (node)
            {
                case ObjectCreationExpressionSyntax creation:
                {
                    var source = EnclosingType(creation, model);
                    var created = (model.GetSymbolInfo(creation).Symbol as IMethodSymbol)?.ContainingType
                                  ?? model.GetTypeInfo(creation).Type;
                    if (source is not null)
                        Add(deps, source, created, DependencyKind.ObjectCreation, node.GetLocation());
                    break;
                }

                case InvocationExpressionSyntax invocation:
                {
                    if (model.GetSymbolInfo(invocation).Symbol is IMethodSymbol called)
                    {
                        var source = EnclosingType(invocation, model);
                        if (source is not null)
                        {
                            Add(deps, source, called.ContainingType, DependencyKind.MethodCall, node.GetLocation());
                            CollectDiRegistration(invocation, called, source, model, deps, node.GetLocation());
                        }
                    }
                    break;
                }

                // 静的フィールド/プロパティへのアクセス（静的メソッド呼び出しは MethodCall のまま）。
                case MemberAccessExpressionSyntax memberAccess:
                {
                    var accessed = model.GetSymbolInfo(memberAccess).Symbol;
                    if (accessed is IFieldSymbol { IsStatic: true } or IPropertySymbol { IsStatic: true })
                    {
                        var source = EnclosingType(memberAccess, model);
                        if (source is not null)
                            Add(deps, source, accessed.ContainingType, DependencyKind.StaticAccess, node.GetLocation());
                    }
                    break;
                }
            }
        }
    }

    private static readonly HashSet<string> DiRegistrationMethods = new()
    {
        "AddScoped", "AddSingleton", "AddTransient"
    };

    /// <summary>
    /// MS.Extensions.DependencyInjection の登録（AddScoped/Singleton/Transient）を検出し、
    /// 合成起点（登録呼び出しの外側の型）→ 具象実装型を DiRegistration として記録する。
    /// </summary>
    private static void CollectDiRegistration(
        InvocationExpressionSyntax invocation, IMethodSymbol called, INamedTypeSymbol source,
        SemanticModel model, List<TypeDependency> deps, Location? loc)
    {
        if (!DiRegistrationMethods.Contains(called.Name))
            return;
        if (!IsServiceCollection(called.ReceiverType))
            return;

        var implementation = ResolveDiImplementation(called, invocation, model);
        if (implementation is not null)
            Add(deps, source, implementation, DependencyKind.DiRegistration, loc);
    }

    /// <summary>
    /// 登録の具象実装型を特定する。
    /// ジェネリック 2 型引数 = AddScoped&lt;IFoo, Foo&gt;() → Foo、
    /// ジェネリック 1 型引数 = AddScoped&lt;Foo&gt;()（自己束縛）→ Foo、
    /// typeof ペア = AddScoped(typeof(IFoo), typeof(Foo)) → Foo。
    /// </summary>
    private static ITypeSymbol? ResolveDiImplementation(
        IMethodSymbol called, InvocationExpressionSyntax invocation, SemanticModel model)
    {
        if (called.TypeArguments.Length == 2)
            return called.TypeArguments[1];
        if (called.TypeArguments.Length == 1)
            return called.TypeArguments[0];

        var typeofTargets = invocation.ArgumentList.Arguments
            .Select(a => a.Expression)
            .OfType<TypeOfExpressionSyntax>()
            .Select(t => model.GetTypeInfo(t.Type).Type)
            .Where(t => t is not null)
            .ToList();
        if (typeofTargets.Count >= 2)
            return typeofTargets[1];
        if (typeofTargets.Count == 1)
            return typeofTargets[0];

        return null;
    }

    private static bool IsServiceCollection(ITypeSymbol? type)
    {
        if (type is null)
            return false;
        if (IsServiceCollectionNamed(type))
            return true;
        foreach (var iface in type.AllInterfaces)
            if (IsServiceCollectionNamed(iface))
                return true;
        return false;
    }

    private static bool IsServiceCollectionNamed(ITypeSymbol type) =>
        type.Name == "IServiceCollection"
        && type.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.DependencyInjection";

    private static void AddAttribute(
        List<TypeDependency> deps, INamedTypeSymbol source, AttributeData attr, Location? fallback)
    {
        if (attr.AttributeClass is not { } attrClass)
            return;
        var loc = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? fallback;
        Add(deps, source, attrClass, DependencyKind.Attribute, loc);
    }

    private static INamedTypeSymbol? EnclosingType(SyntaxNode node, SemanticModel model)
    {
        var declaration = node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        return declaration is null ? null : model.GetDeclaredSymbol(declaration) as INamedTypeSymbol;
    }

    private static void Add(
        List<TypeDependency> deps, INamedTypeSymbol source, ITypeSymbol? rawTarget, DependencyKind kind, Location? loc)
    {
        var (file, line) = Where(loc);

        foreach (var target in ResolveNamedTypes(rawTarget))
        {
            if (IsInterestingTarget(source, target))
                deps.Add(new TypeDependency(source, target, kind, file, line));
        }

        // ジェネリック型引数は文脈 kind に依らず GenericArgument として拾う。
        // 例: field List<Order> は container List（FieldType）に加え Order（GenericArgument）を記録する。
        foreach (var arg in CollectGenericArguments(rawTarget))
        {
            if (IsInterestingTarget(source, arg))
                deps.Add(new TypeDependency(source, arg, DependencyKind.GenericArgument, file, line));
        }
    }

    private static IEnumerable<INamedTypeSymbol> ResolveNamedTypes(ITypeSymbol? type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                foreach (var inner in ResolveNamedTypes(array.ElementType))
                    yield return inner;
                break;
            case INamedTypeSymbol named:
                yield return (INamedTypeSymbol)named.OriginalDefinition;
                break;
        }
    }

    /// <summary>ジェネリック型引数を再帰的に列挙する（ネスト・配列要素も辿る）。</summary>
    private static IEnumerable<INamedTypeSymbol> CollectGenericArguments(ITypeSymbol? type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                foreach (var inner in CollectGenericArguments(array.ElementType))
                    yield return inner;
                break;
            case INamedTypeSymbol named:
                foreach (var arg in named.TypeArguments)
                {
                    foreach (var resolved in ResolveNamedTypes(arg))
                        yield return resolved;
                    foreach (var nested in CollectGenericArguments(arg))
                        yield return nested;
                }
                break;
        }
    }

    private static bool IsInterestingTarget(INamedTypeSymbol source, INamedTypeSymbol target) =>
        target.TypeKind is TypeKind.Class or TypeKind.Struct or TypeKind.Interface
                or TypeKind.Enum or TypeKind.Delegate
        && target.SpecialType == SpecialType.None
        && !SymbolEqualityComparer.Default.Equals(source, target);

    private static Location? PrimaryLocation(ISymbol symbol) =>
        symbol.Locations.FirstOrDefault(l => l.IsInSource) ?? symbol.Locations.FirstOrDefault();

    private static (string? File, int? Line) Where(Location? loc)
    {
        if (loc is null || !loc.IsInSource)
            return (null, null);
        var span = loc.GetLineSpan();
        return (span.Path, span.StartLinePosition.Line + 1);
    }
}
