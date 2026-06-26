using Microsoft.Build.Locator;

namespace DotnetCoupling.Analysis;

/// <summary>
/// MSBuildWorkspace を使う前に、MSBuild インスタンス（VS / Build Tools の Framework MSBuild）を
/// 1 回だけ登録する。レガシー non-SDK / net472 プロジェクトの読込にはこの登録が必須。
/// 重要: MSBuild / Rosloyn の MSBuild アセンブリが JIT される前に呼ぶこと（docs/implementation-plan.md §0）。
/// </summary>
public static class MsBuildRegistrar
{
    private static readonly object Gate = new();
    private static bool _registered;

    /// <summary>登録したインスタンスの説明（未登録なら null）。</summary>
    public static string? RegisteredDescription { get; private set; }

    /// <summary>未登録なら最も新しい VS/Build Tools インスタンスを登録し、説明文字列を返す。</summary>
    public static string EnsureRegistered()
    {
        lock (Gate)
        {
            if (_registered)
                return RegisteredDescription!;

            if (MSBuildLocator.IsRegistered)
            {
                _registered = true;
                RegisteredDescription = "pre-registered";
                return RegisteredDescription;
            }

            var chosen = MSBuildLocator.QueryVisualStudioInstances()
                .OrderByDescending(i => i.Version)
                .FirstOrDefault();

            if (chosen is null)
            {
                throw new InvalidOperationException(
                    "MSBuild インスタンスが見つかりません。Visual Studio または Build Tools for Visual Studio が必要です。");
            }

            MSBuildLocator.RegisterInstance(chosen);
            _registered = true;
            RegisteredDescription = $"{chosen.Name} {chosen.Version} [{chosen.DiscoveryType}] @ {chosen.MSBuildPath}";
            return RegisteredDescription;
        }
    }
}
