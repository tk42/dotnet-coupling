namespace DotnetCoupling.Model;

/// <summary>解析対象の構造単位の種別。</summary>
public enum NodeKind
{
    Solution,
    Project,
    Namespace,
    Type,
    Method,
    ExternalAssembly,
    NuGetPackage
}

/// <summary>依存の種類。Integration Strength の基準値はこの種別で決まる（docs/scoring.md S3）。</summary>
public enum DependencyKind
{
    Inheritance,
    InterfaceImplementation,
    FieldType,
    PropertyType,
    ConstructorParameter,
    MethodParameter,
    ReturnType,
    ObjectCreation,
    MethodCall,
    StaticAccess,
    Attribute,
    GenericArgument,
    UsingDirective,
    DiRegistration,
    Reflection,
    DynamicAccess
}

/// <summary>解析モード。</summary>
public enum AnalysisMode
{
    Semantic,
    SyntaxOnly
}

/// <summary>結果の信頼度。</summary>
public enum ConfidenceLevel
{
    Low,
    Medium,
    High
}
